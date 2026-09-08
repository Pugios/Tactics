using NUnit.Framework;
using UnityEngine;
using Tactics.Weapons;

namespace Tactics.Tests.EditMode
{
    public class MeleeCombatTests
    {
        private const float Range = 2f;
        private const float HalfWidth = 1f; // HitZoneRadii.Leg — the target's body radius

        /// <summary>Knife-shaped weapon: 2 m reach, 50/100 primary, 75/150 alt.</summary>
        private static WeaponData MakeKnife()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.type = WeaponType.Melee;
            weapon.fireRate = 1.6f;
            weapon.altFireRate = 1.1f;
            weapon.meleeRange = Range;
            weapon.meleeFrontDamage = 50f;
            weapon.meleeBackDamage = 100f;
            weapon.altMeleeFrontDamage = 75f;
            weapon.altMeleeBackDamage = 150f;
            return weapon;
        }

        // Attacker at the origin facing +Z throughout the geometry tests.
        private static bool Swing(Vector3 targetPos, out float forwardDistance) =>
            MeleeCombat.TryGetForwardDistance(Vector3.zero, Vector3.forward, targetPos,
                Range, HalfWidth, out forwardDistance);

        #region Swing volume

        [Test]
        public void TargetDeadAheadInRangeIsHit()
        {
            Assert.IsTrue(Swing(new Vector3(0f, 0f, 1.5f), out float forward));
            Assert.AreEqual(1.5f, forward, 1e-4f);
        }

        [Test]
        public void TargetAtPointBlankIsHit()
        {
            Assert.IsTrue(Swing(new Vector3(0f, 0f, 0.05f), out _));
        }

        [Test]
        public void TargetJustPastRangeIsMissed()
        {
            Assert.IsTrue(Swing(new Vector3(0f, 0f, 1.99f), out _));
            Assert.IsFalse(Swing(new Vector3(0f, 0f, 2.01f), out _));
        }

        [Test]
        public void TargetBehindTheAttackerIsMissed()
        {
            Assert.IsFalse(Swing(new Vector3(0f, 0f, -1.5f), out _));
        }

        [Test]
        public void TargetBesideTheAttackerIsMissed()
        {
            // Straight out to the side: zero forward component.
            Assert.IsFalse(Swing(new Vector3(1.5f, 0f, 0f), out _));
        }

        [Test]
        public void TargetOutsideTheHalfWidthIsMissed()
        {
            Assert.IsTrue(Swing(new Vector3(0.95f, 0f, 1f), out _));
            Assert.IsFalse(Swing(new Vector3(1.05f, 0f, 1f), out _));
        }

        [Test]
        public void VolumeIsABoxNotACapsule()
        {
            // A capsule around the forward segment would round the far end and
            // let a target 2.5 m dead ahead sit within 1 m of the segment tip.
            // A box must not reach past the stated range.
            Assert.IsFalse(Swing(new Vector3(0f, 0f, 2.5f), out _));
        }

        [Test]
        public void HeightIsIgnored()
        {
            // The damage model is positional on XZ; a target on a step is still ahead.
            Assert.IsTrue(Swing(new Vector3(0f, 1.4f, 1.5f), out _));
        }

        [Test]
        public void VolumeFollowsTheAttackersFacing()
        {
            Vector3 target = new Vector3(1.5f, 0f, 0f);
            Assert.IsFalse(MeleeCombat.TryGetForwardDistance(Vector3.zero, Vector3.forward, target,
                Range, HalfWidth, out _));
            Assert.IsTrue(MeleeCombat.TryGetForwardDistance(Vector3.zero, Vector3.right, target,
                Range, HalfWidth, out float forward));
            Assert.AreEqual(1.5f, forward, 1e-4f);
        }

        [Test]
        public void ZeroLengthForwardIsAMiss()
        {
            Assert.IsFalse(MeleeCombat.TryGetForwardDistance(Vector3.zero, Vector3.zero,
                new Vector3(0f, 0f, 1f), Range, HalfWidth, out _));
        }

        #endregion

        #region Front / back hemisphere

        // Target sits at +Z from the attacker at the origin, so the attacker
        // approaches from the target's -Z side.

