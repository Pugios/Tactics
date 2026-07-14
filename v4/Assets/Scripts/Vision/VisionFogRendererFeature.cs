using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Tactics.Vision
{
    [DisallowMultipleRendererFeature]
    public class VisionFogRendererFeature : ScriptableRendererFeature
    {

        [Tooltip("Fullscreen shader: darkens everything a standing enemy could not be seen at (Tactics/VisionFogComposite).")]
        public Shader compositeFogShader;

        [Tooltip("Blit shader copying the eye camera's depth buffer into the vision depth map (Tactics/VisionEyeDepthCopy).")]
        public Shader depthCopyShader;

        [Range(0f, 1f)]
        [Tooltip("Used when no VisionController is active in the scene.")]
        public float fallbackFogStrength = 0.9f;

        [Tooltip("When enabled, the game view shows the visibility mask instead of fog (for debugging).")]
        public bool debugShowMask;

        private VisionFogCompositePass compositePass;
        private VisionEyeDepthCapturePass depthCapturePass;
        private Material compositeMaterial;
        private Material depthCopyMaterial;
        private bool warnedMissingDepthCopy;

        public override void Create()
        {
            compositePass = new VisionFogCompositePass(compositeFogShader, depthCopyShader, fallbackFogStrength, debugShowMask);
            depthCapturePass = new VisionEyeDepthCapturePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            VisionController controller = VisionController.Active;
            UnityEngine.Camera camera = renderingData.cameraData.camera;

            // The eye camera gets the depth capture pass; every other game camera gets the fog composite.
            if (controller != null && controller.EyeCamera == camera)
            {
                if (depthCopyShader == null || controller.EyeDepthHandle == null)
                {
                    if (!warnedMissingDepthCopy)
                    {
                        warnedMissingDepthCopy = true;
                        Debug.LogWarning(
                            "[Vision] Eye depth capture cannot run: " +
                            (depthCopyShader == null
                                ? "'Depth Copy Shader' is not assigned on the VisionFogRendererFeature (PC_Renderer asset). Assign Tactics/VisionEyeDepthCopy."
                                : "VisionController has no eye depth texture."));
                    }

                    return;
                }

                if (depthCopyMaterial == null)
                    depthCopyMaterial = CoreUtils.CreateEngineMaterial(depthCopyShader);

                depthCapturePass.Setup(depthCopyMaterial, controller.EyeDepthHandle);
                renderer.EnqueuePass(depthCapturePass);
                return;
            }

            if (compositeFogShader == null)
                return;

            if (compositeMaterial == null)
                compositeMaterial = CoreUtils.CreateEngineMaterial(compositeFogShader);

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
