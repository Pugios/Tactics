using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

namespace ValorantTrainer.Vision
{
    /// <summary>
    /// Draws every renderer on the VisionCone layer whose material has a
    /// "LightMode" = "VisionMask" pass into an RG8 mask texture
    /// (R = inside FOV, G = how much blur the area may receive).
    /// The mask is handed to VisionFogRenderPass via VisionFrameData.
    /// </summary>
    public class VisionMaskRenderPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId VisionMaskTag = new ShaderTagId("VisionMask");

        private readonly VisionFogRendererFeature.Settings settings;
        private readonly FilteringSettings filteringSettings;

        public VisionMaskRenderPass(VisionFogRendererFeature.Settings settings)
        {
            this.settings = settings;
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            filteringSettings = new FilteringSettings(RenderQueueRange.all, settings.visionConeLayer);
        }

        private class PassData
        {
            public RendererListHandle rendererList;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
            TextureDesc maskDesc = new TextureDesc(
                Mathf.Max(1, camDesc.width / settings.maskDownscale),
                Mathf.Max(1, camDesc.height / settings.maskDownscale))
            {
                format = GraphicsFormat.R8G8_UNorm,
                name = "_VisionMaskRT",
                clearBuffer = true,
                clearColor = Color.black,
            };

            TextureHandle maskTexture = renderGraph.CreateTexture(maskDesc);

            VisionFrameData visionData = frameData.GetOrCreate<VisionFrameData>();
            visionData.maskTexture = maskTexture;

            DrawingSettings drawSettings = CreateDrawingSettings(
                VisionMaskTag, renderingData, cameraData, lightData,
                cameraData.defaultOpaqueSortFlags);

            RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawSettings, filteringSettings);
            RendererListHandle rendererList = renderGraph.CreateRendererList(listParams);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Vision Mask", out var passData))
            {
                passData.rendererList = rendererList;

                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(maskTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }
        }
    }
}
