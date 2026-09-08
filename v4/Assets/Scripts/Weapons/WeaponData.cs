using UnityEngine;
using UnityEngine.Serialization;

namespace Tactics.Weapons
{
    public enum WeaponType { Sidearm, SMG, Shotgun, Rifle, Sniper, Melee }
    public enum FireMode { Semi, Auto, Burst }
    // How much damage a weapon keeps through a wall, NOT how far it can shoot
    // through one — reach is a property of the surface alone. See WallPenetration.
    public enum PenetrationLevel { Low, Medium, High }
    // AimDownSight is a sustained stance (right click changes how you aim);
    // Shotgun is a discrete action (right click changes what one trigger pull is).
    public enum AltFireType { None = 0, AimDownSight = 1, Shotgun = 2 }
    public enum AdsMode { Hold = 0, Toggle = 1 }
    // Member order is wire format: HitZone crosses the network in HitConfirmOwnerRpc.
    public enum HitZone { Head, Body, Leg }

    /// <summary>
    /// One distance band of a falloff table: full per-pellet damage values,
    /// not head-relative multipliers, because shotgun bands change the
    /// head/body/leg ratios between bands.
    /// </summary>
    [System.Serializable]
    public struct DamageRange
    {
        public float maxDistance; // band applies while shooter→target XZ distance <= this (meters)
        public float head;
        public float body;
        public float leg;
    }

    [CreateAssetMenu(fileName = "NewWeapon", menuName = "Tactics/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("General")]
        public string weaponName;
        public WeaponType type;
        public int cost;
        public PenetrationLevel wallPenetration;

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
        // Ascending by maxDistance; past the last band it keeps applying (no
        // hard damage cutoff). Empty = legacy headDamage × hit-level multipliers.
        public DamageRange[] damageRanges = new DamageRange[0];

        [Header("Movement")]
        public float runSpeedPercent = 1.0f; // Multiplier for player base speed

        [Header("Ammo")]
        public bool infiniteAmmo = false;

        // Only read when type == WeaponType.Melee. A melee swing ignores the
        // whole bullet path (spread, pellets, falloff, wall penetration): it
        // damages the nearest target straight ahead, and which side of that
        // target was struck picks the number. Front/back are stored outright
        // rather than as a backstab multiplier so each of the four is tunable.
        [Header("Melee")]
        public float meleeRange = 2f; // metres straight ahead of the attacker
        public float meleeFrontDamage = 50f;
        public float meleeBackDamage = 100f;
        public float altMeleeFrontDamage = 75f;
        public float altMeleeBackDamage = 150f;

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
        // How the spread pattern's world radius grows with aim distance:
        // radius = tan(spread°) · REF^(1-exp) · d^exp, REF = 10 m. 1 = true
        // angular cone (default; every rifle, and what recoil assumes). Shotguns
        // use 0.5 (√d): against the fixed XZ hit rings, a pure cone's hit count
        // decays like 1/d² while Valorant's tall-silhouette geometry measures
        // ~1/d — the √d pattern reproduces the measured damage curve.
        [Range(0.25f, 1f)] public float spreadDistanceExponent = 1f;

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
        public AltFireType altFireType = AltFireType.None;
        // Hold: aim while the button is held (Vandal). Toggle: each press cycles
        // no-zoom → level 1 → ... → no-zoom (Operator).
        public AdsMode adsMode = AdsMode.Hold;
        // Tan-space FOV divisors per zoom level (1.25 → 103°→90.3°); index 0 is
        // zoom level 1. Empty = this weapon cannot ADS even with type AimDownSight.
        public float[] adsZoomLevels = new float[0];
        public float adsMoveSpeedMultiplier = 1.0f;
        public float adsFireRateMultiplier = 1.0f;
        // Alt-fire spread column, shared by both alt-fire types but consumed differently:
        //   AimDownSight — while aiming, these replace the hip first/max values only;
        //                  growth and movement penalties stay on the primary column.
        //   Shotgun      — on an alt shot these supply first/max spread AND the per-shot
        //                  growth AND the movement penalties; the primary column is untouched.
        [FormerlySerializedAs("adsFirstShotSpreadStanding")] public float altFirstShotSpreadStanding = 0.25f;
        [FormerlySerializedAs("adsFirstShotSpreadCrouched")] public float altFirstShotSpreadCrouched = 0.21f;
        [FormerlySerializedAs("adsMaxSpreadStanding")] public float altMaxSpreadStanding = 1f;
        [FormerlySerializedAs("adsMaxSpreadCrouched")] public float altMaxSpreadCrouched = 0.85f;

        // Read only for AltFireType.Shotgun. The counts default to 1 rather than 0 so a
        // bug that ever routes an alt shot to another weapon can't divide by zero.
        [Header("Alt Fire — Shotgun burst")]
        public int altPelletCount = 1; // pellets per full alt trigger pull, and rounds it costs
        public float altFireRate = 1f; // absolute bursts per second, NOT a multiplier
        public float altSpreadPerShotDegrees = 0f;
        public float altMovePenaltyCrouchWalk = 0f;
        public float altMovePenaltyWalk = 0f;
        public float altMovePenaltyRun = 0f;
        public float altMovePenaltyAirborne = 0f;
        [Range(0.25f, 1f)] public float altSpreadDistanceExponent = 1f;
    }
}
