using UnityEngine;

namespace WeaponControl.Weapons
{
    /// <summary>
    /// Plays weapon one-shots (fire / dry / reload / inspect) through a local AudioSource.
    /// Attach to each weapon next to <see cref="WeaponFire"/>. Clips come from <see cref="WeaponData"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponAudio : MonoBehaviour
    {
        [SerializeField] private WeaponFire fire;
        [SerializeField] private WeaponAmmo ammo;
        [SerializeField] private WeaponProceduralMotion motion;
        [SerializeField] private AudioSource source;
        [SerializeField] private float firePitchJitter = 0.04f;

        private void Awake()
        {
            if (fire == null) fire = GetComponent<WeaponFire>();
            if (ammo == null) ammo = GetComponent<WeaponAmmo>();
            if (motion == null) motion = GetComponentInParent<WeaponProceduralMotion>();
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            if (fire != null)
            {
                fire.Fired += OnFired;
                fire.DryFired += OnDryFired;
            }
            if (ammo != null) ammo.ReloadStarted += OnReloadStarted;
            if (motion != null) motion.InspectStarted += OnInspectStarted;
        }

        private void OnDisable()
        {
            if (fire != null)
            {
                fire.Fired -= OnFired;
                fire.DryFired -= OnDryFired;
            }
            if (ammo != null) ammo.ReloadStarted -= OnReloadStarted;
            if (motion != null) motion.InspectStarted -= OnInspectStarted;
        }

        private void OnFired()
        {
            var clip = fire != null && fire.Data != null ? fire.Data.fireClip : null;
            Play(clip, 1f, 1f + Random.Range(-firePitchJitter, firePitchJitter));
        }

        private void OnDryFired()
        {
            var clip = fire != null && fire.Data != null ? fire.Data.dryFireClip : null;
            Play(clip, 0.7f, 0.85f);
        }

        private void OnReloadStarted(float duration)
        {
            var clip = fire != null && fire.Data != null ? fire.Data.reloadClip : null;
            Play(clip, 1f, 1f);
        }

        private void OnInspectStarted()
        {
            var clip = fire != null && fire.Data != null ? fire.Data.inspectClip : null;
            Play(clip, 0.8f, 1f);
        }

        /// <summary>Play the equip/draw one-shot. Called by <see cref="WeaponInventory"/> when raised.</summary>
        public void PlayEquip()
        {
            var clip = fire != null && fire.Data != null ? fire.Data.equipClip : null;
            Play(clip, 1f, 1f);
        }

        /// <summary>Reload-phase one-shots, triggered by <see cref="WeaponReloadAnimator"/>.</summary>
        public void PlayMagOut()
        {
            var clip = fire != null && fire.Data != null ? fire.Data.magOutClip : null;
            Play(clip, 1f, 1f);
        }

        public void PlayMagIn()
        {
            var clip = fire != null && fire.Data != null ? fire.Data.magInClip : null;
            Play(clip, 1f, 1f);
        }

        public void PlayRack()
        {
            var clip = fire != null && fire.Data != null ? fire.Data.rackClip : null;
            Play(clip, 1f, 1f);
        }

        private void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || source == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }
    }
}
