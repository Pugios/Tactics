using NUnit.Framework;
using UnityEngine;
using Tactics.Player;
using Tactics.Weapons;

namespace Tactics.Tests.EditMode
{
    public class SpreadCalculatorTests
    {
        private const int Seed = 12345;

        /// <summary>Vandal-shaped weapon (the WeaponData field defaults, plus the Vandal's ADS block) built fresh per test.</summary>
        private static WeaponData MakeRifle()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.altFireType = AltFireType.AimDownSight;
            weapon.adsMode = AdsMode.Hold;
            weapon.adsZoomLevels = new[] { 1.25f };
            weapon.adsMoveSpeedMultiplier = 0.76f;
            weapon.adsFireRateMultiplier = 0.9f;
            weapon.altFirstShotSpreadStanding = 0.157f;
            weapon.altFirstShotSpreadCrouched = 0.13f;
            weapon.altMaxSpreadStanding = 1.02f;
            weapon.altMaxSpreadCrouched = 0.87f;
            return weapon;
        }

        /// <summary>Operator-shaped weapon: toggle 2.5×/5× scope, huge flat hip spread, perfect ADS accuracy.</summary>
        private static WeaponData MakeOperator()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.firstShotSpreadStanding = 5f;
            weapon.firstShotSpreadCrouched = 4.5f;
            weapon.maxSpreadStanding = 5f;
            weapon.maxSpreadCrouched = 4.5f;
            weapon.spreadPerShotDegrees = 0f;
            weapon.movePenaltyCrouchWalk = 7.5f;
            weapon.movePenaltyWalk = 10f;
            weapon.movePenaltyRun = 15f;
            weapon.movePenaltyAirborne = 15f;
            weapon.altFireType = AltFireType.AimDownSight;
            weapon.adsMode = AdsMode.Toggle;
            weapon.adsZoomLevels = new[] { 2.5f, 5f };
            weapon.adsMoveSpeedMultiplier = 0.72f;
            weapon.adsFireRateMultiplier = 1f;
            weapon.altFirstShotSpreadStanding = 0f;
            weapon.altFirstShotSpreadCrouched = 0f;
            weapon.altMaxSpreadStanding = 0f;
            weapon.altMaxSpreadCrouched = 0f;
            return weapon;
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

