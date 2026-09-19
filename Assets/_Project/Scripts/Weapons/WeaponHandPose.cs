using UnityEngine;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Per-weapon hand placement. Attach to a weapon root (next to <see cref="WeaponReferences"/>)
    /// and point the fields at empty child transforms placed where each hand should grip THIS weapon.
    ///
    /// The shared hands are driven toward these targets by <see cref="HandPoseDriver"/>. Any target
    /// left empty falls back to the driver's captured default (rifle) pose, so AKM/M4 - which share a
    /// grip - need no <see cref="WeaponHandPose"/> at all; only weapons with a different grip (e.g. a
    /// pistol) do.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponHandPose : MonoBehaviour
    {
        [Tooltip("Where the trigger / dominant hand should sit for this weapon. Empty = default pose.")]
        [SerializeField] private Transform triggerHandTarget;
        [Tooltip("Where the support / off hand should sit for this weapon. Empty = default pose.")]
        [SerializeField] private Transform supportHandTarget;
        [Tooltip("Hide the support hand entirely for this weapon (e.g. a one-handed pistol).")]
        [SerializeField] private bool hideSupportHand = false;

        public Transform TriggerHandTarget => triggerHandTarget;
        public Transform SupportHandTarget => supportHandTarget;
        public bool HideSupportHand => hideSupportHand;
    }
}
