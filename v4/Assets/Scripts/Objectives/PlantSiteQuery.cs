using UnityEngine;
using Tactics.Map;
using Tactics.Vision;

namespace Tactics.Objectives
{
    /// <summary>
    /// Whether the spike can be planted where a player stands, answered from the
    /// floor under their feet rather than from a trigger volume: the face they
    /// are standing on must be a surface flagged <c>isPlantSite</c>, which the
    /// map importer assigns from the Blender material painted on it.
    ///
    /// Painting the floor beats a trigger volume for the shape sites actually
    /// have. Unity only allows convex triggers, so a jagged site — stairs, a
    /// notched corner — needs either several volumes or one that bulges past the
    /// markings players can see. A painted floor is exactly as jagged as it
    /// looks, and what you see is what you can plant on.
    ///
    /// Jumping over a site is not planting: with no floor within reach the probe
    /// simply misses.
    /// </summary>
    public static class PlantSiteQuery
    {
        /// <summary>Started above the feet so a small step or slope can't start the ray inside the floor.</summary>
        public const float ProbeStartHeight = 0.5f;

        /// <summary>Reaches past the CharacterController's 0.3 m step offset.</summary>
        public const float ProbeLength = 1f;

        public static bool IsOnPlantSite(Vector3 feetPosition)
        {
            return IsOnPlantSite(feetPosition, out _);
        }

        public static bool IsOnPlantSite(Vector3 feetPosition, out SurfaceData surface)
        {
            surface = null;

            // Ground layer only: the same geometry the aim raycast treats as
            // standable, so a plantable face is always one you can be on.
            if (!Physics.Raycast(feetPosition + Vector3.up * ProbeStartHeight, Vector3.down,
                    out RaycastHit hit, ProbeLength, VisionLayerMasks.GroundOnly, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (!hit.collider.TryGetComponent(out SurfaceMap surfaces)) return false;

            surface = surfaces.SurfaceAt(hit.triangleIndex);
            return surface != null && surface.isPlantSite;
        }
    }
}
