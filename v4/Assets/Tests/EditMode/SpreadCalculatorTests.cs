using NUnit.Framework;
using UnityEngine;
using Tactics.Player;
using Tactics.Weapons;

namespace Tactics.Tests.EditMode
{
    public class SpreadCalculatorTests
    {
        private const int Seed = 12345;

        /// <summary>Vandal-shaped weapon (the WeaponData field defaults) built fresh per test.</summary>
        private static WeaponData MakeRifle()
        {
            return ScriptableObject.CreateInstance<WeaponData>();
        }

        /// <summary>Spread zeroed out so only the deterministic recoil T remains.</summary>
        private static WeaponData MakeRecoilOnlyRifle()
        {
            WeaponData weapon = MakeRifle();
            weapon.firstShotSpreadStanding = 0f;
            weapon.firstShotSpreadCrouched = 0f;
            weapon.maxSpreadStanding = 0f;
            weapon.maxSpreadCrouched = 0f;
            weapon.spreadPerShotDegrees = 0f;
            weapon.movePenaltyCrouchWalk = 0f;
            weapon.movePenaltyWalk = 0f;
            weapon.movePenaltyRun = 0f;
            weapon.movePenaltyAirborne = 0f;
            return weapon;
        }

        // --- Determinism ---

        [Test]
        public void ComputeShotOffset_SameInputs_IsIdentical()
        {
            WeaponData weapon = MakeRifle();

            Vector2 a = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, MovementState.Walking, Seed, 17);
            Vector2 b = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, MovementState.Walking, Seed, 17);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void ComputeShotOffset_DifferentShotNumbers_Differ()
        {
            WeaponData weapon = MakeRifle();

            Vector2 a = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, MovementState.Stationary, Seed, 17);
            Vector2 b = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, MovementState.Stationary, Seed, 18);

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void HashShot_ProducesUniformRangeValues()
        {
            for (int shot = 0; shot < 1000; shot++)
            {
                SpreadCalculator.HashShot(Seed, shot, out float u1, out float u2);
                Assert.That(u1, Is.InRange(0f, 1f));
                Assert.That(u2, Is.InRange(0f, 1f));
            }
        }

        // --- Spread cone ---

        [Test]
        public void FirstShot_StandingStill_StaysInsideFirstShotCone()
        {
            WeaponData weapon = MakeRifle();

            for (int shot = 0; shot < 100; shot++)
            {
                Vector2 offset = SpreadCalculator.ComputeShotOffsetDegrees(
                    weapon, 0f, false, MovementState.Stationary, Seed, shot);
                Assert.LessOrEqual(offset.magnitude, weapon.firstShotSpreadStanding + 0.0001f);
            }
        }

        [Test]
        public void SpreadDegrees_GrowsWithSprayIndex_CappedAtMax()
        {
            WeaponData weapon = MakeRifle();

            float atStart = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, MovementState.Stationary);
            float midSpray = SpreadCalculator.ComputeSpreadDegrees(weapon, 4f, false, MovementState.Stationary);
            float longSpray = SpreadCalculator.ComputeSpreadDegrees(weapon, 100f, false, MovementState.Stationary);

            Assert.Less(atStart, midSpray);
            Assert.Less(midSpray, longSpray);
            Assert.AreEqual(weapon.maxSpreadStanding, longSpray);
        }

        [Test]
        public void SpreadDegrees_CrouchedIsTighterThanStanding()
        {
            WeaponData weapon = MakeRifle();

            float standing = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, MovementState.Stationary);
            float crouched = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, MovementState.Stationary);

            Assert.Less(crouched, standing);
        }

        [Test]
        public void SpreadDegrees_MovementPenaltiesEscalate()
        {
            WeaponData weapon = MakeRifle();

            float stationary = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, MovementState.Stationary);
            float crouchWalk = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, MovementState.CrouchWalking);
            float walking = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, MovementState.Walking);
            float running = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, MovementState.Running);
            float airborne = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, MovementState.Airborne);

            Assert.Less(stationary, crouchWalk);
            Assert.Less(crouchWalk, walking);
            Assert.Less(walking, running);
            Assert.Less(running, airborne);
        }

        // --- Recoil T ---

        [Test]
        public void Recoil_FirstShot_HasNoKick()
        {
            Vector2 recoil = SpreadCalculator.ComputeRecoilDegrees(MakeRifle(), 0f);

            Assert.AreEqual(Vector2.zero, recoil);
        }

        [Test]
        public void Recoil_ClimbIsBackwardOnly_GentleThenSteep()
        {
            WeaponData weapon = MakeRifle();

            Vector2 early = SpreadCalculator.ComputeRecoilDegrees(weapon, 3f);
            Vector2 top = SpreadCalculator.ComputeRecoilDegrees(weapon, weapon.recoilClimbShots);

            // During the climb the T has no horizontal component.
            Assert.AreEqual(0f, early.x);
            Assert.AreEqual(0f, top.x);

            // Shot 3 has kicked less than half of the total climb; the top has all of it.
            Assert.Greater(early.y, 0f);
            Assert.Less(early.y, weapon.recoilClimbDegrees * 0.5f);
            Assert.AreEqual(weapon.recoilClimbDegrees, top.y, 0.001f);
        }

        [Test]
        public void Recoil_SwayGoesLeftFirstThenRight()
        {
            WeaponData weapon = MakeRifle(); // climb tops at 7, sway period 5

            Vector2 leftPhase = SpreadCalculator.ComputeRecoilDegrees(weapon, 8.25f);
            Vector2 rightPhase = SpreadCalculator.ComputeRecoilDegrees(weapon, 10.75f);

            Assert.Less(leftPhase.x, 0f);
            Assert.Greater(rightPhase.x, 0f);
            // The backward climb holds at the top while swaying.
            Assert.AreEqual(weapon.recoilClimbDegrees, leftPhase.y, 0.001f);
        }

        [Test]
        public void RecoilOnly_ShotOffset_LandsExactlyOnTheT()
        {
            WeaponData weapon = MakeRecoilOnlyRifle();

            Vector2 offset = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 5f, false, MovementState.Running, Seed, 3);
            Vector2 recoil = SpreadCalculator.ComputeRecoilDegrees(weapon, 5f);

            Assert.AreEqual(recoil, offset);
        }

        // --- Spray decay ---

        [Test]
        public void Decay_WithinGracePeriod_KeepsIndex()
        {
            Assert.AreEqual(6f, SpreadCalculator.DecaySprayIndex(6f, 0.1f, decayDelay: 0.15f, decayPerSecond: 15f));
        }

        [Test]
        public void Decay_ShortPause_PartiallyRecovers()
        {
            float decayed = SpreadCalculator.DecaySprayIndex(6f, 0.35f, decayDelay: 0.15f, decayPerSecond: 15f);

            Assert.AreEqual(3f, decayed, 0.001f);
        }

        [Test]
        public void Decay_LongPause_FullyResets_NeverNegative()
        {
            Assert.AreEqual(0f, SpreadCalculator.DecaySprayIndex(6f, 10f, decayDelay: 0.15f, decayPerSecond: 15f));
            Assert.AreEqual(0f, SpreadCalculator.DecaySprayIndex(0f, float.PositiveInfinity, 0.15f, 15f));
        }

        // --- World-space projection ---

        [Test]
        public void ApplyOffset_BackwardDegrees_LandBeyondAimPoint()
        {
            Vector3 shooter = Vector3.zero;
            Vector3 aim = new Vector3(0f, 0f, 10f);

            Vector3 result = SpreadCalculator.ApplyOffsetToAimPoint(shooter, aim, new Vector2(0f, 4f));

            Assert.Greater(result.z, aim.z);
            Assert.AreEqual(0f, result.x, 0.0001f);
        }

        [Test]
        public void ApplyOffset_PositiveSideDegrees_LandToShootersRight()
        {
            Vector3 shooter = Vector3.zero;
            Vector3 aim = new Vector3(0f, 0f, 10f); // facing +Z, so right is +X

            Vector3 result = SpreadCalculator.ApplyOffsetToAimPoint(shooter, aim, new Vector2(2f, 0f));

            Assert.Greater(result.x, 0f);
            Assert.AreEqual(10f, result.z, 0.0001f);
        }

        [Test]
        public void ApplyOffset_ScalesLinearlyWithDistance()
        {
            Vector3 shooter = Vector3.zero;
            Vector2 offsetDegrees = new Vector2(1f, 2f);

            Vector3 near = SpreadCalculator.ApplyOffsetToAimPoint(shooter, new Vector3(0f, 0f, 10f), offsetDegrees);
            Vector3 far = SpreadCalculator.ApplyOffsetToAimPoint(shooter, new Vector3(0f, 0f, 20f), offsetDegrees);

            float nearMiss = Vector3.Distance(near, new Vector3(0f, 0f, 10f));
            float farMiss = Vector3.Distance(far, new Vector3(0f, 0f, 20f));

            Assert.AreEqual(nearMiss * 2f, farMiss, 0.001f);
        }

        [Test]
        public void ApplyOffset_AimAtOwnFeet_IsUnchanged()
        {
            Vector3 shooter = new Vector3(3f, 1f, 3f);
            Vector3 aim = new Vector3(3f, 0f, 3f);

            Assert.AreEqual(aim, SpreadCalculator.ApplyOffsetToAimPoint(shooter, aim, new Vector2(5f, 5f)));
        }
    }

    public class MovementClassifierTests
    {
        private const float RunSpeed = 5.4f;
        private const float WalkMultiplier = 0.5f;

        private static MovementState Classify(bool grounded, bool crouching, float speed) =>
            MovementClassifier.Classify(grounded, crouching, speed, RunSpeed, WalkMultiplier);

        [Test]
        public void Airborne_TrumpsEverything()
        {
            Assert.AreEqual(MovementState.Airborne, Classify(grounded: false, crouching: true, speed: 0f));
        }

        [Test]
        public void GroundedStill_IsStationary_EvenCrouched()
        {
            Assert.AreEqual(MovementState.Stationary, Classify(grounded: true, crouching: false, speed: 0f));
            Assert.AreEqual(MovementState.Stationary, Classify(grounded: true, crouching: true, speed: 0f));
        }

        [Test]
        public void CrouchMoving_IsCrouchWalking()
        {
            Assert.AreEqual(MovementState.CrouchWalking, Classify(grounded: true, crouching: true, speed: RunSpeed * 0.3f));
        }

        [Test]
        public void WalkSpeed_IsWalking_RunSpeed_IsRunning()
        {
            Assert.AreEqual(MovementState.Walking, Classify(grounded: true, crouching: false, speed: RunSpeed * WalkMultiplier));
            Assert.AreEqual(MovementState.Running, Classify(grounded: true, crouching: false, speed: RunSpeed));
        }
    }
}
