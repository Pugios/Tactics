using UnityEngine;

namespace Tactics.Vision
{
    /// <summary>
    /// Per-frame vision shader inputs published by VisionController and bound explicitly
    /// by VisionFogCompositePass (RenderGraph does not reliably inherit Shader.SetGlobal*).
    /// </summary>
    public readonly struct VisionShaderState
    {
        public bool HasValidMask { get; }
        public Matrix4x4 EyeViewProjection { get; }
        public Matrix4x4 EyeView { get; }
        public Vector3 EyePosition { get; }
        public Vector3 EyeForward { get; }
        public Vector4 EyeZBufferParams { get; }
        public Vector4 EyeDepthParams { get; }
        public Vector4 SampleHeights { get; }
        public Texture EyeDepthTexture { get; }

        public VisionShaderState(
            bool hasValidMask,
            Matrix4x4 eyeViewProjection,
            Matrix4x4 eyeView,
            Vector3 eyePosition,
            Vector3 eyeForward,
            Vector4 eyeZBufferParams,
            Vector4 eyeDepthParams,
            Vector4 sampleHeights,
            Texture eyeDepthTexture)
        {
            HasValidMask = hasValidMask;
            EyeViewProjection = eyeViewProjection;
            EyeView = eyeView;
            EyePosition = eyePosition;
            EyeForward = eyeForward;
            EyeZBufferParams = eyeZBufferParams;
            EyeDepthParams = eyeDepthParams;
            SampleHeights = sampleHeights;
            EyeDepthTexture = eyeDepthTexture;
        }

        public static VisionShaderState Invalid => new VisionShaderState(
            false,
            Matrix4x4.zero,
            Matrix4x4.zero,
            Vector3.zero,
            Vector3.forward,
            Vector4.zero,
            Vector4.zero,
            Vector4.zero,
            null);
    }
}
