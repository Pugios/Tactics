using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Tactics.Vision
{
    [DisallowMultipleRendererFeature]
    public class VisionFogRendererFeature : ScriptableRendererFeature
    {
        public VisionFogRendererSettings settings = new VisionFogRendererSettings();

        private VisionFogCompositePass compositePass;
        private Material compositeMaterial;

        public override void Create()
        {
            compositePass = new VisionFogCompositePass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.compositeFogShader == null)
                return;

            if (compositeMaterial == null)
                compositeMaterial = CoreUtils.CreateEngineMaterial(settings.compositeFogShader);

            compositePass.Setup(compositeMaterial);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(compositeMaterial);
            compositeMaterial = null;
        }
    }
}
