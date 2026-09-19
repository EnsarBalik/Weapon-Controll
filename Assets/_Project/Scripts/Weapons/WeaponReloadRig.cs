using UnityEngine;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Per-weapon reload rig. Attach to the weapon root (next to <see cref="WeaponReferences"/>) and
    /// assign the seated magazine plus an (optional) spare magazine mesh that the hand brings in.
    ///
    /// All animated poses are expressed in the seated magazine's parent space (<see cref="MagSpace"/>)
    /// so the animation is correct regardless of weapon scale. The spare magazine can live anywhere,
    /// but placing it under the same weapon keeps its scale consistent.
    ///
    /// Waypoints are optional: leave them empty to fall back to simple local offsets.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponReloadRig : MonoBehaviour
    {
        [Header("Magazines")]
        [Tooltip("The magazine currently seated in the weapon (a child of the weapon).")]
        [SerializeField] private Transform inWellMagazine;
        [Tooltip("Duplicate magazine mesh, hidden until the insert phase. Optional.")]
        [SerializeField] private Transform spareMagazine;

        [Header("Waypoints (optional - any transform; its pose is sampled)")]
        [Tooltip("Where the ejected magazine ends up (pulled out below the well). Empty = ejectOffset.")]
        [SerializeField] private Transform ejectedPoint;
        [Tooltip("Where the fresh magazine starts (down at the pouch / hand). Empty = fetchOffset.")]
        [SerializeField] private Transform fetchPoint;

        [Header("Fallback offsets (magazine-parent local space)")]
        [SerializeField] private Vector3 ejectOffset = new Vector3(0f, -0.12f, 0f);
        [SerializeField] private Vector3 fetchOffset = new Vector3(0.05f, -0.30f, -0.02f);

        [Header("Support-hand grip (offset from the magazine it is carrying)")]
        [SerializeField] private Vector3 handGripOffset = Vector3.zero;
        [SerializeField] private Vector3 handGripEuler = Vector3.zero;

        [Header("Charging handle / slide (empty reload only)")]
        [Tooltip("Bolt handle (rifle) or slide (pistol) that racks after an empty reload. Optional.")]
        [SerializeField] private Transform chargingHandle;
        [Tooltip("Local travel (in the charging handle's parent space) when pulled fully back.")]
        [SerializeField] private Vector3 chargeTravel = new Vector3(0f, 0f, -0.08f);
        [Tooltip("Extra offset from the charging handle to where the hand grips it.")]
        [SerializeField] private Vector3 chargeHandGripOffset = Vector3.zero;
        [SerializeField] private Vector3 chargeHandGripEuler = Vector3.zero;

        private Vector3 _homePos;
        private Quaternion _homeRot;
        private bool _captured;

        private Vector3 _chargeHomePos;
        private Quaternion _chargeHomeRot;
        private bool _chargeCaptured;

        public Transform InWellMagazine => inWellMagazine;
        public Transform SpareMagazine => spareMagazine;
        public Transform ChargingHandle => chargingHandle;
        public bool HasChargingHandle => chargingHandle != null;

        /// <summary>The space all magazine poses are authored/animated in (the seated mag's parent).</summary>
        public Transform MagSpace => inWellMagazine != null ? inWellMagazine.parent : transform;
        private Transform ChargeSpace => chargingHandle != null ? chargingHandle.parent : transform;

        private void Awake() => CaptureHome();

        /// <summary>Capture the seated magazine + charging handle poses in their parent local space.</summary>
        public void CaptureHome()
        {
            if (inWellMagazine != null)
            {
                _homePos = inWellMagazine.localPosition;
                _homeRot = inWellMagazine.localRotation;
                _captured = true;
            }
            if (chargingHandle != null)
            {
                _chargeHomePos = chargingHandle.localPosition;
                _chargeHomeRot = chargingHandle.localRotation;
                _chargeCaptured = true;
            }
        }

        public bool TryGetHome(out Vector3 pos, out Quaternion rot)
        {
            pos = _homePos;
            rot = _homeRot;
            return _captured;
        }

        public void GetEjected(out Vector3 pos, out Quaternion rot)
        {
            if (ejectedPoint != null) ToMagLocal(ejectedPoint, out pos, out rot);
            else { pos = _homePos + ejectOffset; rot = _homeRot; }
        }

        public void GetFetch(out Vector3 pos, out Quaternion rot)
        {
            if (fetchPoint != null) ToMagLocal(fetchPoint, out pos, out rot);
            else { pos = _homePos + fetchOffset; rot = _homeRot; }
        }

        /// <summary>Places a magazine at a MagSpace-local pose using world space (parent-agnostic).</summary>
        public void PlaceMag(Transform mag, Vector3 localPos, Quaternion localRot)
        {
            if (mag == null) return;
            Transform space = MagSpace;
            mag.position = space.TransformPoint(localPos);
            mag.rotation = space.rotation * localRot;
        }

        /// <summary>World-space support-hand grip pose for a carry point given in MagSpace-local.</summary>
        public void GetHandGripWorld(Vector3 localPos, Quaternion localRot, out Vector3 pos, out Quaternion rot)
        {
            Transform space = MagSpace;
            Vector3 worldPos = space.TransformPoint(localPos);
            Quaternion worldRot = space.rotation * localRot;
            rot = worldRot * Quaternion.Euler(handGripEuler);
            pos = worldPos + worldRot * handGripOffset;
        }

        /// <summary>Slides the charging handle between seated (0) and fully pulled back (1).</summary>
        public void PlaceCharge(float pull)
        {
            if (chargingHandle == null || !_chargeCaptured) return;
            chargingHandle.localPosition = _chargeHomePos + chargeTravel * Mathf.Clamp01(pull);
            chargingHandle.localRotation = _chargeHomeRot;
        }

        /// <summary>World-space support-hand grip pose for the charging handle at a given pull amount.</summary>
        public void GetChargeHandGripWorld(float pull, out Vector3 pos, out Quaternion rot)
        {
            Transform space = ChargeSpace;
            Vector3 localPos = _chargeCaptured ? _chargeHomePos + chargeTravel * Mathf.Clamp01(pull)
                                               : (chargingHandle != null ? chargingHandle.localPosition : Vector3.zero);
            Quaternion localRot = _chargeCaptured ? _chargeHomeRot
                                                  : (chargingHandle != null ? chargingHandle.localRotation : Quaternion.identity);
            Vector3 worldPos = space.TransformPoint(localPos);
            Quaternion worldRot = space.rotation * localRot;
            rot = worldRot * Quaternion.Euler(chargeHandGripEuler);
            pos = worldPos + worldRot * chargeHandGripOffset;
        }

        private void ToMagLocal(Transform t, out Vector3 pos, out Quaternion rot)
        {
            Transform space = MagSpace;
            pos = space.InverseTransformPoint(t.position);
            rot = Quaternion.Inverse(space.rotation) * t.rotation;
        }
    }
}
