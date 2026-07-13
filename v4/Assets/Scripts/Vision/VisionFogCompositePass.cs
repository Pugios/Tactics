using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Tactics.Vision
{
    /// <summary>
    /// Single fullscreen pass: darkens every screen pixel where a standing enemy could not be seen.
    /// Visibility is tested against the vision depth map captured by VisionEyeDepthCapturePass.
    /// </summary>
    public class VisionFogCompositePass : ScriptableRenderPass
    {
        private readonly VisionFogRendererSettings settings;
        private Material compositeMaterial;

        public VisionFogCompositePass(VisionFogRendererSettings settings)
        {
            this.settings = settings;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Setup(Material material)
        {
            compositeMaterial = material;
        }

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle sceneDepth;
            public float fogStrength;
            public bool debugShowMask;
            public VisionShaderState vision;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (compositeMaterial == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle source = resourceData.activeColorTexture;

            TextureHandle sceneDepth = resourceData.cameraDepthTexture;
            if (!sceneDepth.IsValid())
                sceneDepth = resourceData.activeDepthTexture;
            if (!sceneDepth.IsValid())
                return;

            TextureDesc destDesc = renderGraph.GetTextureDesc(source);
            destDesc.name = "_VisionFogCompositeResult";
            destDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            VisionShaderState visionState = VisionController.Active != null
                ? VisionController.Active.GetShaderState()
                : VisionShaderState.Invalid;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Vision Fog Composite", out var passData))
            {
                passData.material = compositeMaterial;
                passData.source = source;
                passData.sceneDepth = sceneDepth;
                passData.fogStrength = ResolveFogStrength();
                passData.debugShowMask = settings.debugShowMask ||
                    (VisionController.Active != null && VisionController.Active.DebugShowMaskTexture);
                passData.vision = visionState;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(sceneDepth, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    if (data.debugShowMask)
                        data.material.EnableKeyword("VISION_DEBUG_SHOW_MASK");
                    else
                        data.material.DisableKeyword("VISION_DEBUG_SHOW_MASK");

                    data.material.SetFloat(VisionShaderGlobals.FogStrength, data.fogStrength);
                    data.material.SetTexture(VisionShaderGlobals.SceneDepth, data.sceneDepth);

                    BindVisionState(data.material, data.vision);

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        private static void BindVisionState(Material material, VisionShaderState vision)
        {
            material.SetFloat(
                VisionShaderGlobals.HasValidMask,
                vision.HasValidMask && vision.EyeDepthTexture != null ? 1f : 0f);

            if (!vision.HasValidMask || vision.EyeDepthTexture == null)
                return;

            material.SetTexture(VisionShaderGlobals.EyeDepth, vision.EyeDepthTexture);
            material.SetMatrix(VisionShaderGlobals.EyeVP, vision.EyeViewProjection);
            material.SetMatrix(VisionShaderGlobals.EyeView, vision.EyeView);
            material.SetVector(VisionShaderGlobals.EyePosition, vision.EyePosition);
            material.SetVector(VisionShaderGlobals.EyeForward, vision.EyeForward);
            material.SetVector(VisionShaderGlobals.EyeZBufferParams, vision.EyeZBufferParams);
            material.SetVector(VisionShaderGlobals.EyeDepthParams, vision.EyeDepthParams);
            material.SetVector(VisionShaderGlobals.SampleHeights, vision.SampleHeights);
        }

        private float ResolveFogStrength()
        {
            if (VisionController.Active != null && VisionController.Active.HasValidAim)
                return VisionController.Active.FogStrength;

            return settings.fallbackFogStrength;
        }
    }
}
