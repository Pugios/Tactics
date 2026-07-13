using UnityEngine;

namespace Tactics.Vision
{
    public static class VisionShaderGlobals
    {
        // Composite pass inputs
        public static readonly int SceneDepth = Shader.PropertyToID("_VisionSceneDepth");
        public static readonly int FogStrength = Shader.PropertyToID("_FogStrength");
        public static readonly int HasValidMask = Shader.PropertyToID("_VisionHasValidMask");

        // Vision depth map (rendered from the player's eye)
        public static readonly int EyeDepth = Shader.PropertyToID("_VisionEyeDepth");
        public static readonly int EyeVP = Shader.PropertyToID("_VisionEyeVP");
        public static readonly int EyeView = Shader.PropertyToID("_VisionEyeView");
        public static readonly int EyePosition = Shader.PropertyToID("_VisionEyePosWS");
        public static readonly int EyeForward = Shader.PropertyToID("_VisionEyeForwardWS");
        public static readonly int EyeZBufferParams = Shader.PropertyToID("_VisionEyeZBufferParams");
        public static readonly int EyeDepthParams = Shader.PropertyToID("_VisionEyeDepthParams");
        public static readonly int SampleHeights = Shader.PropertyToID("_VisionSampleHeights");
    }
}