        [Test]
        public void AttackerFacedByTheTargetIsAFrontHit()
        {
            // Target yaw 180° = facing -Z = looking straight at the attacker.
            Assert.IsFalse(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), 180f));
        }

        [Test]
        public void AttackerBehindTheTargetIsABackHit()
        {
            // Target yaw 0° = facing +Z = away from the attacker.
            Assert.IsTrue(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), 0f));
        }

        [Test]
        public void HemisphereBoundaryIsAFrontHit()
        {
            // Target yaw ±90° = facing exactly across the attacker's approach.
            Assert.IsFalse(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), 90f));
            Assert.IsFalse(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), -90f));
        }

        [Test]
        public void AnywhereInTheRearHemisphereIsABackHit()
        {
            // Well past 90° off the target's back but still behind the shoulder plane.
            Assert.IsTrue(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), 80f));
            Assert.IsTrue(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), -80f));
        }

        [Test]
        public void YawWrapsCorrectly()
        {
            // 360° must behave exactly like 0°.
            Assert.IsTrue(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), 360f));
            Assert.IsFalse(MeleeCombat.IsBackHit(Vector3.zero, new Vector3(0f, 0f, 2f), 540f));
        }

        [Test]
        public void CoLocatedAttackerAndTargetIsNotABackHit()
        {
            Assert.IsFalse(MeleeCombat.IsBackHit(Vector3.zero, Vector3.zero, 0f));
        }

        #endregion

        #region Damage

        [Test]
        public void DamageTableMatchesTheKnife()
        {
            WeaponData knife = MakeKnife();
            Assert.AreEqual(50f, MeleeCombat.GetDamage(knife, altShot: false, backHit: false));
            Assert.AreEqual(100f, MeleeCombat.GetDamage(knife, altShot: false, backHit: true));
            Assert.AreEqual(75f, MeleeCombat.GetDamage(knife, altShot: true, backHit: false));
            Assert.AreEqual(150f, MeleeCombat.GetDamage(knife, altShot: true, backHit: true));
        }

        [Test]
        public void BackstabsAreDoubleTheFrontDamage()
        {
            WeaponData knife = MakeKnife();
            Assert.AreEqual(2f * MeleeCombat.GetDamage(knife, false, false),
                MeleeCombat.GetDamage(knife, false, true));
            Assert.AreEqual(2f * MeleeCombat.GetDamage(knife, true, false),
                MeleeCombat.GetDamage(knife, true, true));
        }

        [Test]
        public void AnyBackstabIsLethalFromFullHealth()
        {
            WeaponData knife = MakeKnife();
            Assert.GreaterOrEqual((int)MeleeCombat.GetDamage(knife, false, true), 100);
        }

        [Test]
        public void NullWeaponDealsNoDamage()
        {
            Assert.AreEqual(0f, MeleeCombat.GetDamage(null, false, true));
        }

        #endregion

        #region Cadence

        [Test]
        public void MeleeIsRecognisedFromWeaponTypeNotAltFireType()
        {
            WeaponData knife = MakeKnife();
            Assert.IsTrue(FireModeStats.IsMelee(knife));
            Assert.AreEqual(AltFireType.None, knife.altFireType);
            Assert.IsTrue(FireModeStats.IsMeleeAlt(knife, true));
            Assert.IsFalse(FireModeStats.IsMeleeAlt(knife, false));
            // A melee weapon must not be mistaken for a shotgun-burst alt.
            Assert.IsFalse(FireModeStats.IsShotgunAlt(knife, true));
        }

        [Test]
        public void PrimaryAndAltUseTheirOwnAbsoluteRates()
        {
            WeaponData knife = MakeKnife();
            Assert.AreEqual(1f / 1.6f, FireModeStats.IntervalSeconds(knife, false, ads: false), 1e-5f);
            Assert.AreEqual(1f / 1.1f, FireModeStats.IntervalSeconds(knife, true, ads: false), 1e-5f);
        }

        [Test]
        public void AltFireRateIsNotAnAdsMultiplier()
        {
            WeaponData knife = MakeKnife();
            // A melee weapon has no ADS, so the ads flag must not touch cadence.
            Assert.AreEqual(FireModeStats.IntervalSeconds(knife, false, ads: false),
                FireModeStats.IntervalSeconds(knife, false, ads: true), 1e-6f);
        }

        [Test]
        public void SlowAltSwingLocksOutTheFasterPrimary()
        {
            WeaponData knife = MakeKnife();
            float primary = FireModeStats.IntervalSeconds(knife, false, false);
            float alt = FireModeStats.IntervalSeconds(knife, true, false);

            // Having just swung the slow alt, the next primary still waits the
            // alt's full gap — interleaving can't beat the better single mode.
            Assert.AreEqual(alt, FireModeStats.RequiredGapSeconds(alt, primary), 1e-6f);
            // And a fast primary followed by an alt waits the alt's gap too.
            Assert.AreEqual(alt, FireModeStats.RequiredGapSeconds(primary, alt), 1e-6f);
        }

        [Test]
        public void MeleeThrowsExactlyOnePellet()
        {
            WeaponData knife = MakeKnife();
            Assert.AreEqual(1, FireModeStats.MaxPelletCount(knife, false));
            Assert.AreEqual(1, FireModeStats.MaxPelletCount(knife, true));
        }

        #endregion
    }
}
