using UnityEngine;

namespace Tactics.Vision
{
    [System.Serializable]
    public class VisionFogRendererSettings
    {
        [Tooltip("Fullscreen shader: darkens everything a standing enemy could not be seen at (Tactics/VisionFogComposite).")]
        public Shader compositeFogShader;

        [Tooltip("Blit shader copying the eye camera's depth buffer into the vision depth map (Tactics/VisionEyeDepthCopy).")]
        public Shader depthCopyShader;

        [Range(0f, 1f)]
        [Tooltip("Used when no VisionController is active in the scene.")]
        public float fallbackFogStrength = 0.9f;

        [Tooltip("When enabled, the game view shows the visibility mask instead of fog (for debugging).")]
        public bool debugShowMask;
    }
}
