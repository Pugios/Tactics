using UnityEngine;

namespace Tactics.Map
{
    /// <summary>
    /// Which surface every triangle of one collider is made of, written by the
    /// map importer. Surfaces are per FACE, not per object, because a single
    /// Blender object can carry several materials — a concrete building with a
    /// dirt floor on top is one mesh with two material slots.
    ///
    /// Read at runtime through <c>RaycastHit.triangleIndex</c>, which indexes the
    /// collider mesh's triangles in the order they were built, so tag i belongs
    /// to triangle i. A single-surface collider stores no per-triangle array at
    /// all and answers from <see cref="palette"/>[0].
    /// </summary>
    [DisallowMultipleComponent]
    public class SurfaceMap : MonoBehaviour
    {
        [Tooltip("Surfaces used by this collider; index 0 is the fallback.")]
        public SurfaceData[] palette;

        [Tooltip("One palette index per collider triangle. Empty = all palette[0].")]
        public byte[] triangleSurface;

        /// <summary>Surface of the given collider triangle, or null when unset.</summary>
        public SurfaceData SurfaceAt(int triangleIndex)
        {
            if (palette == null || palette.Length == 0) return null;
            if (triangleSurface == null || triangleSurface.Length == 0) return palette[0];
            // An out-of-range index would mean the collider and this map drifted
            // apart; answering with the fallback beats throwing mid-shot.
            if (triangleIndex < 0 || triangleIndex >= triangleSurface.Length) return palette[0];

            int index = triangleSurface[triangleIndex];
            return index < palette.Length ? palette[index] : palette[0];
        }

        public float MaxTravelMeters(int triangleIndex, float fallback)
        {
            SurfaceData surface = SurfaceAt(triangleIndex);
            return surface != null ? surface.maxTravelMeters : fallback;
        }

        public string SurfaceName(int triangleIndex)
        {
            SurfaceData surface = SurfaceAt(triangleIndex);
            return surface != null ? surface.name : "Default";
        }

        public bool IsPlantSite(int triangleIndex)
        {
            SurfaceData surface = SurfaceAt(triangleIndex);
            return surface != null && surface.isPlantSite;
        }
    }
}
