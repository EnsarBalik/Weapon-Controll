using UnityEngine;
using UnityEngine.InputSystem;
using WeaponControl.Core;

namespace WeaponControl.Player
{
    public enum MovementState
    {
        Idle,
        Walking,
        Sprinting,
        Crouching,
        Airborne
    }

    /// <summary>
    /// First-person player locomotion built on a CharacterController.
    /// Handles walk / sprint / crouch / jump with acceleration smoothing and gravity.
    /// Movement is relative to the body's yaw (set by <c>CameraController</c>).
    ///
    /// Exposes read-only state (<see cref="State"/>, <see cref="CurrentSpeed"/>, etc.) so
    /// later phases (head-bob, weapon sway) can react to how the player is moving.
    ///
    /// Attach to the Player root object.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float sprintSpeed = 7.5f;
        [SerializeField] private float crouchSpeed = 2.2f;

        [Header("Acceleration")]
        [Tooltip("How fast horizontal velocity ramps toward the target speed on the ground.")]
        [SerializeField] private float groundAcceleration = 60f;
        [Tooltip("How fast horizontal velocity changes while airborne (lower = floatier).")]
        [SerializeField] private float airAcceleration = 12f;

        [Header("Jump & Gravity")]
        [SerializeField] private float jumpHeight = 1.1f;
        [SerializeField] private float gravity = -20f;
        [Tooltip("Small downward force applied while grounded to keep the controller stuck to the floor.")]
        [SerializeField] private float groundedStickForce = -3f;

        [Header("Crouch")]
        [SerializeField] private float standingHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.1f;
        [Tooltip("How fast the capsule/camera transition between standing and crouching.")]
        [SerializeField] private float crouchLerpSpeed = 10f;
        [Tooltip("Layers checked when deciding if there is room to stand up.")]
        [SerializeField] private LayerMask standObstructionMask = ~0;

        [Header("Camera / Head")]
        [Tooltip("The camera (or head) transform whose local height is lowered when crouching.")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float standingEyeHeight = 1.7f;
        [SerializeField] private float crouchEyeHeight = 1.05f;

        [Header("Options")]
        [Tooltip("If true, sprint only engages when moving mostly forward.")]
        [SerializeField] private bool sprintForwardOnly = true;

        private CharacterController _controller;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private bool _isCrouching;
        private float _standingCenterY;
        private float _crouchCenterY;

        // ---- Public read-only state (used by bob / sway / UI later) ----
        public MovementState State { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsCrouching => _isCrouching;
        public bool IsSprinting => State == MovementState.Sprinting;
        /// <summary>Horizontal speed in m/s.</summary>
        public float CurrentSpeed => new Vector2(_horizontalVelocity.x, _horizontalVelocity.z).magnitude;
        /// <summary>Full controller velocity including vertical component.</summary>
        public Vector3 Velocity => _horizontalVelocity + Vector3.up * _verticalVelocity;
        /// <summary>Local move input (x = strafe, y = forward), raw from the input system.</summary>
        public Vector2 MoveInput { get; private set; }
        /// <summary>0..1 how fast we're moving relative to sprint speed. Handy for bob amplitude.</summary>
        public float NormalizedSpeed => Mathf.Clamp01(CurrentSpeed / sprintSpeed);

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _controller.height = standingHeight;
            _standingCenterY = standingHeight * 0.5f;
            _crouchCenterY = crouchHeight * 0.5f;
            _controller.center = new Vector3(0f, _standingCenterY, 0f);

            if (cameraTransform != null)
            {
                Vector3 p = cameraTransform.localPosition;
                p.y = standingEyeHeight;
                cameraTransform.localPosition = p;
            }
        }

        private void Update()
        {
            ReadInput();
            HandleCrouch();
            HandleMovement();
            UpdateState();
        }

        private void ReadInput()
        {
            MoveInput = GameInput.Player.Move.ReadValue<Vector2>();
        }

        private void HandleMovement()
        {
            IsGrounded = _controller.isGrounded;

            if (IsGrounded && _verticalVelocity < 0f)
                _verticalVelocity = groundedStickForce;

            // Desired horizontal direction relative to the body's facing.
            Vector3 desiredDir = transform.right * MoveInput.x + transform.forward * MoveInput.y;
            if (desiredDir.sqrMagnitude > 1f) desiredDir.Normalize();

            float targetSpeed = ResolveTargetSpeed();
            Vector3 targetVelocity = desiredDir * targetSpeed;

            float accel = IsGrounded ? groundAcceleration : airAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, accel * Time.deltaTime);

            // Jump
            if (IsGrounded && GameInput.Player.Jump.WasPressedThisFrame() && !_isCrouching)
                _verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);

            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);
        }

        private float ResolveTargetSpeed()
        {
            if (_isCrouching) return crouchSpeed;

            bool wantsSprint = GameInput.Player.Sprint.IsPressed() && MoveInput.sqrMagnitude > 0.01f;
            if (sprintForwardOnly)
                wantsSprint &= MoveInput.y > 0.3f;

            return wantsSprint ? sprintSpeed : walkSpeed;
        }

        private void HandleCrouch()
        {
            bool wantsCrouch = GameInput.Player.Crouch.IsPressed();

            // Trying to stand: make sure there's headroom.
            if (_isCrouching && !wantsCrouch && !HasHeadroomToStand())
                wantsCrouch = true;

            _isCrouching = wantsCrouch;

            float targetHeight = _isCrouching ? crouchHeight : standingHeight;
            float targetCenter = _isCrouching ? _crouchCenterY : _standingCenterY;
            float t = 1f - Mathf.Exp(-crouchLerpSpeed * Time.deltaTime);

            _controller.height = Mathf.Lerp(_controller.height, targetHeight, t);
            Vector3 center = _controller.center;
            center.y = Mathf.Lerp(center.y, targetCenter, t);
            _controller.center = center;

            if (cameraTransform != null)
            {
                Vector3 p = cameraTransform.localPosition;
                float targetEye = _isCrouching ? crouchEyeHeight : standingEyeHeight;
                p.y = Mathf.Lerp(p.y, targetEye, t);
                cameraTransform.localPosition = p;
            }
        }

        private bool HasHeadroomToStand()
        {
            // Cast a capsule spanning the standing height from the controller's base upward.
            float radius = _controller.radius * 0.95f;
            Vector3 basePoint = transform.position + Vector3.up * radius;
            Vector3 topPoint = transform.position + Vector3.up * (standingHeight - radius);
            // Exclude the player's own layer so the controller capsule doesn't detect itself.
            int mask = standObstructionMask & ~(1 << gameObject.layer);
            return !Physics.CheckCapsule(basePoint, topPoint, radius, mask, QueryTriggerInteraction.Ignore);
        }

        private void UpdateState()
        {
            if (!IsGrounded)
                State = MovementState.Airborne;
            else if (_isCrouching)
                State = MovementState.Crouching;
            else if (CurrentSpeed < 0.15f)
                State = MovementState.Idle;
            else if (IsSprintingSpeed())
                State = MovementState.Sprinting;
            else
                State = MovementState.Walking;
        }

        private bool IsSprintingSpeed()
        {
            // Consider "sprinting" when close to sprint speed and actually moving forward.
            return CurrentSpeed > (walkSpeed + sprintSpeed) * 0.5f;
        }
    }
}
