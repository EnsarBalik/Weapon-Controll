using System;
using UnityEngine;
using WeaponControl.CameraSystem;
using WeaponControl.Combat;
using WeaponControl.Core;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Core hitscan firing logic: fire modes (semi / burst / auto), rate of fire, spread,
    /// raycast damage, impact force, and effects (tracer, muzzle flash, impact). Triggers
    /// <see cref="WeaponRecoil"/> per shot.
    ///
    /// Attach to the weapon object (the AKM, alongside <see cref="WeaponReferences"/>).
    ///
    /// Phase 7 hooks in ammo via <see cref="CanFire"/> and <see cref="AmmoConsumed"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponFire : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private WeaponData data;

        [Header("References (auto-filled if left empty)")]
        [Tooltip("Camera used as the aiming origin/direction (screen center).")]
        [SerializeField] private Transform aimCamera;
        [SerializeField] private WeaponReferences weapon;
        [SerializeField] private WeaponRecoil recoil;
        [SerializeField] private WeaponProceduralMotion motion;
        [SerializeField] private CameraController cameraController;

        [Header("Effects")]
        [Tooltip("Muzzle flash VFX prefab. Instantiated under the muzzle on every shot. " +
                 "If empty, the prefab on Weapon Data is used.")]
        [SerializeField] private GameObject muzzleFlashPrefab;
        [Tooltip("Optional prefab spawned at impact points, oriented to the surface normal.")]
        [SerializeField] private GameObject impactPrefab;
        [SerializeField] private float impactLifetime = 5f;

        [Header("Tracer (built-in line)")]
        [SerializeField] private bool drawTracer = true;
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.4f);
        [Tooltip("Tracer thickness in world units (scale-dependent).")]
        [SerializeField] private float tracerWidth = 0.03f;
        [SerializeField] private float tracerDuration = 0.04f;

        // ---- Ammo integration hooks (set by Phase 7) ----
        /// <summary>Return false to block firing (e.g. empty magazine or reloading).</summary>
        public Func<bool> CanFire;
        /// <summary>Called once per successfully fired round (decrement magazine here).</summary>
        public Action AmmoConsumed;
        /// <summary>Raised when the trigger is pulled but firing is blocked (dry fire / empty).</summary>
        public event Action DryFired;

        /// <summary>Raised for every fired round (for UI, audio, ammo count, etc.).</summary>
        public event Action Fired;
        /// <summary>Raised when a shot hits something that implements <see cref="IDamageable"/>.</summary>
        public event Action<DamageInfo, bool> HitConfirmed;
        /// <summary>Raised when the active fire mode changes.</summary>
        public event Action<FireMode> FireModeChanged;

        public WeaponData Data => data;
        public FireMode CurrentMode { get; private set; }

        /// <summary>Current cone half-angle in degrees, including ADS blend.</summary>
        public float CurrentSpread
        {
            get
            {
                if (data == null) return 0f;
                if (motion == null) return data.hipSpread;
                return Mathf.Lerp(data.hipSpread, data.aimSpread, motion.AimBlend);
            }
        }

        /// <summary>When true, firing and mode-toggle input is ignored (e.g. during a weapon switch).</summary>
        public bool InputLocked { get; set; }

        private int _modeIndex;
        private float _nextFireTime;
        private int _burstRemaining;
        private float _burstTimer;
        private bool _triggerHeldLastFrame;

        private Material _tracerMaterial;

        private const string MuzzleFlashAssetPath =
            "Assets/BigRookGames/_AssetPacks/Stylized Weapon Pack/M4 Scoped Assault Rifle/Prefabs/VFX_M4 Muzzle Flash.prefab";

        private void Awake()
        {
            if (aimCamera == null)
            {
                var cam = GetComponentInParent<Camera>();
                aimCamera = cam != null ? cam.transform : (Camera.main != null ? Camera.main.transform : null);
            }
            if (weapon == null) weapon = GetComponent<WeaponReferences>() ?? GetComponentInChildren<WeaponReferences>(true);
            if (recoil == null) recoil = GetComponentInParent<WeaponRecoil>();
            if (motion == null) motion = GetComponentInParent<WeaponProceduralMotion>();
            if (cameraController == null)
                cameraController = aimCamera != null ? aimCamera.GetComponent<CameraController>() : GetComponentInParent<CameraController>();

            if (data != null && data.availableModes != null && data.availableModes.Length > 0)
            {
                _modeIndex = 0;
                CurrentMode = data.availableModes[0];
            }
        }

        private void PlayMuzzleFlash()
        {
            GameObject prefab = ResolveMuzzleFlashPrefab();
            Transform muzzle = ResolveMuzzle();
            if (prefab == null || muzzle == null) return;

            // This VFX is authored to be instantiated per shot: the root particle uses
            // Stop Action = Destroy, so a persistent instance would vanish immediately.
            var flash = Instantiate(prefab, muzzle);
            flash.name = "MuzzleFlash";
            flash.transform.localPosition = Vector3.zero;
            // Prefab is authored at Y = -90 so emission follows muzzle.forward, not +X.

            var root = flash.GetComponent<ParticleSystem>();
            if (root != null)
            {
                root.Play(true);
            }
            else
            {
                var systems = flash.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                    systems[i].Play(false);
            }
        }

        private GameObject ResolveMuzzleFlashPrefab()
        {
            if (muzzleFlashPrefab != null) return muzzleFlashPrefab;
            if (data != null && data.muzzleFlashPrefab != null) return data.muzzleFlashPrefab;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFlashAssetPath);
