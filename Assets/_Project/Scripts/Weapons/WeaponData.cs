using UnityEngine;

namespace WeaponControl.Weapons
{
    public enum FireMode
    {
        Semi,
        Burst,
        Auto
    }

    /// <summary>
    /// Static configuration for a weapon. Create via
    /// Assets > Create > WeaponControl > Weapon Data.
    /// Ammo-related fields are added in Phase 7.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponData", menuName = "WeaponControl/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public string weaponName = "AKM";

        [Header("Damage")]
        public float damage = 34f;
        [Tooltip("Max hitscan distance in world meters.")]
        public float range = 300f;
        [Tooltip("Impulse applied to rigidbodies that are hit.")]
        public float impactForce = 30f;

        [Header("Fire")]
        [Tooltip("Rounds per minute.")]
        public float fireRate = 600f;
        [Tooltip("Fire modes this weapon supports; the first one is selected by default.")]
        public FireMode[] availableModes = { FireMode.Auto, FireMode.Semi };
        [Tooltip("Rounds fired per trigger pull in Burst mode.")]
        public int burstCount = 3;

        [Header("Accuracy (cone half-angle in degrees)")]
        [Tooltip("Spread while firing from the hip.")]
        public float hipSpread = 2.5f;
        [Tooltip("Spread while fully aimed down sights.")]
        public float aimSpread = 0.35f;

        [Header("Hit Detection")]
        [Tooltip("Which layers bullets can hit.")]
        public LayerMask hitMask = ~0;

        [Header("Ammo")]
        public int magazineSize = 30;
        [Tooltip("Total ammo carried in reserve at start.")]
        public int startReserve = 120;
        [Tooltip("Reload time when the magazine still has a round chambered (faster).")]
        public float tacticalReloadTime = 2.1f;
        [Tooltip("Reload time from a completely empty weapon (needs to chamber a round).")]
        public float emptyReloadTime = 2.8f;

        [Header("Feel")]
        [Tooltip("Camera shake intensity applied on each shot.")]
        public float fireShake = 0.85f;

        [Header("VFX (optional)")]
        [Tooltip("Muzzle flash prefab spawned at the barrel on each shot (root + child particle systems).")]
        public GameObject muzzleFlashPrefab;

        [Header("Audio (optional)")]
        public AudioClip fireClip;
        public AudioClip dryFireClip;
        public AudioClip reloadClip;
        public AudioClip inspectClip;
        [Tooltip("Played when the weapon is drawn / equipped.")]
        public AudioClip equipClip;

        [Header("Reload phase audio (optional; used by WeaponReloadAnimator)")]
        [Tooltip("Old magazine released / pulled out of the well.")]
        public AudioClip magOutClip;
        [Tooltip("Fresh magazine seated into the well.")]
        public AudioClip magInClip;
        [Tooltip("Charging handle / slide slamming forward after an empty reload.")]
        public AudioClip rackClip;
    }
}
