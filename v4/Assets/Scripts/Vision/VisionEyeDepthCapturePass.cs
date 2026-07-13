using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Tactics.Vision
{
    /// <summary>
    /// Runs on the eye camera only: copies its depth buffer into the persistent vision depth map
    /// and publishes the exact view-projection matrix URP rasterized with, so the composite pass
    /// can project world points into the depth map without platform matrix guesswork.
    /// </summary>
    public class VisionEyeDepthCapturePass : ScriptableRenderPass
    {
        /// <summary>Time.frameCount of the last frame this pass was recorded. Used to detect a dead capture chain.</summary>
        public static int LastCaptureFrameCount { get; private set; } = -1;

        /// <summary>View-projection matrix from the most recent eye depth capture (GPU-exact).</summary>
        public static Matrix4x4 LastEyeViewProjection { get; private set; }

        private Material copyMaterial;
        private RTHandle destination;

        public VisionEyeDepthCapturePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Setup(Material material, RTHandle destinationHandle)
        {
            copyMaterial = material;
            destination = destinationHandle;
        }

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public Matrix4x4 eyeViewProjection;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (copyMaterial == null || destination == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle sourceDepth = resourceData.cameraDepthTexture;
            if (!sourceDepth.IsValid())
                sourceDepth = resourceData.activeDepthTexture;
            if (!sourceDepth.IsValid())
                return;

            TextureHandle destinationHandle = renderGraph.ImportTexture(destination);
            LastCaptureFrameCount = Time.frameCount;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Vision Eye Depth Capture", out var passData))
            {
                passData.material = copyMaterial;
                passData.source = sourceDepth;
                passData.eyeViewProjection = cameraData.GetGPUProjectionMatrix() * cameraData.GetViewMatrix();
                LastEyeViewProjection = passData.eyeViewProjection;

                builder.UseTexture(sourceDepth, AccessFlags.Read);
                builder.SetRenderAttachment(destinationHandle, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalMatrix(VisionShaderGlobals.EyeVP, data.eyeViewProjection);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }
        }
    }
}
