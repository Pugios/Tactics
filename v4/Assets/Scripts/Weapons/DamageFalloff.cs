namespace Tactics.Weapons
{
    /// <summary>
    /// Pure-logic damage-by-distance lookup, no Unity scene state (mirrors
    /// <c>SpreadCalculator</c>). Weapons without a falloff table keep the
    /// legacy scheme where head damage is the base and body/leg are encoded
    /// as hit-level multipliers; banded weapons (shotguns) carry explicit
    /// head/body/leg values per band because the ratios differ between bands.
    /// </summary>
    public static class DamageFalloff
    {
        /// <summary>
        /// Base damage for one pellet/bullet in the given zone at the given
        /// shooter→target distance. Bands are checked in ascending order and
        /// the first with maxDistance >= distance wins; beyond the last band
        /// the last band keeps applying — range never zeroes a hit outright.
        /// </summary>
        public static float GetZoneDamage(WeaponData weapon, float distanceMeters, HitZone zone)
        {
            var bands = weapon.damageRanges;
            if (bands == null || bands.Length == 0)
            {
                switch (zone)
                {
                    case HitZone.Head: return weapon.headDamage * weapon.perfectMultiplier;
                    case HitZone.Body: return weapon.headDamage * weapon.mediumMultiplier;
                    default: return weapon.headDamage * weapon.lowMultiplier;
                }
            }

            DamageRange band = bands[bands.Length - 1];
            for (int i = 0; i < bands.Length; i++)
            {
                if (distanceMeters <= bands[i].maxDistance)
                {
                    band = bands[i];
                    break;
                }
            }

            switch (zone)
            {
                case HitZone.Head: return band.head;
                case HitZone.Body: return band.body;
                default: return band.leg;
            }
        }
    }
}
