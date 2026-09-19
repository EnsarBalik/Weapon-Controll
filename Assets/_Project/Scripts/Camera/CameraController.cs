using UnityEngine;
using UnityEngine.InputSystem;
using WeaponControl.Core;

namespace WeaponControl.CameraSystem
{
    /// <summary>
    /// First-person look controller.
    /// Yaw (left/right) is applied to <see cref="yawTarget"/> (the player body) so that
    /// movement in Phase 2 can use the body's facing direction. Pitch (up/down) is applied
    /// to this camera transform and clamped.
    ///
    /// Attach this to the Camera object and assign the Player root as the Yaw Target.
    /// If no Yaw Target is assigned, both yaw and pitch are applied to this transform
    /// (useful for quick standalone testing).
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Player body transform that rotates around Y (yaw). Leave empty to rotate this transform for both axes.")]
        [SerializeField] private Transform yawTarget;

        [Header("Look Sensitivity")]
        [Tooltip("Degrees of rotation per unit of look input (mouse delta is already frame-based).")]
        [SerializeField] private float sensitivity = 0.1f;
        [Tooltip("Extra multiplier applied only to gamepad stick input (scaled by delta time).")]
        [SerializeField] private float gamepadSensitivity = 180f;
        [SerializeField] private bool invertY = false;

        [Header("Pitch Clamp")]
        [SerializeField] private float pitchMin = -89f;
        [SerializeField] private float pitchMax = 89f;

        [Header("Smoothing")]
        [Tooltip("If enabled, look rotation eases toward the target for a smoother (slightly laggier) feel.")]
        [SerializeField] private bool smoothing = false;
        [SerializeField] private float smoothSpeed = 30f;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnStart = true;

        [Header("FOV (infrastructure for ADS in later phases)")]
        [Tooltip("How quickly the camera FOV eases toward the target FOV.")]
        [SerializeField] private float fovLerpSpeed = 12f;

        [Header("Recoil")]
        [Tooltip("How fast the view snaps toward the accumulated recoil (higher = punchier).")]
        [SerializeField] private float recoilSnappiness = 18f;
        [Tooltip("How fast recoil recovers back to the original aim (higher = faster settle).")]
        [SerializeField] private float recoilRecovery = 10f;

        [Header("Shake")]
        [SerializeField] private float shakeDecay = 14f;

        private float _yaw;
        private float _pitch;
        private float _targetYaw;
        private float _targetPitch;

        private Camera _camera;
        private float _baseFov;
        private float _targetFov;

        // Recoil (additive view rotation that fully recovers). x = vertical (up+), y = horizontal (yaw).
        private Vector2 _recoilCurrent;
        private Vector2 _recoilTarget;

        // Lean: -1 left, +1 right. Applied as local X offset + Z roll.
        private float _lean;
        private float _leanRoll;
        private float _leanOffset;

        // Camera shake (recovers automatically).
        private Vector3 _shakeRot;
        private Vector3 _shakePos;

        /// <summary>The camera component on this object.</summary>
        public Camera Cam => _camera;

        /// <summary>Default (hip-fire) field of view captured at startup.</summary>
        public float BaseFov => _baseFov;

        /// <summary>Current pitch in degrees (up/down look angle).</summary>
        public float Pitch => _pitch;

