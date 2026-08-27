using UnityEngine;

namespace Tactics.Weapons
{
    public enum WeaponType { Sidearm, SMG, Shotgun, Rifle, Sniper, Melee }
    public enum FireMode { Semi, Auto, Burst }
    public enum WallPenetration { Low, Medium, High }

    [CreateAssetMenu(fileName = "NewWeapon", menuName = "Tactics/Weapon Data")]
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

        [Header("Ammo")]
        public bool infiniteAmmo = false;

        // All angles are degrees off the shooter→aim axis; SpreadCalculator
        // projects them onto the ground as distance · tan(angle), so the same
        // numbers Valorant publishes per weapon can be transcribed directly.
        [Header("Spread (degrees, Valorant-style)")]
        public float firstShotSpreadStanding = 0.25f;
        public float firstShotSpreadCrouched = 0.21f;
        public float maxSpreadStanding = 1f;
        public float maxSpreadCrouched = 0.85f;
        public float spreadPerShotDegrees = 0.11f; // growth per consecutive shot toward max
        public float movePenaltyCrouchWalk = 0.8f;
        public float movePenaltyWalk = 3f;
        public float movePenaltyRun = 6f;
        public float movePenaltyAirborne = 10f;

        // The "T" every spray traces: a backward climb (shots land beyond the
        // aim point) that tops out, then constant left/right sway. Guns differ
        // only in how fast and how far the climb goes.
        [Header("Recoil (T pattern)")]
        public float recoilClimbDegrees = 4f; // backward kick at the top of the T
        public float recoilClimbShots = 7f; // spray index where the climb tops out
        public AnimationCurve recoilClimbCurve = new AnimationCurve( // normalized: gentle through ~shot 3, steep to the top
            new Keyframe(0f, 0f, 0f, 0.25f),
            new Keyframe(0.45f, 0.15f, 0.6f, 0.6f),
            new Keyframe(1f, 1f, 1.8f, 0f));
        public float recoilSwayDegrees = 2f; // half-width of the T's top bar
        public float recoilSwayPeriodShots = 5f; // full left→right→left cycle (~2-3 shots per side)

        [Header("Spray Recovery")]
        public float sprayDecayDelay = 0.15f; // grace before accuracy starts recovering
        public float sprayDecayPerSecond = 15f; // spray-index units recovered per second after that

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
