using UnityEngine;

namespace Tactics.Weapons
{
    /// <summary>
    /// Pure ADS zoom-level rules, shared by input sampling (PlayerController)
    /// and the vision zoom (VisionController). No Unity scene state, so the
    /// toggle-cycle and level→multiplier mapping are edit-mode testable.
    ///
    /// A "zoom level" is 0 (not aiming) or a 1-based index into
    /// <see cref="WeaponData.adsZoomLevels"/>. Hold-mode weapons only ever use
    /// level 0/1; toggle-mode weapons cycle 0 → 1 → ... → N → 0.
    /// </summary>
    public static class AdsZoomLogic
    {
        /// <summary>How many zoom levels this weapon offers; 0 = cannot ADS.</summary>
        public static int LevelCount(WeaponData weapon)
        {
            if (weapon == null || weapon.altFireType != AltFireType.AimDownSight) return 0;
            return weapon.adsZoomLevels != null ? weapon.adsZoomLevels.Length : 0;
        }

        /// <summary>Next level after one toggle press: cycles 0..levelCount then wraps to 0.</summary>
        public static int NextToggleLevel(int currentLevel, int levelCount)
        {
            if (levelCount <= 0) return 0;
            return (Mathf.Clamp(currentLevel, 0, levelCount) + 1) % (levelCount + 1);
        }

        /// <summary>Tan-space FOV divisor for a level; 1 for level 0, out-of-range, or non-ADS weapons.</summary>
        public static float ZoomForLevel(WeaponData weapon, int level)
        {
            if (level <= 0 || level > LevelCount(weapon)) return 1f;
            return Mathf.Max(1f, weapon.adsZoomLevels[level - 1]);
        }
    }
}
