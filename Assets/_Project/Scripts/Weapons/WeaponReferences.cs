using UnityEngine;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Holds the important anchor transforms on a weapon so other systems don't need to
    /// hard-code child lookups:
    ///   - <see cref="Muzzle"/>       : barrel tip, +Z pointing out the barrel (fire origin / muzzle flash).
    ///   - <see cref="EjectionPort"/> : where spent casings spawn, +Z / +X pointing out to the side.
    ///   - <see cref="AimPoint"/>     : the point that should line up with the camera center when
    ///                                  aiming down sights (ADS). Used in Phase 4.
    ///   - <see cref="GripPoint"/>    : optional reference for the dominant hand / holster pivots.
    ///
    /// Attach to the weapon root (e.g. the AKM object).
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponReferences : MonoBehaviour
    {
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform ejectionPort;
        [SerializeField] private Transform aimPoint;
        [SerializeField] private Transform gripPoint;

        public Transform Muzzle => muzzle;
        public Transform EjectionPort => ejectionPort;
        public Transform AimPoint => aimPoint;
        public Transform GripPoint => gripPoint;

#if UNITY_EDITOR
        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private float gizmoSize = 0.02f;

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            if (muzzle != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(muzzle.position, gizmoSize);
                Gizmos.DrawLine(muzzle.position, muzzle.position + muzzle.forward * 0.25f);
            }

            if (ejectionPort != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(ejectionPort.position, gizmoSize);
                Gizmos.DrawLine(ejectionPort.position, ejectionPort.position + ejectionPort.forward * 0.1f);
            }

            if (aimPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(aimPoint.position, gizmoSize);
                Gizmos.DrawLine(aimPoint.position, aimPoint.position + aimPoint.forward * 0.3f);
            }

            if (gripPoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(gripPoint.position, gizmoSize);
            }
        }
#endif
    }
}
