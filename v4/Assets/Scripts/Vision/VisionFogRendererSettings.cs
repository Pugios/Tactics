using UnityEngine;

namespace Tactics.Vision
{
    [System.Serializable]
    public class VisionFogRendererSettings
    {
        [Tooltip("Fullscreen shader: darkens outside footprint mask (Tactics/VisionFogComposite).")]
        public Shader compositeFogShader;

        [Range(0f, 1f)]
        [Tooltip("Used when no VisionController is active in the scene.")]
        public float fallbackFogStrength = 0.9f;

        [Range(64, 2048)]
        [Tooltip("Resolution of the world-aligned footprint mask texture.")]
        public int maskTextureResolution = 512;

        [Tooltip("When enabled, the game view shows the footprint mask instead of fog (for debugging).")]
        public bool debugShowMask;
    }
}
