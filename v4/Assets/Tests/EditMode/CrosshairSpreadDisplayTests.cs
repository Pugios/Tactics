using NUnit.Framework;
using UnityEngine;
using Tactics.Player;
using Tactics.Weapons;

namespace Tactics.Tests.EditMode
{
    /// <summary>
    /// The crosshair's "Show Firing Error" / "Show Movement Error" toggles leave
    /// one error source out of the displayed cone and nothing else.
    /// </summary>
    public class CrosshairSpreadDisplayTests
    {
        private const float SprayIndex = 6f;

        /// <summary>Shotgun-shaped weapon with growth and movement penalties large enough to tell apart.</summary>
        private static WeaponData MakeWeapon()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.firstShotSpreadStanding = 8f;
            weapon.firstShotSpreadCrouched = 6f;
            weapon.maxSpreadStanding = 20f;
            weapon.maxSpreadCrouched = 18f;
            weapon.spreadPerShotDegrees = 1f;
            weapon.movePenaltyCrouchWalk = 1f;
            weapon.movePenaltyWalk = 2f;
            weapon.movePenaltyRun = 4f;
            weapon.movePenaltyAirborne = 8f;
            weapon.altFireType = AltFireType.None;
            return weapon;
        }

        [Test]
        public void BothShown_MatchesTheShotSpread()
        {
            WeaponData weapon = MakeWeapon();
            float shot = SpreadCalculator.ComputeSpreadDegrees(weapon, SprayIndex, false, false, MovementState.Running);
            float shown = SpreadCalculator.ComputeDisplayedSpreadDegrees(weapon, SprayIndex, false, false,
                MovementState.Running, includeFiringError: true, includeMovementError: true);
            Assert.AreEqual(shot, shown);
            Assert.AreEqual(8f + 6f + 4f, shown, 1e-4f);
        }

        [Test]
        public void FiringErrorHidden_DropsSprayGrowthOnly()
        {
            WeaponData weapon = MakeWeapon();
            float shown = SpreadCalculator.ComputeDisplayedSpreadDegrees(weapon, SprayIndex, false, false,
                MovementState.Running, includeFiringError: false, includeMovementError: true);
            Assert.AreEqual(8f + 4f, shown, 1e-4f);
        }

        [Test]
        public void MovementErrorHidden_DropsMovementPenaltyOnly()
        {
            WeaponData weapon = MakeWeapon();
            float shown = SpreadCalculator.ComputeDisplayedSpreadDegrees(weapon, SprayIndex, false, false,
                MovementState.Airborne, includeFiringError: true, includeMovementError: false);
            Assert.AreEqual(8f + 6f, shown, 1e-4f);
        }

        [Test]
        public void BothHidden_IsFirstShotSpread_AndStanceStillApplies()
        {
            WeaponData weapon = MakeWeapon();
            float standing = SpreadCalculator.ComputeDisplayedSpreadDegrees(weapon, SprayIndex, false, false,
                MovementState.Running, includeFiringError: false, includeMovementError: false);
            float crouched = SpreadCalculator.ComputeDisplayedSpreadDegrees(weapon, SprayIndex, true, false,
                MovementState.CrouchWalking, includeFiringError: false, includeMovementError: false);

            // Constant while moving or spraying — but a stance is neither error,
            // so crouching still reads the crouched column.
            Assert.AreEqual(8f, standing, 1e-4f);
            Assert.AreEqual(6f, crouched, 1e-4f);
        }
    }
}