        /// <summary>Current yaw in degrees.</summary>
        public float Yaw => _yaw;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null)
            {
                _baseFov = _camera.fieldOfView;
                _targetFov = _baseFov;
            }
        }

        private void Start()
        {
            // Seed rotation from current transform so we don't snap on the first frame.
            _yaw = _targetYaw = yawTarget != null ? yawTarget.eulerAngles.y : transform.eulerAngles.y;
            _pitch = _targetPitch = NormalizeAngle(transform.localEulerAngles.x);

            if (lockCursorOnStart)
                SetCursorLocked(true);
        }

        private void Update()
        {
            HandleCursorToggle();
            HandleLook();
            HandleFov();
        }

        private void HandleLook()
        {
            UpdateRecoil(Time.deltaTime);

            Vector2 look = GameInput.Player.Look.ReadValue<Vector2>();

            // Mouse delta is per-frame; gamepad stick is a persistent value that must be
            // scaled by delta time. Detect the source via the active control device.
            bool isGamepad = Gamepad.current != null &&
                             GameInput.Player.Look.activeControl != null &&
                             GameInput.Player.Look.activeControl.device is Gamepad;

            Vector2 delta = isGamepad
                ? look * gamepadSensitivity * Time.deltaTime
                : look * sensitivity;

            _targetYaw += delta.x;
            _targetPitch += invertY ? delta.y : -delta.y;
            _targetPitch = Mathf.Clamp(_targetPitch, pitchMin, pitchMax);

            if (smoothing)
            {
                float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
                _yaw = Mathf.LerpAngle(_yaw, _targetYaw, t);
                _pitch = Mathf.Lerp(_pitch, _targetPitch, t);
            }
            else
            {
                _yaw = _targetYaw;
                _pitch = _targetPitch;
            }

            // Recoil is applied additively on the camera transform so it always recovers
            // back to the player's real aim. Vertical kick pushes the view up (negative pitch).
            float pitch = _pitch - _recoilCurrent.x;

            UpdateShake(Time.deltaTime);

            float roll = _leanRoll + _shakeRot.z;
            if (yawTarget != null)
            {
                yawTarget.rotation = Quaternion.Euler(0f, _yaw, 0f);
                transform.localRotation = Quaternion.Euler(pitch + _shakeRot.x, _recoilCurrent.y + _shakeRot.y, roll);
            }
            else
            {
                transform.localRotation = Quaternion.Euler(pitch + _shakeRot.x, _yaw + _recoilCurrent.y + _shakeRot.y, roll);
            }

            Vector3 lp = transform.localPosition;
            lp.x = _leanOffset + _shakePos.x;
            lp.z = _shakePos.z;
            transform.localPosition = lp;
        }

        private void UpdateRecoil(float dt)
        {
            _recoilTarget = Vector2.Lerp(_recoilTarget, Vector2.zero, 1f - Mathf.Exp(-recoilRecovery * dt));
            _recoilCurrent = Vector2.Lerp(_recoilCurrent, _recoilTarget, 1f - Mathf.Exp(-recoilSnappiness * dt));
        }

        /// <summary>
        /// Add a recoil impulse to the view. <paramref name="vertical"/> kicks the aim up (degrees),
        /// <paramref name="horizontal"/> kicks it sideways (degrees). Recovers automatically.
        /// </summary>
        public void AddRecoil(float vertical, float horizontal)
        {
            _recoilTarget += new Vector2(vertical, horizontal);
        }

        /// <summary>Set lean pose. <paramref name="lean"/> is -1 (left) to 1 (right).</summary>
        public void SetLean(float lean, float offset, float roll)
        {
            _lean = lean;
            _leanOffset = offset;
            _leanRoll = roll;
        }

        /// <summary>Current lean amount (-1..1).</summary>
        public float Lean => _lean;

        /// <summary>Add a short camera shake impulse (degrees / local units).</summary>
        public void AddShake(float intensity)
        {
            intensity = Mathf.Max(0f, intensity);
            _shakeRot += new Vector3(
                Random.Range(-0.4f, 0.15f) * intensity,
                Random.Range(-0.35f, 0.35f) * intensity,
                Random.Range(-0.6f, 0.6f) * intensity);
            _shakePos += new Vector3(
                Random.Range(-0.004f, 0.004f) * intensity,
                Random.Range(-0.003f, 0.003f) * intensity,
                Random.Range(-0.006f, 0f) * intensity);
        }

        private void UpdateShake(float dt)
        {
            float t = 1f - Mathf.Exp(-shakeDecay * dt);
            _shakeRot = Vector3.Lerp(_shakeRot, Vector3.zero, t);
            _shakePos = Vector3.Lerp(_shakePos, Vector3.zero, t);
        }

        private void HandleFov()
        {
            if (_camera == null) return;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, _targetFov,
                1f - Mathf.Exp(-fovLerpSpeed * Time.deltaTime));
        }

        private void HandleCursorToggle()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                     Cursor.lockState != CursorLockMode.Locked)
                SetCursorLocked(true);
        }

        /// <summary>Set the target FOV (used by ADS / sprint effects in later phases).</summary>
        public void SetTargetFov(float fov) => _targetFov = fov;

        /// <summary>Reset FOV back to the captured base value.</summary>
        public void ResetFov() => _targetFov = _baseFov;

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            return angle;
        }
    }
}
