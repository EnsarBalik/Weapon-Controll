using System;
using UnityEngine;
using WeaponControl.Core;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Magazine + reserve ammo with reload logic.
    ///
    /// Chamber model: a full weapon holds magazineSize + 1 (one round chambered).
    ///   * Tactical reload (rounds still in the magazine) keeps the chambered round,
    ///     so you end up with magazineSize + 1. Uses the faster reload time.
    ///   * Empty reload (0 rounds) must chamber a round, so you end up with magazineSize.
    ///     Uses the slower reload time.
    ///
    /// Plugs into <see cref="WeaponFire"/> through its ammo hooks, so WeaponFire itself
    /// stays ammo-agnostic. Attach to the weapon object (the AKM) alongside WeaponFire.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponAmmo : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private WeaponData data;
        [SerializeField] private WeaponFire fire;
        [SerializeField] private WeaponProceduralMotion motion;

        [Header("Behaviour")]
        [Tooltip("Automatically start a reload when trying to fire on empty.")]
        [SerializeField] private bool autoReloadOnEmpty = true;

        public int MagazineCount { get; private set; }
        public int ReserveCount { get; private set; }
        public bool IsReloading { get; private set; }
        /// <summary>True while the in-progress reload is an empty reload (needs to chamber a round).</summary>
        public bool IsEmptyReload { get; private set; }

        /// <summary>(magazine, reserve) whenever either value changes.</summary>
        public event Action<int, int> AmmoChanged;
        /// <summary>Reload began; argument is the reload duration in seconds.</summary>
        public event Action<float> ReloadStarted;
        public event Action ReloadCompleted;
        public event Action ReloadCancelled;

        private float _reloadTimer;
        private float _reloadDuration;

        private void Awake()
        {
            if (data == null && fire != null) data = fire.Data;
            if (fire == null) fire = GetComponent<WeaponFire>();
            if (data == null && fire != null) data = fire.Data;
            if (motion == null) motion = GetComponentInParent<WeaponProceduralMotion>();

            if (data != null)
            {
                MagazineCount = data.magazineSize + 1; // start fully loaded with one chambered
                ReserveCount = data.startReserve;
            }
        }

        private void OnEnable()
        {
            if (fire != null)
            {
                fire.CanFire = CanFire;
                fire.AmmoConsumed = ConsumeRound;
                fire.DryFired += OnDryFired;
            }
        }

        private void OnDisable()
        {
            if (fire != null)
            {
                if (fire.CanFire == CanFire) fire.CanFire = null;
                if (fire.AmmoConsumed == ConsumeRound) fire.AmmoConsumed = null;
                fire.DryFired -= OnDryFired;
            }
        }

        private void Start()
        {
            AmmoChanged?.Invoke(MagazineCount, ReserveCount);
        }

        private void Update()
        {
            bool locked = fire != null && fire.InputLocked;
            if (!locked && GameInput.Player.Reload.WasPressedThisFrame())
                StartReload();

            if (IsReloading)
            {
                _reloadTimer -= Time.deltaTime;
                if (_reloadTimer <= 0f)
                    CompleteReload();
            }
        }

        private bool CanFire() => !IsReloading && MagazineCount > 0;

        private void ConsumeRound()
        {
            MagazineCount = Mathf.Max(0, MagazineCount - 1);
            AmmoChanged?.Invoke(MagazineCount, ReserveCount);
        }

        private void OnDryFired()
        {
            if (autoReloadOnEmpty && !IsReloading && MagazineCount == 0)
                StartReload();
        }

        /// <summary>Begin a reload if it's possible and useful.</summary>
        public void StartReload()
        {
            if (data == null || IsReloading) return;
            if (ReserveCount <= 0) return;

            int target = MagazineCount > 0 ? data.magazineSize + 1 : data.magazineSize;
            if (MagazineCount >= target) return; // already full

            IsReloading = true;
            IsEmptyReload = MagazineCount <= 0;
            _reloadDuration = MagazineCount > 0 ? data.tacticalReloadTime : data.emptyReloadTime;
            _reloadTimer = _reloadDuration;

            motion?.CancelInspect();
            motion?.SetReloadPose(true);
            ReloadStarted?.Invoke(_reloadDuration);
        }

        /// <summary>Cancel an in-progress reload (e.g. on weapon switch in Phase 8).</summary>
        public void CancelReload()
        {
            if (!IsReloading) return;
            IsReloading = false;
            motion?.SetReloadPose(false);
            ReloadCancelled?.Invoke();
        }

        private void CompleteReload()
        {
            int target = MagazineCount > 0 ? data.magazineSize + 1 : data.magazineSize;
            int needed = target - MagazineCount;
            int taken = Mathf.Min(needed, ReserveCount);

            MagazineCount += taken;
            ReserveCount -= taken;

            IsReloading = false;
            motion?.SetReloadPose(false);

            AmmoChanged?.Invoke(MagazineCount, ReserveCount);
            ReloadCompleted?.Invoke();
        }
    }
}
