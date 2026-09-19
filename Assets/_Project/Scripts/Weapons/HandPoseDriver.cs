using UnityEngine;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Drives the single shared pair of hands so their pose follows the active weapon.
    ///
    /// Attach to the "Hands" group (the parent of the two hand meshes, sitting under the RecoilPivot).
    /// On startup it captures each hand's local pose as the DEFAULT (rifle) pose. Each frame it looks
    /// up the active weapon's <see cref="WeaponHandPose"/>: if it provides a hand target, the hand
    /// blends toward that target; otherwise it blends back to the captured default. Because the blend
    /// is exponential and fast, hands stay glued to the weapon during play and only visibly move when
    /// the grip changes on a weapon switch.
    /// </summary>
    [DisallowMultipleComponent]
    public class HandPoseDriver : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private WeaponInventory inventory;
        [Tooltip("Root transform of the trigger / dominant hand mesh.")]
        [SerializeField] private Transform triggerHand;
        [Tooltip("Root transform of the support / off hand mesh.")]
        [SerializeField] private Transform supportHand;

        [Header("Blend")]
        [Tooltip("How fast hands ease toward the target pose (higher = snappier).")]
        [SerializeField] private float blendSpeed = 12f;

        // Captured default (rifle) local poses.
        private Vector3 _defTriggerPos, _defSupportPos;
        private Quaternion _defTriggerRot, _defSupportRot;
        private bool _hasTriggerDefault, _hasSupportDefault;
        private bool _initialized;

        // Reload override: while active an external driver (WeaponReloadAnimator) supplies the
        // support hand's world pose so it can carry the magazine. The override forces the hand
        // visible and wins over the weapon's WeaponHandPose target.
        private bool _supportOverride;
        private Vector3 _supportOvPos;
        private Quaternion _supportOvRot;

        /// <summary>Force the support hand to a world pose (used during reload to carry the mag).</summary>
        public void SetSupportOverride(Vector3 worldPos, Quaternion worldRot)
        {
            _supportOverride = true;
            _supportOvPos = worldPos;
            _supportOvRot = worldRot;
        }

        /// <summary>Release the support-hand override so it returns to the weapon grip.</summary>
        public void ClearSupportOverride() => _supportOverride = false;

        private void Awake()
        {
            if (inventory == null) inventory = FindFirstObjectByType<WeaponInventory>();
            if (triggerHand == null) triggerHand = transform.Find("BlackHand tetik");
            if (supportHand == null) supportHand = transform.Find("BlackHand");

            if (triggerHand != null)
            {
                _defTriggerPos = triggerHand.localPosition;
                _defTriggerRot = triggerHand.localRotation;
                _hasTriggerDefault = true;
            }
            if (supportHand != null)
            {
                _defSupportPos = supportHand.localPosition;
                _defSupportRot = supportHand.localRotation;
                _hasSupportDefault = true;
            }
        }

        private void LateUpdate()
        {
            WeaponHandPose pose = ActivePose();

            // First frame: snap (hands are already at their default, so this is a no-op but avoids
            // an initial blend from a stale value).
            float k = _initialized ? 1f - Mathf.Exp(-blendSpeed * Time.deltaTime) : 1f;

            if (triggerHand != null && _hasTriggerDefault)
            {
                ResolveTarget(triggerHand, pose != null ? pose.TriggerHandTarget : null,
                    _defTriggerPos, _defTriggerRot, out Vector3 tp, out Quaternion tr);
                triggerHand.localPosition = Vector3.Lerp(triggerHand.localPosition, tp, k);
                triggerHand.localRotation = Quaternion.Slerp(triggerHand.localRotation, tr, k);
            }

            if (supportHand != null && _hasSupportDefault)
            {
                Vector3 sp;
                Quaternion sr;
                if (_supportOverride)
                {
                    // Reload is carrying the magazine: keep the hand visible and follow the driver.
                    if (!supportHand.gameObject.activeSelf) supportHand.gameObject.SetActive(true);
                    if (supportHand.parent != null)
                    {
                        sp = supportHand.parent.InverseTransformPoint(_supportOvPos);
                        sr = Quaternion.Inverse(supportHand.parent.rotation) * _supportOvRot;
                    }
                    else
                    {
                        sp = _supportOvPos;
                        sr = _supportOvRot;
                    }
                }
                else
                {
                    bool hide = pose != null && pose.HideSupportHand;
                    if (supportHand.gameObject.activeSelf == hide)
                        supportHand.gameObject.SetActive(!hide);

                    ResolveTarget(supportHand, pose != null ? pose.SupportHandTarget : null,
                        _defSupportPos, _defSupportRot, out sp, out sr);
                }

                supportHand.localPosition = Vector3.Lerp(supportHand.localPosition, sp, k);
                supportHand.localRotation = Quaternion.Slerp(supportHand.localRotation, sr, k);
            }

            _initialized = true;
        }

        /// <summary>
        /// Resolves the desired LOCAL pose (in the hand's parent space) from a world-space anchor,
        /// falling back to the captured default when no anchor is supplied.
        /// </summary>
        private static void ResolveTarget(Transform hand, Transform anchor, Vector3 defPos, Quaternion defRot,
            out Vector3 localPos, out Quaternion localRot)
        {
            if (anchor != null && hand.parent != null)
            {
                localPos = hand.parent.InverseTransformPoint(anchor.position);
                localRot = Quaternion.Inverse(hand.parent.rotation) * anchor.rotation;
            }
            else
            {
                localPos = defPos;
                localRot = defRot;
            }
        }

        private WeaponHandPose ActivePose()
        {
            WeaponFire fire = inventory != null ? inventory.CurrentFire : null;
            return fire != null ? fire.GetComponent<WeaponHandPose>() : null;
        }
    }
}
