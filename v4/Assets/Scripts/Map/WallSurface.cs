using UnityEngine;

namespace Tactics.Map
{
    /// <summary>
    /// Marks one piece of wall geometry with the material it is made of.
    /// Attached automatically on model import (see MapImportPostprocessor) from
    /// the object's Blender name, so a re-exported map carries its surfaces in
    /// with it and needs no hand-wiring.
    ///
    /// Read server-side, once per pellet, via TryGetComponent on the collider
    /// the shot crossed; geometry without one falls back to the default travel
    /// distance, so an unlabelled wall is still perfectly shootable.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallSurface : MonoBehaviour
    {
        public WallSurfaceData surface;

        /// <summary>Travel budget in meters, or <paramref name="fallback"/> when unassigned.</summary>
        public float MaxTravelMeters(float fallback) =>
            surface != null ? surface.maxTravelMeters : fallback;

        public string SurfaceName => surface != null ? surface.ResolvedName : "Default";
    }
}