#else
            return null;
#endif
        }

        private Transform ResolveMuzzle()
        {
            if (weapon == null)
                weapon = GetComponent<WeaponReferences>() ?? GetComponentInChildren<WeaponReferences>(true);
            if (weapon != null && weapon.Muzzle != null) return weapon.Muzzle;
            Transform named = transform.Find("Muzzle");
            return named != null ? named : transform;
        }

        private void Update()
        {
            if (data == null) return;

            if (InputLocked)
            {
                _triggerHeldLastFrame = GameInput.Player.Attack.IsPressed();
                _burstRemaining = 0;
                return;
            }

            HandleModeToggle();
            HandleFireInput();
            HandleBurst();
        }

        private void HandleModeToggle()
        {
            if (data.availableModes == null || data.availableModes.Length <= 1) return;

            if (GameInput.Player.FireMode.WasPressedThisFrame())
            {
                _modeIndex = (_modeIndex + 1) % data.availableModes.Length;
                CurrentMode = data.availableModes[_modeIndex];
                FireModeChanged?.Invoke(CurrentMode);
            }
        }

        private void HandleFireInput()
        {
            bool held = GameInput.Player.Attack.IsPressed();
            bool pressedThisFrame = held && !_triggerHeldLastFrame;
            _triggerHeldLastFrame = held;

            switch (CurrentMode)
            {
                case FireMode.Auto:
                    if (held) TryFire();
                    break;

                case FireMode.Semi:
                    if (pressedThisFrame) TryFire();
                    break;

                case FireMode.Burst:
                    if (pressedThisFrame && _burstRemaining == 0)
                    {
                        _burstRemaining = Mathf.Max(1, data.burstCount);
                        _burstTimer = 0f;
                    }
                    break;
            }
        }

        private void HandleBurst()
        {
            if (_burstRemaining <= 0) return;

            _burstTimer -= Time.deltaTime;
            if (_burstTimer <= 0f && TryFire())
                _burstRemaining--;
        }

        /// <summary>Attempts to fire respecting rate of fire and ammo. Returns true if a shot was fired.</summary>
        private bool TryFire()
        {
            if (Time.time < _nextFireTime) return false;

            // Ammo / block check (Phase 7). When no hook is set, firing is unrestricted.
            if (CanFire != null && !CanFire())
            {
                // Only report dry fire once per trigger cadence to avoid spamming.
                _nextFireTime = Time.time + 60f / Mathf.Max(1f, data.fireRate);
                _burstRemaining = 0;
                DryFired?.Invoke();
                return false;
            }

            _nextFireTime = Time.time + 60f / Mathf.Max(1f, data.fireRate);
            FireOneRound();
            return true;
        }

        private void FireOneRound()
        {
            if (aimCamera == null) return;

            float spread = data.hipSpread;
            if (motion != null)
                spread = Mathf.Lerp(data.hipSpread, data.aimSpread, motion.AimBlend);

            Vector3 origin = aimCamera.position;
            Vector3 dir = GetSpreadDirection(aimCamera.forward, spread);

            Vector3 endPoint = origin + dir * data.range;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, data.range, data.hitMask, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;
                ApplyHit(hit, dir);
            }

            // Muzzle-based visuals
            Vector3 tracerStart = weapon != null && weapon.Muzzle != null ? weapon.Muzzle.position : origin;
            SpawnTracer(tracerStart, endPoint);
            PlayMuzzleFlash();

            recoil?.ApplyShot();
            motion?.CancelInspect();
            if (cameraController != null && data != null)
                cameraController.AddShake(data.fireShake);
            AmmoConsumed?.Invoke();
            Fired?.Invoke();
        }

        private void ApplyHit(RaycastHit hit, Vector3 dir)
        {
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                var info = new DamageInfo
                {
                    Amount = data.damage,
                    Point = hit.point,
                    Normal = hit.normal,
                    Direction = dir,
                    Source = gameObject
                };
                damageable.TakeDamage(info);

                var health = hit.collider.GetComponentInParent<Health>();
                bool killed = health != null && health.IsDead;
                HitConfirmed?.Invoke(info, killed);
            }

            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(dir * data.impactForce, hit.point, ForceMode.Impulse);

            if (impactPrefab != null)
            {
                var fx = Instantiate(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                Destroy(fx, impactLifetime);
            }
        }

        private Vector3 GetSpreadDirection(Vector3 forward, float spreadDegrees)
        {
            if (spreadDegrees <= 0f) return forward;
            Vector2 rnd = UnityEngine.Random.insideUnitCircle * spreadDegrees;
            return Quaternion.AngleAxis(rnd.x, aimCamera.up) *
                   Quaternion.AngleAxis(rnd.y, aimCamera.right) *
                   forward;
        }

        private void SpawnTracer(Vector3 start, Vector3 end)
        {
            if (!drawTracer) return;

            if (_tracerMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _tracerMaterial = new Material(shader);
            }

            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = _tracerMaterial;
            lr.startColor = lr.endColor = tracerColor;
            lr.startWidth = lr.endWidth = tracerWidth;
            lr.numCapVertices = 0;
            lr.textureMode = LineTextureMode.Stretch;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Destroy(go, tracerDuration);
        }

        private void OnDestroy()
        {
            if (_tracerMaterial != null) Destroy(_tracerMaterial);
        }
    }
}
