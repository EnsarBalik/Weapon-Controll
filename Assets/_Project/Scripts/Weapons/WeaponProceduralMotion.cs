using System;
using UnityEngine;
using WeaponControl.CameraSystem;
using WeaponControl.Core;
using WeaponControl.Player;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Procedural view-model motion for the weapon. Attach to the WeaponHolder (the empty
    /// parent of the weapon, child of the camera). Combines several layers each frame:
    ///
    ///   * Sway     - weapon lags behind camera look, eases back when the mouse stops.
    ///   * Bob      - sinusoidal movement driven by walk/sprint speed.
    ///   * ADS      - slides the weapon so its AimPoint lines up with screen center,
    ///                narrows FOV and damps sway/bob for a steady aim.
    ///   * Sprint   - lowers / tilts the weapon into a "running" pose.
    ///
    /// All offsets are authored in the holder's local (camera) space, which keeps the
    /// *visual* amount consistent regardless of the player's overall transform scale.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponProceduralMotion : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private CameraController cameraController;
        [SerializeField] private PlayerMovement movement;
        [SerializeField] private WeaponReferences weapon;

        [Header("Sway (look)")]
        [SerializeField] private float swayPositionStrength = 0.02f;
        [SerializeField] private float swayPositionClamp = 0.06f;
        [SerializeField] private float swayRotationStrength = 4f;
        [SerializeField] private float swayRotationClamp = 8f;
        [SerializeField] private float swaySmooth = 12f;

        [Header("Bob (movement)")]
        [SerializeField] private float bobSpeed = 9f;
        [SerializeField] private float bobHorizontalAmount = 0.015f;
        [SerializeField] private float bobVerticalAmount = 0.02f;
        [SerializeField] private float bobRotationAmount = 1.2f;
        [SerializeField] private float sprintBobMultiplier = 1.6f;
        [SerializeField] private float bobSmooth = 10f;

        [Header("Aim Down Sights")]
        [SerializeField] private float aimBlendSpeed = 14f;
        [SerializeField] private float aimFov = 45f;
        [Tooltip("How much sway/bob remains while fully aimed (0 = rock steady, 1 = full).")]
        [Range(0f, 1f)][SerializeField] private float aimMotionMultiplier = 0.15f;

        [Header("Sprint Pose")]
        [SerializeField] private Vector3 sprintPositionOffset = new Vector3(0f, -0.02f, -0.03f);
        [SerializeField] private Vector3 sprintRotationOffset = new Vector3(10f, -18f, 0f);
        [SerializeField] private float sprintBlendSpeed = 8f;

        [Header("Reload Pose (weapon raised toward the player to swap the mag)")]
        [SerializeField] private Vector3 reloadPositionOffset = new Vector3(0.02f, 0.05f, -0.06f);
        [SerializeField] private Vector3 reloadRotationOffset = new Vector3(-12f, 20f, 10f);
        [SerializeField] private float reloadBlendSpeed = 10f;

        [Header("Holster / Draw Pose")]
        [SerializeField] private Vector3 holsterPositionOffset = new Vector3(0f, -0.3f, 0.05f);
        [SerializeField] private Vector3 holsterRotationOffset = new Vector3(55f, 0f, 0f);
        [SerializeField] private float holsterBlendSpeed = 12f;

        [Header("Inspect")]
        [SerializeField] private float inspectDuration = 2.6f;
        [SerializeField] private Vector3 inspectPositionOffset = new Vector3(-0.04f, 0.02f, 0.06f);
        [SerializeField] private float inspectBlendSpeed = 9f;

        [Header("Idle Breath")]
        [SerializeField] private float breathAmount = 0.004f;
        [SerializeField] private float breathSpeed = 1.15f;

        [Header("Weapon Collision")]
        [SerializeField] private bool enableCollision = true;
        [SerializeField] private float collisionCheckDistance = 0.55f;
        [SerializeField] private float collisionRadius = 0.08f;
        [SerializeField] private float collisionPull = 0.14f;
        [SerializeField] private float collisionPitch = 28f;
        [SerializeField] private float collisionSmooth = 12f;
        [SerializeField] private LayerMask collisionMask = ~0;

        // Home pose
        private Vector3 _basePos;
        private Quaternion _baseRot;

        // Smoothed state
        private Vector3 _swayPos;
        private Vector3 _swayRot;
        private float _bobTimer;
        private float _bobAmp;
        private float _aimBlend;
        private float _sprintBlend;
        private bool _reloadActive;
        private float _reloadBlend;
        private bool _reloadWeightDriven;
        private float _reloadWeightExternal;
        private bool _holsterActive;
        private float _holsterBlend;
        private bool _inspecting;
        private float _inspectTimer;
        private float _inspectBlend;
        private float _collision;

        /// <summary>0..1 aim-down-sights blend. Used by recoil / crosshair / sensitivity later.</summary>
        public float AimBlend => _aimBlend;
        public bool IsAiming => _aimBlend > 0.5f;
        public bool IsInspecting => _inspecting;
        public float CollisionAmount => _collision;

        public event Action InspectStarted;

        /// <summary>Called by the ammo system to raise/lower the weapon into a reload pose.</summary>
        public void SetReloadPose(bool active) => _reloadActive = active;

        /// <summary>
        /// Lets the reload animator drive the reload-pose blend directly from a timed curve instead
        /// of the default ease. Call every frame during reload; call <see cref="ClearReloadWeightOverride"/>
        /// when finished so the automatic ease takes over again.
        /// </summary>
        public void SetReloadWeight(float weight)
        {
            _reloadWeightDriven = true;
            _reloadWeightExternal = Mathf.Clamp01(weight);
        }

        /// <summary>Hand reload-pose blending back to the automatic ease.</summary>
        public void ClearReloadWeightOverride() => _reloadWeightDriven = false;

        /// <summary>Called by the inventory to lower (holster) / raise (draw) the weapon.</summary>
        public void SetHolster(bool active) => _holsterActive = active;

        /// <summary>Swap which weapon's AimPoint drives ADS alignment (on weapon switch).</summary>
        public void SetWeapon(WeaponReferences newWeapon) => weapon = newWeapon;

        public void CancelInspect()
        {
            _inspecting = false;
        }

        private void Awake()
        {
            if (cameraTransform == null) cameraTransform = transform.parent;
            if (cameraController == null) cameraController = GetComponentInParent<CameraController>();
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();
            if (weapon == null) weapon = GetComponentInChildren<WeaponReferences>();

            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            ExcludeLayer(ref collisionMask, "Player");
            ExcludeLayer(ref collisionMask, "Weapon");
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            HandleInspectInput();
            UpdateBlends(dt);
            UpdateCollision(dt);
            Vector3 swayPos = UpdateSway(dt, out Vector3 swayRot);
            Vector3 bobPos = UpdateBob(dt, out Vector3 bobRot);
            Vector3 adsOffset = ComputeAdsOffset();
            EvaluateInspect(out Vector3 inspectPos, out Vector3 inspectRot);
            Vector3 breath = Vector3.up * (Mathf.Sin(Time.time * breathSpeed) * breathAmount * (1f - _aimBlend) * (1f - _inspectBlend));

            float motionMul = Mathf.Lerp(1f, aimMotionMultiplier, _aimBlend);
            Vector3 collisionPos = new Vector3(0f, 0f, -collisionPull * _collision);
            Vector3 collisionRot = new Vector3(collisionPitch * _collision, 0f, 0f);

            Vector3 finalPos = _basePos
                               + (swayPos + bobPos) * motionMul
                               + sprintPositionOffset * _sprintBlend
                               + reloadPositionOffset * _reloadBlend
                               + holsterPositionOffset * _holsterBlend
                               + inspectPos * _inspectBlend
                               + adsOffset * _aimBlend
                               + collisionPos
                               + breath;

            Quaternion finalRot = _baseRot
                                  * Quaternion.Euler((swayRot + bobRot) * motionMul)
                                  * Quaternion.Euler(sprintRotationOffset * _sprintBlend)
                                  * Quaternion.Euler(reloadRotationOffset * _reloadBlend)
                                  * Quaternion.Euler(holsterRotationOffset * _holsterBlend)
                                  * Quaternion.Euler(inspectRot * _inspectBlend)
                                  * Quaternion.Euler(collisionRot);

            transform.localPosition = finalPos;
            transform.localRotation = finalRot;

            if (cameraController != null)
                cameraController.SetTargetFov(Mathf.Lerp(cameraController.BaseFov, aimFov, _aimBlend));
        }

        private void UpdateBlends(float dt)
        {
            // Can't aim down sights while reloading, holstered or inspecting.
            float aimTarget = (GameInput.Player.Aim.IsPressed() && !_reloadActive && !_holsterActive && !_inspecting) ? 1f : 0f;
            _aimBlend = Mathf.Lerp(_aimBlend, aimTarget, 1f - Mathf.Exp(-aimBlendSpeed * dt));

            bool sprinting = movement != null && movement.IsSprinting && _aimBlend < 0.5f && !_reloadActive && !_holsterActive && !_inspecting;
            _sprintBlend = Mathf.Lerp(_sprintBlend, sprinting ? 1f : 0f, 1f - Mathf.Exp(-sprintBlendSpeed * dt));

            if (_reloadWeightDriven)
                _reloadBlend = _reloadWeightExternal;
            else
                _reloadBlend = Mathf.Lerp(_reloadBlend, _reloadActive ? 1f : 0f, 1f - Mathf.Exp(-reloadBlendSpeed * dt));
            _holsterBlend = Mathf.Lerp(_holsterBlend, _holsterActive ? 1f : 0f, 1f - Mathf.Exp(-holsterBlendSpeed * dt));
            _inspectBlend = Mathf.Lerp(_inspectBlend, _inspecting ? 1f : 0f, 1f - Mathf.Exp(-inspectBlendSpeed * dt));
        }

        private void HandleInspectInput()
        {
            if (GameInput.Player.Inspect.WasPressedThisFrame())
            {
                if (_inspecting) CancelInspect();
                else TryStartInspect();
            }

            if (!_inspecting) return;

            _inspectTimer -= Time.deltaTime;
            bool interrupted = _reloadActive || _holsterActive
                               || (movement != null && movement.IsSprinting)
                               || GameInput.Player.Aim.IsPressed()
                               || GameInput.Player.Attack.IsPressed();
            if (_inspectTimer <= 0f || interrupted)
                CancelInspect();
        }

        private void TryStartInspect()
        {
            if (_reloadActive || _holsterActive || _aimBlend > 0.2f) return;
            if (movement != null && movement.IsSprinting) return;
            _inspecting = true;
            _inspectTimer = inspectDuration;
            InspectStarted?.Invoke();
        }

        private void EvaluateInspect(out Vector3 pos, out Vector3 rot)
        {
            float t = inspectDuration > 0f ? 1f - Mathf.Clamp01(_inspectTimer / inspectDuration) : 1f;
            // Lift to center, yaw to show the left side, then roll the mag well into view.
            float yaw = Mathf.Sin(t * Mathf.PI) * -55f;
            float pitch = Mathf.Sin(t * Mathf.PI * 2f) * 12f;
            float roll = Mathf.Sin(t * Mathf.PI) * 18f;
            pos = inspectPositionOffset * Mathf.Sin(t * Mathf.PI);
            rot = new Vector3(pitch, yaw, roll);
        }

        private void UpdateCollision(float dt)
        {
            float target = 0f;
            if (enableCollision && cameraTransform != null && _aimBlend < 0.85f && !_inspecting)
            {
                // Scale the *world-space* probe by the PLAYER root scale, not the camera's.
                // The camera is intentionally down-scaled (e.g. 0.1) so the view-model stays a
                // normal size on a giant (10x) player, which makes cameraTransform.lossyScale ~1.
                // The distance we need to check, however, must grow with the player so the probe
                // still reaches the wall the (scaled) body is pressed against.
                float scale = movement != null ? movement.transform.lossyScale.z : cameraTransform.lossyScale.z;
                if (scale <= 0.0001f) scale = 1f;

                float dist = collisionCheckDistance * scale;
                float radius = collisionRadius * scale;
                Vector3 origin = cameraTransform.position;
                if (Physics.SphereCast(origin, radius, cameraTransform.forward, out RaycastHit hit,
                        dist, collisionMask, QueryTriggerInteraction.Ignore))
                {
                    target = 1f - Mathf.Clamp01(hit.distance / dist);
                }
                else if (Physics.CheckSphere(origin, radius, collisionMask, QueryTriggerInteraction.Ignore))
                {
                    // Sphere already overlaps geometry at the camera (pressed right into a wall):
                    // SphereCast returns nothing in that case, so force a full retract.
                    target = 1f;
                }
            }

            _collision = Mathf.Lerp(_collision, target, 1f - Mathf.Exp(-collisionSmooth * dt));
        }

        private Vector3 UpdateSway(float dt, out Vector3 swayRot)
        {
            Vector2 look = GameInput.Player.Look.ReadValue<Vector2>();

            Vector3 posTarget = new Vector3(
                Mathf.Clamp(-look.x * swayPositionStrength, -swayPositionClamp, swayPositionClamp),
                Mathf.Clamp(-look.y * swayPositionStrength, -swayPositionClamp, swayPositionClamp),
                0f);

            Vector3 rotTarget = new Vector3(
                Mathf.Clamp(look.y * swayRotationStrength, -swayRotationClamp, swayRotationClamp),   // pitch
                Mathf.Clamp(look.x * swayRotationStrength, -swayRotationClamp, swayRotationClamp),   // yaw
                Mathf.Clamp(-look.x * swayRotationStrength, -swayRotationClamp, swayRotationClamp));  // roll

            float t = 1f - Mathf.Exp(-swaySmooth * dt);
            _swayPos = Vector3.Lerp(_swayPos, posTarget, t);
            _swayRot = Vector3.Lerp(_swayRot, rotTarget, t);

            swayRot = _swayRot;
            return _swayPos;
        }

        private Vector3 UpdateBob(float dt, out Vector3 bobRot)
        {
            float speedFactor = movement != null ? movement.NormalizedSpeed : 0f;
            bool grounded = movement == null || movement.IsGrounded;
            bool moving = grounded && speedFactor > 0.1f;
            bool sprinting = movement != null && movement.IsSprinting;

            float freq = bobSpeed * (sprinting ? sprintBobMultiplier : 1f);
            if (moving) _bobTimer += dt * freq;

            _bobAmp = Mathf.Lerp(_bobAmp, moving ? 1f : 0f, 1f - Mathf.Exp(-bobSmooth * dt));
            float intensity = _bobAmp * Mathf.Lerp(0.5f, 1f, speedFactor) * (sprinting ? sprintBobMultiplier : 1f);

            Vector3 bobPos = new Vector3(
                Mathf.Cos(_bobTimer) * bobHorizontalAmount,
                Mathf.Sin(_bobTimer * 2f) * bobVerticalAmount,
                0f) * intensity;

            bobRot = new Vector3(
                Mathf.Sin(_bobTimer * 2f) * bobRotationAmount * 0.5f,
                Mathf.Cos(_bobTimer) * bobRotationAmount,
                Mathf.Sin(_bobTimer) * bobRotationAmount) * intensity;

            return bobPos;
        }

        private Vector3 ComputeAdsOffset()
        {
            if (weapon == null || weapon.AimPoint == null || cameraTransform == null)
                return Vector3.zero;

            // AimPoint position expressed in the camera's local space.
            Vector3 aimCamLocal = cameraTransform.InverseTransformPoint(weapon.AimPoint.position);
            // Offset of the aim point from the holder (invariant to holder translation,
            // so this is stable even though we move the holder by this value).
            Vector3 aimFromHolder = aimCamLocal - transform.localPosition;
            // Move the holder so the aim point ends up centered (x = y = 0), keep depth.
            return new Vector3(-aimFromHolder.x, -aimFromHolder.y, 0f);
        }

        private static void ExcludeLayer(ref LayerMask mask, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask &= ~(1 << layer);
        }
    }
}
