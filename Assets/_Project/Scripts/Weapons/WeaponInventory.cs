using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WeaponControl.Core;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Slot-based weapon inventory. Each weapon is a child GameObject (with WeaponFire /
    /// WeaponAmmo / WeaponReferences) under the shared RecoilPivot. Only one is active at a
    /// time; switching plays a holster (lower) → swap → draw (raise) sequence and locks
    /// firing for the duration.
    ///
    /// Cycle with the Previous / Next actions (keys 1 and 2 by default).
    /// Attach to the WeaponHolder (or any object above the weapons).
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponInventory : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private WeaponProceduralMotion motion;
        [Tooltip("Parent whose children are the weapons (usually the RecoilPivot). Used only if the weapon list is empty.")]
        [SerializeField] private Transform weaponsRoot;

        [Header("Weapons")]
        [Tooltip("Weapon GameObjects. If left empty, auto-collected from children of Weapons Root that have a WeaponReferences.")]
        [SerializeField] private List<GameObject> weapons = new List<GameObject>();

        [Header("Switch Timing")]
        [SerializeField] private float holsterTime = 0.22f;
        [SerializeField] private float drawTime = 0.3f;

        public int CurrentIndex { get; private set; } = -1;
        public int Count => weapons.Count;
        public WeaponFire CurrentFire =>
            CurrentIndex >= 0 && CurrentIndex < weapons.Count && weapons[CurrentIndex] != null
                ? weapons[CurrentIndex].GetComponent<WeaponFire>()
                : null;

        /// <summary>Raised after a switch completes: (index, activeWeaponFire).</summary>
        public event Action<int, WeaponFire> WeaponChanged;

        private bool _switching;

        private void Awake()
        {
            if (motion == null) motion = GetComponentInParent<WeaponProceduralMotion>();
            if (motion == null) motion = GetComponentInChildren<WeaponProceduralMotion>();

            if (weapons.Count == 0)
            {
                Transform root = weaponsRoot != null ? weaponsRoot : transform;
                var found = root.GetComponentsInChildren<WeaponReferences>(true);
                foreach (var w in found)
                    weapons.Add(w.gameObject);
            }
        }

        private void Start()
        {
            if (weapons.Count == 0) return;
            EquipImmediate(0);
        }

        private void Update()
        {
            if (_switching || weapons.Count <= 1) return;

            if (GameInput.Player.Next.WasPressedThisFrame())
                SwitchTo(CurrentIndex + 1);
            else if (GameInput.Player.Previous.WasPressedThisFrame())
                SwitchTo(CurrentIndex - 1);
        }

        /// <summary>Request a switch to a slot (wraps around). Ignored if already switching/equipped.</summary>
        public void SwitchTo(int index)
        {
            if (weapons.Count == 0) return;
            index = ((index % weapons.Count) + weapons.Count) % weapons.Count;
            if (_switching || index == CurrentIndex) return;
            StartCoroutine(SwitchRoutine(index));
        }

        private void EquipImmediate(int index)
        {
            for (int i = 0; i < weapons.Count; i++)
                if (weapons[i] != null) weapons[i].SetActive(i == index);

            CurrentIndex = index;
            var refs = weapons[index].GetComponent<WeaponReferences>();
            motion?.SetWeapon(refs);

            weapons[index].GetComponent<WeaponAudio>()?.PlayEquip();

            var fire = weapons[index].GetComponent<WeaponFire>();
            WeaponChanged?.Invoke(CurrentIndex, fire);
        }

        private IEnumerator SwitchRoutine(int index)
        {
            _switching = true;

            // --- Holster current ---
            GameObject oldGo = CurrentIndex >= 0 ? weapons[CurrentIndex] : null;
            if (oldGo != null)
            {
                var oldFire = oldGo.GetComponent<WeaponFire>();
                if (oldFire != null) oldFire.InputLocked = true;
                oldGo.GetComponent<WeaponAmmo>()?.CancelReload();
            }

            motion?.CancelInspect();
            motion?.SetHolster(true);
            yield return new WaitForSeconds(holsterTime);

            if (oldGo != null) oldGo.SetActive(false);

            // --- Swap ---
            GameObject newGo = weapons[index];
            newGo.SetActive(true);
            CurrentIndex = index;

            var newRefs = newGo.GetComponent<WeaponReferences>();
            motion?.SetWeapon(newRefs);

            var newFire = newGo.GetComponent<WeaponFire>();
            if (newFire != null) newFire.InputLocked = true;

            // --- Draw new ---
            motion?.SetHolster(false);
            newGo.GetComponent<WeaponAudio>()?.PlayEquip();
            yield return new WaitForSeconds(drawTime);

            if (newFire != null) newFire.InputLocked = false;
            _switching = false;

            WeaponChanged?.Invoke(CurrentIndex, newFire);
        }
    }
}
