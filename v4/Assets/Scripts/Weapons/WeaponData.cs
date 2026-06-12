using UnityEngine;

namespace ValorantTrainer.Weapons
{
    public enum WeaponType { Sidearm, SMG, Shotgun, Rifle, Sniper, Melee }
    public enum FireMode { Semi, Auto, Burst }
    public enum WallPenetration { Low, Medium, High }

    [CreateAssetMenu(fileName = "NewWeapon", menuName = "Valorant Trainer/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("General")]
        public string weaponName;
        public WeaponType type;
        public int cost;
        public WallPenetration wallPenetration;

        [Header("Primary Fire")]
        public FireMode fireMode;
        public float fireRate; // Rounds per second
        public int magazineSize;
        public int reserveAmmo;
        public int pelletCount = 1;
        public float reloadSpeed; // In seconds
        public float equipSpeed; // In seconds

        [Header("Damage")]
        public float headDamage;
        public float bodyDamage;
        public float legDamage;
        public float rangeStart;
        public float rangeEnd;
        public float headDamageFalloff;
        public float bodyDamageFalloff;
        public float legDamageFalloff;

        [Header("Movement")]
        public float runSpeedPercent = 1.0f; // Multiplier for player base speed

        [Header("Hit Level Multipliers (Relative to Head Damage)")]
        public float perfectMultiplier = 1.0f;
        public float mediumMultiplier = 0.33f;
        public float lowMultiplier = 0.28f;

        [Header("Alt Fire")]
        public bool hasAltFire;
        public string altFireFunction;
        public float zoomMultiplier = 1.0f;
    }
}
