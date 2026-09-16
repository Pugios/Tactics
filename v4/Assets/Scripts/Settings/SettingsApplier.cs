using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Tactics.Settings
{
    /// <summary>
    /// Pushes settings into the engine: screen mode and resolution, frame pacing,
    /// and the URP pipeline asset.
    ///
    /// The pipeline asset is never edited in place. At first use it is cloned and
    /// the clone is installed for the current quality level, because properties
    /// set on the real asset during Play Mode are written straight into the
    /// project file. On leaving Play Mode (Application.quitting) the original
    /// asset, VSync and frame cap are put back so the editor is left as found.
    /// </summary>
    public static class SettingsApplier
    {
        private static UniversalRenderPipelineAsset runtimeAsset;
        private static RenderPipelineAsset originalQualityPipeline;
        private static float authoredShadowDistance;
        private static int authoredShadowResolution;

        private static bool capturedEngineDefaults;
        private static int originalVSyncCount;
        private static int originalTargetFrameRate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            runtimeAsset = null;
            originalQualityPipeline = null;
            capturedEngineDefaults = false;
        }

        public static void ApplyAll(GameSettings settings)
        {
            // An untouched file keeps whatever window the game launched with.
            if (settings.video.HasResolution) ApplyDisplay(settings.video);
            ApplyFrameRate(settings.video);
            ApplyGraphics(settings.graphics);
        }

        public static FullScreenMode ToFullScreenMode(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.Fullscreen: return FullScreenMode.ExclusiveFullScreen;
                case WindowMode.Borderless: return FullScreenMode.FullScreenWindow;
                default: return FullScreenMode.Windowed;
            }
        }

        public static WindowMode FromFullScreenMode(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return WindowMode.Fullscreen;
                case FullScreenMode.FullScreenWindow: return WindowMode.Borderless;
                default: return WindowMode.Windowed;
            }
        }

        /// <summary>
        /// Window mode and resolution. Has no effect in the Editor, whose Game
        /// view size is set by the view itself — test this in a build.
        /// </summary>
        public static void ApplyDisplay(VideoSettings video)
        {
            if (Application.isEditor) return;

            int width = video.HasResolution ? video.width : Screen.currentResolution.width;
            int height = video.HasResolution ? video.height : Screen.currentResolution.height;
            Screen.SetResolution(width, height, ToFullScreenMode(video.windowMode));
        }

        public static void ApplyFrameRate(VideoSettings video)
        {
            CaptureEngineDefaults();
            QualitySettings.vSyncCount = video.vSync ? 1 : 0;
            // VSync paces frames itself; Unity ignores targetFrameRate while it's on.
            Application.targetFrameRate = video.vSync || video.frameRateCap <= 0 ? -1 : video.frameRateCap;
        }

        public static void ApplyGraphics(GraphicsQualitySettings graphics)
        {
            UniversalRenderPipelineAsset asset = GetRuntimeAsset();
            if (asset == null) return;

            asset.renderScale = graphics.renderScale;
            asset.msaaSampleCount = graphics.msaa;

            // URP skips every shadow pass once the shadow distance is zero.
            asset.shadowDistance = graphics.shadows == ShadowLevel.Off ? 0f : authoredShadowDistance;
            asset.mainLightShadowmapResolution = ShadowResolutionFor(graphics.shadows);
        }

        private static int ShadowResolutionFor(ShadowLevel quality)
        {
            switch (quality)
            {
                case ShadowLevel.Low: return 512;
                case ShadowLevel.Medium: return 1024;
                case ShadowLevel.High: return Mathf.Max(2048, authoredShadowResolution);
                default: return authoredShadowResolution;
            }
        }

        private static UniversalRenderPipelineAsset GetRuntimeAsset()
        {
            if (runtimeAsset != null) return runtimeAsset;

            RenderPipelineAsset active = QualitySettings.renderPipeline != null
                ? QualitySettings.renderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            if (!(active is UniversalRenderPipelineAsset source)) return null;

            CaptureEngineDefaults();
            originalQualityPipeline = QualitySettings.renderPipeline;
            authoredShadowDistance = source.shadowDistance;
            authoredShadowResolution = source.mainLightShadowmapResolution;

            runtimeAsset = Object.Instantiate(source);
            runtimeAsset.name = source.name + " (Runtime)";
            runtimeAsset.hideFlags = HideFlags.DontSave;
            QualitySettings.renderPipeline = runtimeAsset;
            return runtimeAsset;
        }

        private static void CaptureEngineDefaults()
        {
            if (capturedEngineDefaults) return;
            capturedEngineDefaults = true;
            originalVSyncCount = QualitySettings.vSyncCount;
            originalTargetFrameRate = Application.targetFrameRate;
            Application.quitting += RestoreEngineDefaults;
        }

        private static void RestoreEngineDefaults()
        {
            Application.quitting -= RestoreEngineDefaults;
            if (!Application.isEditor) return;

            QualitySettings.vSyncCount = originalVSyncCount;
            Application.targetFrameRate = originalTargetFrameRate;
            if (runtimeAsset != null)
            {
                QualitySettings.renderPipeline = originalQualityPipeline;
                Object.Destroy(runtimeAsset);
                runtimeAsset = null;
            }
            capturedEngineDefaults = false;
        }
    }
}
