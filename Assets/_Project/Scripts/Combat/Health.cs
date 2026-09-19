using System;
using UnityEngine;
using WeaponControl.Core;

namespace WeaponControl.Combat
{
    /// <summary>
    /// Simple health container that implements <see cref="IDamageable"/>.
    /// Raises C# events so visuals / UI / AI can react without tight coupling.
    /// </summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private bool destroyOnDeath = false;
        [SerializeField] private float destroyDelay = 3f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public bool IsDead { get; private set; }
        public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;

        /// <summary>Fired on every damage event (before death is processed).</summary>
        public event Action<DamageInfo> Damaged;
        /// <summary>Fired whenever current health changes: (current, max).</summary>
        public event Action<float, float> HealthChanged;
        /// <summary>Fired once when health reaches zero.</summary>
        public event Action<DamageInfo> Died;

        private void Awake()
        {
            Current = maxHealth;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (IsDead) return;

            Current = Mathf.Max(0f, Current - info.Amount);
            Damaged?.Invoke(info);
            HealthChanged?.Invoke(Current, maxHealth);

            if (Current <= 0f)
            {
                IsDead = true;
                Died?.Invoke(info);
                if (destroyOnDeath)
                    Destroy(gameObject, destroyDelay);
            }
        }

        /// <summary>Restore health to full (useful for respawning test targets).</summary>
        public void ResetHealth()
        {
            IsDead = false;
            Current = maxHealth;
            HealthChanged?.Invoke(Current, maxHealth);
        }
    }
}
