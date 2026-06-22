using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Pure visibility evaluation: 3D cone test, line-of-sight, and enemy capsule sampling.
    /// </summary>
    public static class VisionEvaluator
    {
        private static readonly List<Vector3> CapsuleSampleBuffer = new List<Vector3>(16);

        public static bool IsInCone(Vector3 origin, Vector3 forward, Vector3 target, float halfAngleDegrees)
        {
            Vector3 offset = target - origin;
            if (offset.sqrMagnitude < 0.0001f)
                return true;

            Vector3 direction = offset.normalized;
            Vector3 coneForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            return Vector3.Angle(coneForward, direction) <= halfAngleDegrees;
        }

        /// <summary>
        /// True when the target point is inside the cone and not blocked by losMask geometry.
        /// </summary>
        public static bool IsPointVisible(Vector3 origin, Vector3 forward, Vector3 target, in VisionConfig config)
        {
            if (!IsInCone(origin, forward, target, config.HorizontalHalfAngle))
                return false;

            return HasLineOfSight(origin, target, config.LosMask, config.LosSkinWidth);
        }

        /// <summary>
        /// True when any sampled point on the enemy capsule is visible. One visible sample reveals the whole enemy.
        /// </summary>
        public static bool IsEnemyVisibleAt(
            Vector3 origin,
            Vector3 forward,
            Vector3 feetPosition,
            in VisionConfig config,
            float heightOverride = -1f,
            float radiusOverride = -1f)
        {
            float height = heightOverride > 0f ? heightOverride : config.EnemyHeight;
            float radius = radiusOverride > 0f ? radiusOverride : config.EnemyRadius;

            if (!IsEnemyRoughlyInCone(origin, forward, feetPosition, height, radius, config))
                return false;

            CollectCapsuleSamplePoints(feetPosition, radius, height, forward, CapsuleSampleBuffer);

            for (int i = 0; i < CapsuleSampleBuffer.Count; i++)
            {
                if (IsPointVisible(origin, forward, CapsuleSampleBuffer[i], config))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Unobstructed line from origin to target on losMask layers only.
        /// </summary>
        public static bool HasLineOfSight(Vector3 origin, Vector3 target, LayerMask losMask, float skinWidth = 0.05f)
        {
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= skinWidth)
                return true;

            Vector3 direction = delta / distance;
            float rayDistance = distance - skinWidth;

            return !Physics.Raycast(
                origin,
                direction,
                rayDistance,
                losMask,
                QueryTriggerInteraction.Ignore);
        }

        public static void GetConeBasis(Vector3 forward, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            else
                right.Normalize();

            up = Vector3.Cross(forward, right).normalized;
        }

        public static Vector3 GetConeDirection(
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float azimuthDegrees,
            float elevationDegrees,
            float halfAngleDegrees)
        {
            float azimuthRad = azimuthDegrees * Mathf.Deg2Rad;
            float elevationRad = elevationDegrees * Mathf.Deg2Rad;
            float halfAngleRad = halfAngleDegrees * Mathf.Deg2Rad;

            float cosHalf = Mathf.Cos(halfAngleRad);
            float sinHalf = Mathf.Sin(halfAngleRad);
            float cosElev = Mathf.Cos(elevationRad);
            float sinElev = Mathf.Sin(elevationRad);

            Vector3 tangent = right * (Mathf.Sin(azimuthRad) * cosElev) + up * sinElev;
            return (forward * cosHalf + tangent * sinHalf).normalized;
        }

        /// <summary>
        /// Direction for an oblong cone: horizontal and vertical offsets are applied independently.
        /// </summary>
        public static Vector3 GetOblongConeDirection(
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float azimuthDegrees,
            float elevationDegrees)
        {
            Vector3 yawedForward = Quaternion.AngleAxis(azimuthDegrees, up) * forward;
            Vector3 pitchAxis = Vector3.Cross(up, yawedForward);
            if (pitchAxis.sqrMagnitude < 0.001f)
                pitchAxis = right;

            pitchAxis.Normalize();
            return (Quaternion.AngleAxis(elevationDegrees, pitchAxis) * yawedForward).normalized;
        }

        private static bool IsEnemyRoughlyInCone(
            Vector3 origin,
            Vector3 forward,
            Vector3 feetPosition,
            float height,
            float radius,
            in VisionConfig config)
        {
            Vector3 capsuleCenter = feetPosition + Vector3.up * (height * 0.5f);
            Vector3 toCenter = capsuleCenter - origin;
            float centerDistance = toCenter.magnitude;

            float expandedHalfAngle = config.HorizontalHalfAngle;
            if (centerDistance > 0.01f)
                expandedHalfAngle += Mathf.Asin(Mathf.Clamp(radius / centerDistance, 0f, 1f)) * Mathf.Rad2Deg;

            return IsInCone(origin, forward, capsuleCenter, expandedHalfAngle);
        }

        private static void CollectCapsuleSamplePoints(
            Vector3 feet,
            float radius,
            float height,
            Vector3 viewForward,
            List<Vector3> points)
        {
            points.Clear();

            Vector3 flatForward = viewForward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();

            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;

            float yBottom = feet.y + radius;
            float yTop = feet.y + height - radius;
            float yMid = (yBottom + yTop) * 0.5f;
            float yUpper = Mathf.Lerp(yMid, yTop, 0.66f);
            float yLower = Mathf.Lerp(yBottom, yMid, 0.66f);

            AddCapsuleColumn(points, feet.x, feet.z, yBottom, yMid, yTop);
            AddViewFacingRing(points, feet.x, feet.z, yTop, flatForward, flatRight, radius);
            AddViewFacingRing(points, feet.x, feet.z, yUpper, flatForward, flatRight, radius);
            AddViewFacingRing(points, feet.x, feet.z, yMid, flatForward, flatRight, radius);
            AddViewFacingRing(points, feet.x, feet.z, yLower, flatForward, flatRight, radius);
        }

        private static void AddCapsuleColumn(List<Vector3> points, float x, float z, float yBottom, float yMid, float yTop)
        {
            points.Add(new Vector3(x, yTop, z));
            points.Add(new Vector3(x, yMid, z));
            points.Add(new Vector3(x, yBottom, z));
        }

        private static void AddViewFacingRing(
            List<Vector3> points,
            float x,
            float z,
            float y,
            Vector3 forward,
            Vector3 right,
            float radius)
        {
            Vector3 center = new Vector3(x, y, z);
            points.Add(center + forward * radius);
            points.Add(center + (forward + right).normalized * radius);
            points.Add(center + (forward - right).normalized * radius);
        }
    }
}
