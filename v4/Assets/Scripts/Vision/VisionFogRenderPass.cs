using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace ValorantTrainer.Vision
{
    /// <summary>
    /// Fullscreen composite: reads the camera color and the vision mask,
    /// darkens pixels where the mask is black, and makes the result the new camera color.
    /// </summary>
    public class VisionFogRenderPass : ScriptableRenderPass
    {
        private static readonly int VisionMaskTexId = Shader.PropertyToID("_VisionMaskTex");
        private static readonly int FogStrengthId = Shader.PropertyToID("_FogStrength");
        private static readonly int FogSoftnessId = Shader.PropertyToID("_FogSoftness");

        private readonly VisionFogRendererFeature.Settings settings;
        private Material fogMaterial;

        public VisionFogRenderPass(VisionFogRendererFeature.Settings settings)
        {
            this.settings = settings;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void Setup(Material material)
        {
            fogMaterial = material;
        }

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle mask;
            public float fogStrength;
            public float fogSoftness;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (fogMaterial == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game)
                return;

            if (!frameData.Contains<VisionFrameData>())
                return;

            VisionFrameData visionData = frameData.Get<VisionFrameData>();
            if (!visionData.maskTexture.IsValid())
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle source = resourceData.activeColorTexture;

            // Render Graph forbids reading and writing the same texture in one pass,
            // so write to a new texture and promote it to the camera color.
            TextureDesc destDesc = renderGraph.GetTextureDesc(source);
            destDesc.name = "_VisionFogResult";
            destDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Vision Fog", out var passData))
            {
                passData.material = fogMaterial;
                passData.source = source;
                passData.mask = visionData.maskTexture;
                passData.fogStrength = settings.fogStrength;
                passData.fogSoftness = settings.fogEdgeSoftness;

                builder.UseTexture(passData.source);
                builder.UseTexture(passData.mask);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RTHandle maskRT = data.mask;
                    data.material.SetTexture(VisionMaskTexId, maskRT);
                    data.material.SetFloat(FogStrengthId, data.fogStrength);
                    data.material.SetFloat(FogSoftnessId, data.fogSoftness);

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }
}
