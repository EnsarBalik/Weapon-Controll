using UnityEngine;

namespace WeaponControl.Core
{
    /// <summary>Data passed to anything taking damage from a weapon.</summary>
    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 Direction;
        public GameObject Source;
    }

    /// <summary>Implemented by anything that can be shot (targets, enemies, breakables).</summary>
    public interface IDamageable
    {
        void TakeDamage(in DamageInfo info);
    }
}
