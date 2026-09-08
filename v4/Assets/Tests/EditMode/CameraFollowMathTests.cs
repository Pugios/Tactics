using NUnit.Framework;
using UnityEngine;
using Tactics.Camera;

namespace Tactics.Tests.EditMode
{
    public class CameraFollowMathTests
    {
        private const float BaseWeight = 0.4f;
        private const float AdsMaxWeight = 0.85f;
        private const float Smoothness = 0.125f;
        private const float Dt = 1f / 60f;
        private const float Height = 12f;

        // --- TargetLookWeight ---

        [Test]
        public void TargetLookWeight_MapsZoomToBiasFraction()
        {
            // Bias fraction 1 - 1/zoom: 0 at no zoom, 0.6 / 0.8 at Operator levels.
            Assert.AreEqual(BaseWeight, CameraFollowMath.TargetLookWeight(BaseWeight, AdsMaxWeight, 1f));
            Assert.AreEqual(Mathf.Lerp(BaseWeight, AdsMaxWeight, 0.2f),
                CameraFollowMath.TargetLookWeight(BaseWeight, AdsMaxWeight, 1.25f), 1e-5f); // Vandal
            Assert.AreEqual(Mathf.Lerp(BaseWeight, AdsMaxWeight, 0.6f),
                CameraFollowMath.TargetLookWeight(BaseWeight, AdsMaxWeight, 2.5f), 1e-5f);  // Operator L1
            Assert.AreEqual(Mathf.Lerp(BaseWeight, AdsMaxWeight, 0.8f),
                CameraFollowMath.TargetLookWeight(BaseWeight, AdsMaxWeight, 5f), 1e-5f);    // Operator L2
        }

        [Test]
        public void TargetLookWeight_SubUnityZoom_ClampsToBase()
        {
            Assert.AreEqual(BaseWeight, CameraFollowMath.TargetLookWeight(BaseWeight, AdsMaxWeight, 0.5f));
            Assert.AreEqual(BaseWeight, CameraFollowMath.TargetLookWeight(BaseWeight, AdsMaxWeight, 0f));
        }

        // --- Step determinism ---

        [Test]
        public void Step_IdenticalInputs_AreBitIdentical()
        {
            // The shadow camera relies on this: equal state + equal inputs must
            // give EXACTLY equal outputs, so "no ADS" means an exact zero delta.
            Vector3 posA = new Vector3(3f, Height, -2f), posB = posA;
            Vector3 velA = Vector3.zero, velB = Vector3.zero;

            for (int i = 0; i < 100; i++)
            {
                Vector3 player = new Vector3(Mathf.Sin(i * 0.1f) * 4f, 0f, i * 0.05f);
                Vector3 mouse = player + new Vector3(5f, 0f, 3f + Mathf.Cos(i * 0.07f));

                posA = CameraFollowMath.Step(posA, ref velA, player, mouse, BaseWeight, Height, Smoothness, Dt);
                posB = CameraFollowMath.Step(posB, ref velB, player, mouse, BaseWeight, Height, Smoothness, Dt);

                Assert.AreEqual(posA, posB);
                Assert.AreEqual(velA, velB);
            }
        }

        [Test]
        public void Step_SharedHeightChanges_ProduceZeroYBias()
        {
            // Scroll zoom changes lockedHeight for both cameras. Different look
            // weights must never leak into Y — SmoothDamp with maxSpeed Infinity
            // is per-component, so equal Y state + equal height input stays
            // bit-identical even while XZ diverges.
            Vector3 actual = new Vector3(0f, 12f, 0f), shadow = actual;
            Vector3 actualVel = Vector3.zero, shadowVel = Vector3.zero;
            Vector3 player = Vector3.zero;
            Vector3 mouse = new Vector3(8f, 0f, 6f);

            for (int i = 0; i < 60; i++)
            {
                float height = Mathf.Lerp(12f, 30f, i / 59f);
                actual = CameraFollowMath.Step(actual, ref actualVel, player, mouse, AdsMaxWeight, height, Smoothness, Dt);
                shadow = CameraFollowMath.Step(shadow, ref shadowVel, player, mouse, BaseWeight, height, Smoothness, Dt);

                Assert.AreEqual(shadow.y, actual.y);
                Assert.AreNotEqual(shadow.x, actual.x); // sanity: XZ really diverges
            }
        }

