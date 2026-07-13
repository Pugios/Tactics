using NUnit.Framework;
using UnityEngine;
using Tactics.Vision;

namespace Tactics.Tests.EditMode
{
    public class VisionEvaluatorTests
    {
        private const float HorizontalViewAngle = 103f;
        private const float VerticalViewAngle = 70.53f;

        private static VisionConfig DefaultConfig => new VisionConfig(
            HorizontalViewAngle,
            VerticalViewAngle,
            maxViewDistance: 500f,
            enemyHeight: 2f,
            enemyRadius: 0.5f,
            losMask: VisionLayerMasks.DefaultLos,
            losSkinWidth: 0.05f);

        [Test]
        public void IsInCone_PointOnForwardAxis_IsVisible()
        {
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;

            Assert.IsTrue(VisionEvaluator.IsInCone(origin, forward, new Vector3(0f, 0f, 5f), 51.5f));
        }

        [Test]
        public void IsInCone_PointBehindOrigin_IsNotVisible()
        {
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;

            Assert.IsFalse(VisionEvaluator.IsInCone(origin, forward, new Vector3(0f, 0f, -1f), 51.5f));
        }

        [Test]
        public void IsInCone_PointAtHalfAngle_IsVisible()
        {
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;
            float halfAngle = HorizontalViewAngle * 0.5f;

            Vector3 direction = Quaternion.Euler(0f, halfAngle, 0f) * forward;
            Vector3 target = origin + direction * 10f;

            Assert.IsTrue(VisionEvaluator.IsInCone(origin, forward, target, halfAngle));
        }

        [Test]
        public void IsInCone_PointOutsideHalfAngle_IsNotVisible()
        {
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;
            float halfAngle = HorizontalViewAngle * 0.5f;

            Vector3 direction = Quaternion.Euler(0f, halfAngle + 1f, 0f) * forward;
            Vector3 target = origin + direction * 10f;

            Assert.IsFalse(VisionEvaluator.IsInCone(origin, forward, target, halfAngle));
        }

        [Test]
        public void IsInCone_ElevatedTargetInsideCone_IsVisible()
        {
            Vector3 origin = new Vector3(0f, 1.5f, 0f);
            Vector3 forward = new Vector3(0f, 0.5f, 1f).normalized;
            Vector3 target = origin + forward * 8f;

            Assert.IsTrue(VisionEvaluator.IsInCone(origin, forward, target, 51.5f));
        }

        [Test]
        public void GetConeDirection_ForwardAtZeroAzimuthAndElevation_MatchesForward()
        {
            Vector3 forward = new Vector3(0.2f, 0.3f, 1f).normalized;
            VisionEvaluator.GetConeBasis(forward, out Vector3 right, out Vector3 up);

            Vector3 direction = VisionEvaluator.GetConeDirection(forward, right, up, 0f, 0f, 51.5f);

            Assert.That(Vector3.Dot(direction, forward), Is.GreaterThan(0.99f));
        }

        [Test]
        public void GetOblongConeDirection_AtZeroOffset_MatchesForward()
        {
            Vector3 forward = new Vector3(0.2f, 0.3f, 1f).normalized;
            VisionEvaluator.GetConeBasis(forward, out Vector3 right, out Vector3 up);

            Vector3 direction = VisionEvaluator.GetOblongConeDirection(forward, right, up, 0f, 0f);

            Assert.That(Vector3.Dot(direction, forward), Is.GreaterThan(0.99f));
        }
    }
}
