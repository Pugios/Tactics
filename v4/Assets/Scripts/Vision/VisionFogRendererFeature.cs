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
        private VisionEyeDepthCapturePass depthCapturePass;
        private Material compositeMaterial;
        private Material depthCopyMaterial;
        private bool warnedMissingDepthCopy;

        public override void Create()
        {
            compositePass = new VisionFogCompositePass(settings);
            depthCapturePass = new VisionEyeDepthCapturePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            VisionController controller = VisionController.Active;
            UnityEngine.Camera camera = renderingData.cameraData.camera;

            // The eye camera gets the depth capture pass; every other game camera gets the fog composite.
            if (controller != null && controller.EyeCamera == camera)
            {
                if (settings.depthCopyShader == null || controller.EyeDepthHandle == null)
                {
                    if (!warnedMissingDepthCopy)
                    {
                        warnedMissingDepthCopy = true;
                        Debug.LogWarning(
                            "[Vision] Eye depth capture cannot run: " +
                            (settings.depthCopyShader == null
                                ? "'Depth Copy Shader' is not assigned on the VisionFogRendererFeature (PC_Renderer asset). Assign Tactics/VisionEyeDepthCopy."
                                : "VisionController has no eye depth texture."));
                    }

                    return;
                }

                if (depthCopyMaterial == null)
                    depthCopyMaterial = CoreUtils.CreateEngineMaterial(settings.depthCopyShader);

                depthCapturePass.Setup(depthCopyMaterial, controller.EyeDepthHandle);
                renderer.EnqueuePass(depthCapturePass);
                return;
            }

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
            CoreUtils.Destroy(depthCopyMaterial);
            depthCopyMaterial = null;
        }
    }
}