            Vector2 a = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, false, MovementState.Walking, Seed, 17);
            Vector2 b = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, false, MovementState.Walking, Seed, 17);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void ComputeShotOffset_DifferentShotNumbers_Differ()
        {
            WeaponData weapon = MakeRifle();

            Vector2 a = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, false, MovementState.Stationary, Seed, 17);
            Vector2 b = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 4.2f, false, false, MovementState.Stationary, Seed, 18);

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
                    weapon, 0f, false, false, MovementState.Stationary, Seed, shot);
                Assert.LessOrEqual(offset.magnitude, weapon.firstShotSpreadStanding + 0.0001f);
            }
        }

        [Test]
        public void SpreadDegrees_GrowsWithSprayIndex_CappedAtMax()
        {
            WeaponData weapon = MakeRifle();

            float atStart = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary);
            float midSpray = SpreadCalculator.ComputeSpreadDegrees(weapon, 4f, false, false, MovementState.Stationary);
            float longSpray = SpreadCalculator.ComputeSpreadDegrees(weapon, 100f, false, false, MovementState.Stationary);

            Assert.Less(atStart, midSpray);
            Assert.Less(midSpray, longSpray);
            Assert.AreEqual(weapon.maxSpreadStanding, longSpray);
        }

        [Test]
        public void SpreadDegrees_CrouchedIsTighterThanStanding()
        {
            WeaponData weapon = MakeRifle();

            float standing = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary);
            float crouched = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, false, MovementState.Stationary);

            Assert.Less(crouched, standing);
        }

        [Test]
        public void SpreadDegrees_MovementPenaltiesEscalate()
        {
            WeaponData weapon = MakeRifle();

            float stationary = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary);
            float crouchWalk = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, false, MovementState.CrouchWalking);
            float walking = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Walking);
            float running = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Running);
            float airborne = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Airborne);

            Assert.Less(stationary, crouchWalk);
            Assert.Less(crouchWalk, walking);
            Assert.Less(walking, running);
            Assert.Less(running, airborne);
        }

        // --- ADS (alt-fire) spread column ---

        [Test]
        public void Ads_FirstShot_UsesAdsColumn_TighterThanHipFire()
        {
            WeaponData weapon = MakeRifle();

            float hipStanding = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary);
            float adsStanding = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Stationary);
            float adsCrouched = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, true, MovementState.Stationary);

            Assert.AreEqual(0.157f, adsStanding, 0.0001f);
            Assert.AreEqual(0.13f, adsCrouched, 0.0001f);
            Assert.Less(adsStanding, hipStanding);
        }

        [Test]
        public void Ads_LongSpray_CapsAtAdsMaxSpread()
        {
            WeaponData weapon = MakeRifle();

            float standing = SpreadCalculator.ComputeSpreadDegrees(weapon, 100f, false, true, MovementState.Stationary);
            float crouched = SpreadCalculator.ComputeSpreadDegrees(weapon, 100f, true, true, MovementState.Stationary);

            Assert.AreEqual(1.02f, standing, 0.0001f);
            Assert.AreEqual(0.87f, crouched, 0.0001f);
        }

        [Test]
        public void Ads_OnWeaponWithoutAdsAltFire_IsIgnored()
        {
            WeaponData weapon = MakeRifle();
            weapon.altFireType = AltFireType.None;

            float hip = SpreadCalculator.ComputeSpreadDegrees(weapon, 3f, false, false, MovementState.Walking);
            float claimedAds = SpreadCalculator.ComputeSpreadDegrees(weapon, 3f, false, true, MovementState.Walking);

            Assert.AreEqual(hip, claimedAds);
        }

        [Test]
        public void Ads_MovementPenalty_StacksSameAsHipFire()
        {
            WeaponData weapon = MakeRifle();

            float stationary = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Stationary);
            float running = SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Running);

            Assert.AreEqual(stationary + weapon.movePenaltyRun, running, 0.0001f);
        }

        // --- Operator (sniper) profile ---

        [Test]
        public void Operator_HipFire_SpreadConstantAcrossSpray()
        {
            WeaponData weapon = MakeOperator();

            Assert.AreEqual(5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 25f, false, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(4.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, false, MovementState.Stationary), 0.0001f);
            Assert.AreEqual(4.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 25f, true, false, MovementState.Stationary), 0.0001f);
        }

        [Test]
        public void Operator_Scoped_Stationary_IsPerfectlyAccurate()
        {
            WeaponData weapon = MakeOperator();

            Assert.AreEqual(0f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Stationary));
            Assert.AreEqual(0f, SpreadCalculator.ComputeSpreadDegrees(weapon, 25f, true, true, MovementState.Stationary));
        }

        [Test]
        public void Operator_Scoped_SharesPrimaryMovementPenalties()
        {
            WeaponData weapon = MakeOperator();

            Assert.AreEqual(7.5f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, true, true, MovementState.CrouchWalking), 0.0001f);
            Assert.AreEqual(10f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Walking), 0.0001f);
            Assert.AreEqual(15f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Running), 0.0001f);
            Assert.AreEqual(15f, SpreadCalculator.ComputeSpreadDegrees(weapon, 0f, false, true, MovementState.Airborne), 0.0001f);
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

            Vector2 offset = SpreadCalculator.ComputeShotOffsetDegrees(weapon, 5f, false, false, MovementState.Running, Seed, 3);
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

    public class AdsZoomLogicTests
    {
        private static WeaponData MakeToggleSniper()
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.altFireType = AltFireType.AimDownSight;
            weapon.adsMode = AdsMode.Toggle;
            weapon.adsZoomLevels = new[] { 2.5f, 5f };
            return weapon;
        }

        [Test]
        public void LevelCount_RequiresAdsTypeAndLevels()
        {
            Assert.AreEqual(2, AdsZoomLogic.LevelCount(MakeToggleSniper()));
            Assert.AreEqual(0, AdsZoomLogic.LevelCount(null));

            WeaponData noAds = ScriptableObject.CreateInstance<WeaponData>();
            Assert.AreEqual(0, AdsZoomLogic.LevelCount(noAds)); // altFireType None

            WeaponData emptyLevels = MakeToggleSniper();
            emptyLevels.adsZoomLevels = new float[0];
            Assert.AreEqual(0, AdsZoomLogic.LevelCount(emptyLevels));
        }

        [Test]
        public void NextToggleLevel_CyclesThroughAllLevelsThenBackToZero()
        {
            Assert.AreEqual(1, AdsZoomLogic.NextToggleLevel(0, 2));
            Assert.AreEqual(2, AdsZoomLogic.NextToggleLevel(1, 2));
            Assert.AreEqual(0, AdsZoomLogic.NextToggleLevel(2, 2));
        }

        [Test]
        public void NextToggleLevel_NoLevels_StaysAtZero()
        {
            Assert.AreEqual(0, AdsZoomLogic.NextToggleLevel(0, 0));
            Assert.AreEqual(0, AdsZoomLogic.NextToggleLevel(3, 0));
        }

        [Test]
        public void ZoomForLevel_MapsLevelsAndClampsOutOfRange()
        {
            WeaponData weapon = MakeToggleSniper();

            Assert.AreEqual(1f, AdsZoomLogic.ZoomForLevel(weapon, 0));
            Assert.AreEqual(2.5f, AdsZoomLogic.ZoomForLevel(weapon, 1));
            Assert.AreEqual(5f, AdsZoomLogic.ZoomForLevel(weapon, 2));
            Assert.AreEqual(1f, AdsZoomLogic.ZoomForLevel(weapon, 3));
            Assert.AreEqual(1f, AdsZoomLogic.ZoomForLevel(null, 1));
        }
    }

    public class MovementClassifierTests
    {
        private const float RunSpeed = 5.4f;
        private const float AdsMultiplier = 0.76f;

        private static MovementState Classify(bool grounded, bool crouching, bool walking, float speed) =>
            MovementClassifier.Classify(grounded, crouching, walking, speed);

        [Test]
        public void Airborne_TrumpsEverything()
        {
            Assert.AreEqual(MovementState.Airborne, Classify(grounded: false, crouching: true, walking: true, speed: 0f));
        }

        [Test]
        public void GroundedStill_IsStationary_EvenCrouchedOrWalkHeld()
        {
            Assert.AreEqual(MovementState.Stationary, Classify(grounded: true, crouching: false, walking: false, speed: 0f));
            Assert.AreEqual(MovementState.Stationary, Classify(grounded: true, crouching: true, walking: false, speed: 0f));
            Assert.AreEqual(MovementState.Stationary, Classify(grounded: true, crouching: false, walking: true, speed: 0f));
        }

        [Test]
        public void CrouchInput_WinsOverWalkInput()
        {
            Assert.AreEqual(MovementState.CrouchWalking, Classify(grounded: true, crouching: true, walking: true, speed: RunSpeed * 0.3f));
        }

        [Test]
        public void WalkInput_IsWalking_NoStanceInput_IsRunning()
        {
            Assert.AreEqual(MovementState.Walking, Classify(grounded: true, crouching: false, walking: true, speed: RunSpeed * 0.5f));
            Assert.AreEqual(MovementState.Running, Classify(grounded: true, crouching: false, walking: false, speed: RunSpeed));
        }

        [Test]
        public void ClassificationFollowsInputs_NotSpeed()
        {
            // ADS (or any future slow) reduces speed but not the player's chosen
            // movement: full-input ADS movement is still Running, and ADS+walk is
            // still Walking even though its speed is below the old walk band.
            Assert.AreEqual(MovementState.Running,
                Classify(grounded: true, crouching: false, walking: false, speed: RunSpeed * AdsMultiplier));
            Assert.AreEqual(MovementState.Walking,
                Classify(grounded: true, crouching: false, walking: true, speed: RunSpeed * 0.5f * AdsMultiplier));
        }
    }
}
