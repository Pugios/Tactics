using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace ValorantTrainer.Vision
{
    /// <summary>
    /// Data shared between the mask pass and the fog pass within one frame.
    /// </summary>
    public class VisionFrameData : ContextItem
    {
        public TextureHandle maskTexture;

        public override void Reset()
        {
            maskTexture = TextureHandle.nullHandle;
        }
    }

    /// <summary>
    /// URP Renderer Feature registering the vision mask and fog composite passes.
    /// Lives on the PC_Renderer asset.
    /// </summary>
    [DisallowMultipleRendererFeature]
    public class VisionFogRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Fullscreen shader that darkens pixels outside the mask (Vision/VisionFogComposite).")]
            public Shader fogCompositeShader;

            [Range(0f, 1f)]
            [Tooltip("How much to darken geometry outside the vision cone (0 = none, 1 = black).")]
            public float fogStrength = 0.65f;

            [Range(0f, 16f)]
            [Tooltip("Blur radius (in mask texels) applied to the lit boundary. Purely visual; gameplay visibility is unaffected. 0 = hard edge.")]
            public float fogEdgeSoftness = 6f;

            [Range(1, 4)]
            [Tooltip("Divide screen resolution for the mask RT (1 = full res, 2 = half, etc.). Higher values also soften the edge.")]
            public int maskDownscale = 1;

            [Tooltip("Layer of the VisionCone mesh. Must be INCLUDED in the Main Camera culling mask; the VisionMaskDraw material keeps it invisible in the normal view.")]
            public LayerMask visionConeLayer = 1 << 8;
        }

        public Settings settings = new Settings();

        private VisionMaskRenderPass maskPass;
        private VisionFogRenderPass fogPass;
        private Material fogMaterial;

        public override void Create()
        {
            maskPass = new VisionMaskRenderPass(settings);
            fogPass = new VisionFogRenderPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.fogCompositeShader == null || settings.visionConeLayer.value == 0)
                return;

            if (fogMaterial == null)
                fogMaterial = CoreUtils.CreateEngineMaterial(settings.fogCompositeShader);

            fogPass.Setup(fogMaterial);

            renderer.EnqueuePass(maskPass);
            renderer.EnqueuePass(fogPass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(fogMaterial);
            fogMaterial = null;
        }
    }
}