        [Test]
        public void Step_ZeroDeltaTime_ChangesNothing()
        {
            Vector3 pos = new Vector3(1f, Height, 2f);
            Vector3 vel = new Vector3(0.5f, 0f, -0.25f);
            Vector3 velBefore = vel;

            Vector3 result = CameraFollowMath.Step(pos, ref vel, Vector3.zero,
                new Vector3(5f, 0f, 5f), BaseWeight, Height, Smoothness, 0f);

            Assert.AreEqual(pos, result);
            Assert.AreEqual(velBefore, vel);
        }

        // --- Closed-loop compensation invariance ---
        //
        // Sim A (reference): one camera at base weight; the cursor is fixed on
        // screen, so the aim point moves with the camera: aim += cameraDelta.
        // Sim B (compensated): the actual camera's weight ramps toward ADS while
        // a shadow stays at base weight; the warp pins the aim to move only by
        // the shadow's motion: aim = CompensatedAimPoint(aim, shadowDelta).
        // Both cameras in each sim are fed their sim's current aim as the mouse
        // world position (closed loop, as in TopDownCamera.LateUpdate).
        //
        // Invariant: aim-B == aim-A and shadow-B == camera-A — the compensated
        // biased system is indistinguishable from the no-bias one. Compared with
        // a tight tolerance rather than bit-exactly: the two sims sum the same
        // deltas in a different order (aim + (camDelta + user) vs
        // (aim + shadowDelta) + user), which differs by an ulp once a user
        // delta is present.
        private const float ClosedLoopTolerance = 1e-4f;

