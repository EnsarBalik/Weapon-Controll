using UnityEngine;
using WeaponControl.CameraSystem;
using WeaponControl.Core;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Handles recoil in two coordinated parts:
    ///   * Camera recoil - a view kick sent to <see cref="CameraController.AddRecoil"/> that
    ///     shifts the point of aim and then recovers.
    ///   * Weapon kick   - a springy positional/rotational punch applied to this transform
    ///     (the RecoilPivot), layered on top of sway/bob which live on the parent holder.
    ///
    /// Attach to a "RecoilPivot" empty placed between the WeaponHolder and the weapon:
    ///   WeaponHolder (WeaponProceduralMotion)
    ///     └─ RecoilPivot (this)
    ///         └─ AKM
    ///
    /// Call <see cref="ApplyShot"/> for every fired round. Phase 6 will drive this from the
    /// actual fire logic; until then enable <see cref="enableTestFire"/> to feel it with LMB.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponRecoil : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private CameraController cameraController;
        [SerializeField] private WeaponProceduralMotion motion;

        [Header("Camera Recoil (degrees per shot)")]
        [SerializeField] private float verticalKick = 1.2f;
        [SerializeField] private float horizontalKick = 0.4f;

        [Header("Weapon Kick")]
        [Tooltip("How far the weapon punches backward (local units).")]
        [SerializeField] private float kickBack = 0.03f;
        [Tooltip("Slight upward shift of the weapon on each shot (local units).")]
        [SerializeField] private float kickUp = 0.008f;
        [Tooltip("Pitch punch (weapon muzzle rises), degrees.")]
        [SerializeField] private float kickPitch = 3f;
        [SerializeField] private float kickYawRandom = 1.5f;
        [SerializeField] private float kickRollRandom = 1.8f;

        [Header("Spring")]
        [Tooltip("How fast the kick reaches its peak (higher = snappier).")]
        [SerializeField] private float kickSnappiness = 20f;
        [Tooltip("How fast the weapon settles back to rest.")]
        [SerializeField] private float kickRecovery = 9f;

        [Header("Aim")]
        [Tooltip("Recoil multiplier while fully aimed down sights (0..1).")]
        [Range(0f, 1f)][SerializeField] private float aimRecoilMultiplier = 0.6f;

        [Header("Test Fire (temporary - remove in Phase 6)")]
        [SerializeField] private bool enableTestFire = true;
        [Tooltip("Rounds per minute for the test fire.")]
        [SerializeField] private float testFireRate = 600f;

        private Vector3 _basePos;
        private Quaternion _baseRot;

        private Vector3 _kickPos;
        private Vector3 _kickPosTarget;
        private Vector3 _kickRot;
        private Vector3 _kickRotTarget;

        private float _nextTestShotTime;

        private void Awake()
        {
            if (cameraController == null) cameraController = GetComponentInParent<CameraController>();
            if (motion == null) motion = GetComponentInParent<WeaponProceduralMotion>();

            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
        }

        private void Update()
        {
            if (enableTestFire && GameInput.Player.Attack.IsPressed() && Time.time >= _nextTestShotTime)
            {
                _nextTestShotTime = Time.time + 60f / Mathf.Max(1f, testFireRate);
                ApplyShot();
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Impulse builds toward target, target decays back to zero (recovery).
            _kickPosTarget = Vector3.Lerp(_kickPosTarget, Vector3.zero, 1f - Mathf.Exp(-kickRecovery * dt));
            _kickRotTarget = Vector3.Lerp(_kickRotTarget, Vector3.zero, 1f - Mathf.Exp(-kickRecovery * dt));
            _kickPos = Vector3.Lerp(_kickPos, _kickPosTarget, 1f - Mathf.Exp(-kickSnappiness * dt));
            _kickRot = Vector3.Lerp(_kickRot, _kickRotTarget, 1f - Mathf.Exp(-kickSnappiness * dt));

            transform.localPosition = _basePos + _kickPos;
            transform.localRotation = _baseRot * Quaternion.Euler(_kickRot);
        }

        /// <summary>Apply one shot's worth of recoil (camera view kick + weapon kick).</summary>
        public void ApplyShot()
        {
            float aimMul = motion != null
                ? Mathf.Lerp(1f, aimRecoilMultiplier, motion.AimBlend)
                : 1f;

            if (cameraController != null)
            {
                cameraController.AddRecoil(
                    verticalKick * aimMul,
                    Random.Range(-horizontalKick, horizontalKick) * aimMul);
            }

            _kickPosTarget += new Vector3(0f, kickUp, -kickBack) * aimMul;
            _kickRotTarget += new Vector3(
                -kickPitch,
                Random.Range(-kickYawRandom, kickYawRandom),
                Random.Range(-kickRollRandom, kickRollRandom)) * aimMul;
        }
    }
}
