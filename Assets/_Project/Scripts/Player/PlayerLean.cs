using UnityEngine;
using UnityEngine.InputSystem;
using WeaponControl.CameraSystem;

namespace WeaponControl.Player
{
    /// <summary>
    /// Hold Q / E (or gamepad bumpers) to peek around cover.
    /// Blocks lean into walls and reports the current lean to <see cref="CameraController"/>.
    /// Attach to the Player root.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerLean : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private CameraController cameraController;
        [SerializeField] private Transform cameraTransform;

        [Header("Lean")]
        [SerializeField] private float leanOffset = 0.28f;
        [SerializeField] private float leanRoll = 12f;
        [SerializeField] private float leanSpeed = 10f;
        [SerializeField] private float obstructionCheckRadius = 0.18f;
        [SerializeField] private LayerMask obstructionMask = ~0;

        public float Lean { get; private set; }

        private float _current;

        private void Awake()
        {
            if (cameraController == null) cameraController = GetComponentInChildren<CameraController>();
            if (cameraTransform == null && cameraController != null) cameraTransform = cameraController.transform;
            obstructionMask &= ~(1 << gameObject.layer);
        }

        private void Update()
        {
            float target = ReadLeanInput();
            if (target != 0f && IsBlocked(target))
                target = 0f;

            _current = Mathf.Lerp(_current, target, 1f - Mathf.Exp(-leanSpeed * Time.deltaTime));
            if (Mathf.Abs(_current) < 0.001f) _current = 0f;

            Lean = _current;
            cameraController?.SetLean(_current, _current * leanOffset, -_current * leanRoll);
        }

        private static float ReadLeanInput()
        {
            float lean = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.qKey.isPressed) lean -= 1f;
                if (kb.eKey.isPressed) lean += 1f;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.leftShoulder.isPressed) lean -= 1f;
                if (pad.rightShoulder.isPressed) lean += 1f;
            }

            return Mathf.Clamp(lean, -1f, 1f);
        }

        private bool IsBlocked(float direction)
        {
            if (cameraTransform == null) return false;
            Vector3 origin = cameraTransform.position;
            Vector3 dir = cameraTransform.right * Mathf.Sign(direction);
            float scale = transform.lossyScale.x;
            float dist = Mathf.Abs(leanOffset) * scale;
            float radius = obstructionCheckRadius * scale;
            int mask = obstructionMask & ~(1 << gameObject.layer);
            return Physics.SphereCast(origin, radius, dir, out _, dist, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
