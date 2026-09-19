using UnityEngine;
using WeaponControl.CameraSystem;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Procedural magazine-swap reload animation. One instance lives on the weapon holder; it binds
    /// to the active weapon's <see cref="WeaponAmmo"/> reload events and, for weapons that carry a
    /// <see cref="WeaponReloadRig"/>, animates:
    ///   * the in-well (old) magazine dropping out of the well,
    ///   * a spare (new) magazine appearing low and inserting into the well,
    ///   * the support hand following the magazine through the whole sequence.
    ///
    /// Timing is normalized to the reload duration, so tactical and empty reloads both fit. Weapons
    /// without a rig fall back to the existing static reload pose (driven by WeaponAmmo) and this
    /// component simply does nothing for them.
    ///
    /// Runs after weapon motion (order 0) and before <see cref="HandPoseDriver"/> (order 100) so the
    /// hand override it publishes each frame is applied the same frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponReloadAnimator : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private WeaponInventory inventory;
        [SerializeField] private HandPoseDriver handDriver;
        [SerializeField] private WeaponProceduralMotion motion;
        [SerializeField] private CameraController cameraController;

        [Header("Polish")]
        [Tooltip("Camera kick when the fresh mag seats.")]
        [SerializeField] private float seatShake = 0.4f;
        [Tooltip("Camera kick when the charging handle slams forward (empty reload).")]
        [SerializeField] private float rackShake = 0.9f;
        [Tooltip("Spawn a physics copy of the ejected magazine that falls to the floor.")]
        [SerializeField] private bool spawnDroppedMagazine = true;
        [Tooltip("Seconds before the dropped magazine is destroyed.")]
        [SerializeField] private float droppedMagLifetime = 4f;

        [Header("Weapon raise")]
        [Tooltip("Weapon is fully raised by this point, then held until the mag is seated.")]
        [SerializeField, Range(0f, 1f)] private float raiseUpEnd = 0.15f;

        [Header("Phase timing (normalized 0..1 of the reload duration)")]
        [Tooltip("Old mag starts sliding out of the well.")]
        [SerializeField, Range(0f, 1f)] private float ejectStart = 0.18f;
        [Tooltip("Old mag is fully out (then hidden - 'dropped').")]
        [SerializeField, Range(0f, 1f)] private float ejectEnd = 0.40f;
        [Tooltip("Fresh mag appears in the hand, down at the pouch.")]
        [SerializeField, Range(0f, 1f)] private float spareAppear = 0.50f;
        [Tooltip("Fresh mag starts moving up into the well.")]
        [SerializeField, Range(0f, 1f)] private float insertStart = 0.60f;
        [Tooltip("Fresh mag is fully seated; hand starts returning to the grip.")]
        [SerializeField, Range(0f, 1f)] private float insertEnd = 0.85f;

        [Header("Charging handle / slide (empty reloads only)")]
        [Tooltip("Charging handle is fully pulled back by this point.")]
        [SerializeField, Range(0f, 1f)] private float chargeBackEnd = 0.90f;
        [Tooltip("Charging handle has snapped forward again by this point.")]
        [SerializeField, Range(0f, 1f)] private float chargeReleaseEnd = 0.96f;

        private WeaponAmmo _boundAmmo;
        private WeaponReloadRig _rig;
        private WeaponAudio _audio;
        private bool _reloading;
        private bool _empty;
        private float _t;
        private float _prevT;
        private float _duration;

        // True when the current reload should play the charging-handle rack.
        private bool Racking => _empty && _rig != null && _rig.HasChargingHandle;

        private void Awake()
        {
            if (inventory == null) inventory = FindFirstObjectByType<WeaponInventory>();
            if (handDriver == null) handDriver = FindFirstObjectByType<HandPoseDriver>();
            if (motion == null) motion = FindFirstObjectByType<WeaponProceduralMotion>();
            if (cameraController == null) cameraController = FindFirstObjectByType<CameraController>();
        }

        private void OnEnable()
        {
            if (inventory != null) inventory.WeaponChanged += OnWeaponChanged;
        }

        private void OnDisable()
        {
            if (inventory != null) inventory.WeaponChanged -= OnWeaponChanged;
            Unbind();
        }

        private void Start()
        {
            if (inventory != null && inventory.CurrentFire != null)
                Bind(inventory.CurrentFire.GetComponent<WeaponAmmo>());
        }

        private void OnWeaponChanged(int index, WeaponFire fire)
        {
            Bind(fire != null ? fire.GetComponent<WeaponAmmo>() : null);
        }

        private void Bind(WeaponAmmo ammo)
        {
            if (_boundAmmo == ammo) return;
            Unbind();
            _boundAmmo = ammo;
            if (_boundAmmo != null)
            {
                _boundAmmo.ReloadStarted += OnReloadStarted;
                _boundAmmo.ReloadCompleted += OnReloadEnded;
                _boundAmmo.ReloadCancelled += OnReloadEnded;
            }
        }

        private void Unbind()
        {
            if (_boundAmmo != null)
            {
                _boundAmmo.ReloadStarted -= OnReloadStarted;
                _boundAmmo.ReloadCompleted -= OnReloadEnded;
                _boundAmmo.ReloadCancelled -= OnReloadEnded;
            }
            _boundAmmo = null;
        }

        private void OnReloadStarted(float duration)
        {
            _rig = _boundAmmo != null ? _boundAmmo.GetComponent<WeaponReloadRig>() : null;
            if (_rig == null || _rig.InWellMagazine == null) { _rig = null; return; }

            _rig.CaptureHome();
            _audio = _boundAmmo != null ? _boundAmmo.GetComponent<WeaponAudio>() : null;
            _reloading = true;
            _empty = _boundAmmo != null && _boundAmmo.IsEmptyReload;
            _t = 0f;
            _prevT = 0f;
            _duration = Mathf.Max(0.05f, duration);

            SetActive(_rig.InWellMagazine, true);
            SetActive(_rig.SpareMagazine, false);
            _rig.PlaceCharge(0f);
        }

        private void OnReloadEnded() => EndAndRestore();

        private void EndAndRestore()
        {
            _reloading = false;
            if (_rig != null)
            {
                // Seat the (new) magazine at home and hide the spare, whatever phase we stopped in.
                if (_rig.InWellMagazine != null && _rig.TryGetHome(out Vector3 hp, out Quaternion hr))
                    _rig.PlaceMag(_rig.InWellMagazine, hp, hr);
                SetActive(_rig.InWellMagazine, true);
                SetActive(_rig.SpareMagazine, false);
                _rig.PlaceCharge(0f);
            }
            handDriver?.ClearSupportOverride();
            motion?.ClearReloadWeightOverride();
            _rig = null;
        }

        private void LateUpdate()
        {
            if (!_reloading || _rig == null) return;

            _t += Time.deltaTime / _duration;
            if (_t >= 1f) { HandleCues(); EndAndRestore(); return; }

            HandleCues();
            motion?.SetReloadWeight(RaiseWeight(_t));
            AnimateMagazines();
            AnimateChargingHandle();
            AnimateHand();
            _prevT = _t;
        }

        /// <summary>Fires one-shot audio / camera kicks / the dropped mag as the timeline passes each cue.</summary>
        private void HandleCues()
        {
            if (Crossed(ejectStart))
                _audio?.PlayMagOut();

            if (Crossed(ejectEnd) && spawnDroppedMagazine)
                SpawnDroppedMagazine();

            if (Crossed(insertEnd))
            {
                _audio?.PlayMagIn();
                if (cameraController != null && seatShake > 0f) cameraController.AddShake(seatShake);
            }

            if (Racking && Crossed(chargeReleaseEnd))
            {
                _audio?.PlayRack();
                if (cameraController != null && rackShake > 0f) cameraController.AddShake(rackShake);
            }
        }

        /// <summary>True the frame the normalized time steps past a threshold.</summary>
        private bool Crossed(float threshold) => _prevT < threshold && _t >= threshold;

        private void SpawnDroppedMagazine()
        {
            Transform src = _rig != null ? _rig.InWellMagazine : null;
            if (src == null) return;

            var clone = Instantiate(src.gameObject, src.position, src.rotation);
            clone.name = "DroppedMagazine";
            clone.transform.SetParent(null, true);
            clone.transform.localScale = src.lossyScale; // keep the on-screen size after un-parenting
            clone.SetActive(true);

            // Strip any gameplay components that rode along on the clone (keep only visuals).
            foreach (var mono in clone.GetComponentsInChildren<MonoBehaviour>(true))
                Destroy(mono);

            if (clone.GetComponentInChildren<Collider>() == null)
            {
                var box = clone.AddComponent<BoxCollider>();
                var mr = clone.GetComponentInChildren<Renderer>();
                if (mr != null)
                {
                    box.center = clone.transform.InverseTransformPoint(mr.bounds.center);
                    Vector3 s = clone.transform.InverseTransformVector(mr.bounds.size);
                    box.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                }
            }

            var rb = clone.GetComponent<Rigidbody>();
            if (rb == null) rb = clone.AddComponent<Rigidbody>();
            rb.useGravity = true;
            // Toss down and slightly back/out with a little spin.
            rb.linearVelocity = -src.up * 1.2f + src.forward * -0.3f;
            rb.angularVelocity = new Vector3(Random.Range(-4f, 4f), Random.Range(-4f, 4f), Random.Range(-4f, 4f));

            Destroy(clone, droppedMagLifetime);
        }

        /// <summary>Raise up early, hold while swapping (and racking), then lower back at the end.</summary>
        private float RaiseWeight(float t)
        {
            float lowerStart = Racking ? chargeReleaseEnd : insertEnd;
            if (t < raiseUpEnd) return Smooth(Mathf.InverseLerp(0f, raiseUpEnd, t));
            if (t < lowerStart) return 1f;
            return 1f - Smooth(Mathf.InverseLerp(lowerStart, 1f, t));
        }

        /// <summary>Pull amount (0 seated .. 1 fully back) for the charging handle during an empty reload.</summary>
        private float ChargePull(float t)
        {
            if (!Racking || t < insertEnd || t >= chargeReleaseEnd) return 0f;
            return t < chargeBackEnd
                ? Smooth(Mathf.InverseLerp(insertEnd, chargeBackEnd, t))          // pull back
                : 1f - Smooth(Mathf.InverseLerp(chargeBackEnd, chargeReleaseEnd, t)); // snap forward
        }

        private void AnimateChargingHandle()
        {
            if (!Racking) return;
            _rig.PlaceCharge(ChargePull(_t));
        }

        private void AnimateMagazines()
        {
            _rig.TryGetHome(out Vector3 home, out Quaternion homeR);
            _rig.GetEjected(out Vector3 ej, out Quaternion ejR);
            _rig.GetFetch(out Vector3 fe, out Quaternion feR);

            Transform inWell = _rig.InWellMagazine;
            Transform spare = _rig.SpareMagazine;

            // --- Old magazine (seated -> pulled out -> dropped/hidden) ---
            if (inWell != null)
            {
                if (_t < ejectStart)
                {
                    _rig.PlaceMag(inWell, home, homeR);
                    SetActive(inWell, true);
                }
                else if (_t < ejectEnd)
                {
                    float u = Smooth(Mathf.InverseLerp(ejectStart, ejectEnd, _t));
                    _rig.PlaceMag(inWell, Vector3.LerpUnclamped(home, ej, u),
                        Quaternion.SlerpUnclamped(homeR, ejR, u));
                    SetActive(inWell, true);
                }
                else if (_t < insertEnd)
                {
                    SetActive(inWell, false); // dropped; new mag not seated yet
                }
                else
                {
                    _rig.PlaceMag(inWell, home, homeR);
                    SetActive(inWell, true); // new mag seated
                }
            }

            // --- Spare magazine (hidden -> appears low -> inserts) ---
            if (spare != null)
            {
                if (_t < spareAppear)
                {
                    SetActive(spare, false);
                }
                else if (_t < insertStart)
                {
                    _rig.PlaceMag(spare, fe, feR);
                    SetActive(spare, true);
                }
                else if (_t < insertEnd)
                {
                    float u = Smooth(Mathf.InverseLerp(insertStart, insertEnd, _t));
                    _rig.PlaceMag(spare, Vector3.LerpUnclamped(fe, home, u),
                        Quaternion.SlerpUnclamped(feR, homeR, u));
                    SetActive(spare, true);
                }
                else
                {
                    SetActive(spare, false); // handed off to the in-well mag
                }
            }
        }

        private void AnimateHand()
        {
            if (handDriver == null) return;

            // Empty reload: after seating the mag, the support hand racks the charging handle.
            if (Racking && _t >= insertEnd && _t < chargeReleaseEnd)
            {
                _rig.GetChargeHandGripWorld(ChargePull(_t), out Vector3 cp, out Quaternion cr);
                handDriver.SetSupportOverride(cp, cr);
                return;
            }

            float handEnd = Racking ? chargeReleaseEnd : insertEnd;
            if (_t >= handEnd)
            {
                handDriver.ClearSupportOverride(); // hand returns to the foregrip
                return;
            }

            CarryPoint(_t, out Vector3 localPos, out Quaternion localRot);
            _rig.GetHandGripWorld(localPos, localRot, out Vector3 worldPos, out Quaternion worldRot);
            handDriver.SetSupportOverride(worldPos, worldRot);
        }

        /// <summary>The point the support hand carries, in MagSpace-local, across the timeline.</summary>
        private void CarryPoint(float t, out Vector3 pos, out Quaternion rot)
        {
            _rig.TryGetHome(out Vector3 home, out Quaternion homeR);
            _rig.GetEjected(out Vector3 ej, out Quaternion ejR);
            _rig.GetFetch(out Vector3 fe, out Quaternion feR);

            if (t < ejectStart)
            {
                pos = home; rot = homeR;
            }
            else if (t < ejectEnd)
            {
                float u = Smooth(Mathf.InverseLerp(ejectStart, ejectEnd, t));
                pos = Vector3.LerpUnclamped(home, ej, u);
                rot = Quaternion.SlerpUnclamped(homeR, ejR, u);
            }
            else if (t < spareAppear)
            {
                float u = Smooth(Mathf.InverseLerp(ejectEnd, spareAppear, t));
                pos = Vector3.LerpUnclamped(ej, fe, u);
                rot = Quaternion.SlerpUnclamped(ejR, feR, u);
            }
            else if (t < insertStart)
            {
                pos = fe; rot = feR;
            }
            else
            {
                float u = Smooth(Mathf.InverseLerp(insertStart, insertEnd, t));
                pos = Vector3.LerpUnclamped(fe, home, u);
                rot = Quaternion.SlerpUnclamped(feR, homeR, u);
            }
        }

        private static void SetActive(Transform t, bool active)
        {
            if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
