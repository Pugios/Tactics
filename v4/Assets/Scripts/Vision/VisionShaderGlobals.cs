using UnityEngine;

namespace Tactics.Vision
{
    public static class VisionShaderGlobals
    {
        public static readonly int FootprintMask = Shader.PropertyToID("_VisionFootprintMask");
        public static readonly int FootprintVertCount = Shader.PropertyToID("_VisionFootprintVertCount");
        public static readonly int FootprintVerts = Shader.PropertyToID("_VisionFootprintVerts");
        public static readonly int SceneDepth = Shader.PropertyToID("_VisionSceneDepth");
        public static readonly int MaskOrigin = Shader.PropertyToID("_VisionMaskOrigin");
        public static readonly int MaskHalfExtent = Shader.PropertyToID("_VisionMaskHalfExtent");
        public static readonly int HasValidMask = Shader.PropertyToID("_VisionHasValidMask");
        public static readonly int FogStrength = Shader.PropertyToID("_FogStrength");
    }
}