        private static void AssertApproximately(Vector3 expected, Vector3 actual, string message)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(ClosedLoopTolerance),
                $"{message}\n  Expected: {expected:F6}\n  But was:  {actual:F6}");
        }

        private static void RunClosedLoop(System.Func<int, Vector3> userAimDelta)
        {
            Vector3 camA = new Vector3(2f, Height, 1f);
            Vector3 velA = Vector3.zero;
            Vector3 aimA = new Vector3(6f, 0f, 5f);

            Vector3 actualB = camA, shadowB = camA;
            Vector3 actualVelB = Vector3.zero, shadowVelB = Vector3.zero;
            Vector3 aimB = aimA;

            for (int i = 0; i < 120; i++)
            {
                Vector3 player = new Vector3(i * 0.03f, 0f, Mathf.Sin(i * 0.05f)); // strafing mid-ADS
                float weight = Mathf.Lerp(BaseWeight, AdsMaxWeight, Mathf.Clamp01(i / 30f));
                Vector3 user = userAimDelta(i);

                // Sim A: screen-fixed cursor -> aim rides the camera.
                Vector3 camAOld = camA;
                camA = CameraFollowMath.Step(camA, ref velA, player, aimA, BaseWeight, Height, Smoothness, Dt);
                aimA += (camA - camAOld) + user;

                // Sim B: warped cursor -> aim rides only the shadow.
                Vector3 shadowBOld = shadowB;
                shadowB = CameraFollowMath.Step(shadowB, ref shadowVelB, player, aimB, BaseWeight, Height, Smoothness, Dt);
                actualB = CameraFollowMath.Step(actualB, ref actualVelB, player, aimB, weight, Height, Smoothness, Dt);
                aimB = CameraFollowMath.CompensatedAimPoint(aimB, shadowB - shadowBOld) + user;

                AssertApproximately(aimA, aimB, $"aim diverged at frame {i}");
                AssertApproximately(camA, shadowB, $"shadow diverged from reference camera at frame {i}");
            }
        }

        [Test]
        public void CompensatedAim_ClosedLoop_MatchesNoBiasSystem()
        {
            RunClosedLoop(_ => Vector3.zero);
        }

        [Test]
        public void CompensatedAim_UserMouseMotion_PassesThroughUnchanged()
        {
            RunClosedLoop(i => new Vector3(Mathf.Cos(i * 0.2f) * 0.1f, 0f, 0.06f));
        }

        // --- MaxLookWeightForVisibility ---

        private const float HalfWidth = 16f;
        private const float HalfHeight = 9f;
        private const float Margin = 0.7f;

        [Test]
        public void MaxLookWeight_MouseAtPlayer_Unconstrained()
        {
            Vector3 p = new Vector3(3f, 0f, -2f);
            Assert.AreEqual(1f, CameraFollowMath.MaxLookWeightForVisibility(
                p, p, 0f, HalfWidth, HalfHeight, Margin));
        }

        [Test]
        public void MaxLookWeight_FarMouseOnOneAxis_IsAllowedExtentOverDistance()
        {
            // Player 40 m east of the mouse at zero yaw -> only the
            // screen-horizontal extent constrains.
            float w = CameraFollowMath.MaxLookWeightForVisibility(
                new Vector3(40f, 0f, 0f), Vector3.zero, 0f, HalfWidth, HalfHeight, Margin);
            Assert.AreEqual((HalfWidth - Margin) / 40f, w, 1e-5f);
        }

        [Test]
        public void MaxLookWeight_MarginTightensTheCap()
        {
            Vector3 player = new Vector3(0f, 0f, 20f);
            float loose = CameraFollowMath.MaxLookWeightForVisibility(
                player, Vector3.zero, 0f, HalfWidth, HalfHeight, 0f);
            float tight = CameraFollowMath.MaxLookWeightForVisibility(
                player, Vector3.zero, 0f, HalfWidth, HalfHeight, 2f);
            Assert.AreEqual(HalfHeight / 20f, loose, 1e-5f);
            Assert.AreEqual((HalfHeight - 2f) / 20f, tight, 1e-5f);
        }

        [Test]
        public void MaxLookWeight_YawRotatesTheConstraintAxes()
        {
            // At 90 degrees yaw a world-X offset lands on the screen's vertical
            // axis, so the (smaller) halfHeight extent governs instead.
            Vector3 player = new Vector3(30f, 0f, 0f);
            float w0 = CameraFollowMath.MaxLookWeightForVisibility(
                player, Vector3.zero, 0f, HalfWidth, HalfHeight, Margin);
            float w90 = CameraFollowMath.MaxLookWeightForVisibility(
                player, Vector3.zero, 90f, HalfWidth, HalfHeight, Margin);
            Assert.AreEqual((HalfWidth - Margin) / 30f, w0, 1e-5f);
            Assert.AreEqual((HalfHeight - Margin) / 30f, w90, 1e-5f);
        }

        [Test]
        public void ClampedSteadyState_KeepsPlayerInsideExtents_UnclampedDoesNot()
        {
            // TopDownCamera applies min(smoothed ADS weight, max(maxW, base)).
            // Converged against a far cursor, the clamped camera must hold the
            // player at exactly the allowed extent while the unclamped ADS
            // weight pushes it off-screen (the bug this guards against).
            Vector3 player = Vector3.zero;
            Vector3 mouse = new Vector3(24f, 0f, 0f);
            Vector3 clamped = new Vector3(0f, Height, 0f), free = clamped;
            Vector3 clampedVel = Vector3.zero, freeVel = Vector3.zero;

            for (int i = 0; i < 600; i++)
            {
                float maxW = CameraFollowMath.MaxLookWeightForVisibility(
                    player, mouse, 0f, HalfWidth, HalfHeight, Margin);
                float w = Mathf.Min(AdsMaxWeight, Mathf.Max(maxW, BaseWeight));
                clamped = CameraFollowMath.Step(clamped, ref clampedVel, player, mouse, w, Height, Smoothness, Dt);
                free = CameraFollowMath.Step(free, ref freeVel, player, mouse, AdsMaxWeight, Height, Smoothness, Dt);
            }

            Assert.LessOrEqual(Mathf.Abs(player.x - clamped.x), HalfWidth - Margin + 1e-3f);
            Assert.Greater(Mathf.Abs(player.x - free.x), HalfWidth);
        }
    }
}
