using System;
using UnityEngine;

namespace Tactics.Settings
{
    /// <summary>
    /// Everything a player sets for themselves on this machine, saved as one JSON
    /// file. Match rules (cheats) are deliberately not here: they belong to the
    /// match and its host, see <c>Tactics.Core.MatchRules</c>.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public VideoSettings video = new VideoSettings();
        public GraphicsQualitySettings graphics = new GraphicsQualitySettings();
        public CrosshairSettings crosshair = new CrosshairSettings();

        /// <summary>
        /// Reads a saved file. Anything unreadable falls back to defaults, and
        /// every value is clamped, so a hand-edited or older file can never feed
        /// the game something out of range.
        /// </summary>
        public static GameSettings FromJson(string json)
        {
            GameSettings settings = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { settings = JsonUtility.FromJson<GameSettings>(json); }
                catch (ArgumentException) { settings = null; }
            }

            settings ??= new GameSettings();
            settings.Sanitize();
            return settings;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        public GameSettings Clone() => FromJson(ToJson());

        public void Sanitize()
        {
            // Sections missing from an older file deserialize as null.
            video ??= new VideoSettings();
            graphics ??= new GraphicsQualitySettings();
            crosshair ??= new CrosshairSettings();

            video.Sanitize();
            graphics.Sanitize();
            crosshair.Sanitize();
            version = CurrentVersion;
        }
    }

    public enum WindowMode
    {
        Fullscreen,
        Borderless,
        Windowed,
    }

    [Serializable]
    public class VideoSettings
    {
        public static readonly int[] FrameRateCaps = { 0, 30, 60, 120, 144, 165, 240, 360 };

        public WindowMode windowMode = WindowMode.Borderless;

        /// <summary>0 × 0 means "never chosen": the game keeps whatever resolution it launched with.</summary>
        public int width;
        public int height;

        public bool vSync;

        /// <summary>Frames per second, 0 = unlimited. Ignored while VSync is on.</summary>
        public int frameRateCap;

        public bool HasResolution => width > 0 && height > 0;

        public void Sanitize()
        {
            if (!Enum.IsDefined(typeof(WindowMode), windowMode)) windowMode = WindowMode.Borderless;
            if (width <= 0 || height <= 0) width = height = 0;
            if (Array.IndexOf(FrameRateCaps, frameRateCap) < 0) frameRateCap = 0;
        }
    }

    public enum QualityPreset
    {
        Low,
        Medium,
        High,
        Custom,
    }

    public enum ShadowLevel
    {
        Off,
        Low,
        Medium,
        High,
    }

    /// <summary>
    /// Rendering quality. Nothing here may change what a player can SEE of the
    /// enemy: fog of war and its depth map are gameplay, and stay fixed.
    /// </summary>
    [Serializable]
    public class GraphicsQualitySettings
    {
        public static readonly int[] MsaaOptions = { 1, 2, 4, 8 };
        public const float MinRenderScale = 0.5f;
        public const float MaxRenderScale = 1f;

        public QualityPreset preset = QualityPreset.High;
        public float renderScale = 1f;
        /// <summary>MSAA sample count: 1 (off), 2, 4 or 8.</summary>
        public int msaa = 4;
        public ShadowLevel shadows = ShadowLevel.High;

        /// <summary>The values a named preset stands for. <see cref="QualityPreset.Custom"/> has none.</summary>
        public static bool TryGetPreset(QualityPreset preset, out GraphicsQualitySettings values)
        {
            switch (preset)
            {
                case QualityPreset.Low:
                    values = new GraphicsQualitySettings { preset = preset, renderScale = 0.75f, msaa = 1, shadows = ShadowLevel.Off };
                    return true;
                case QualityPreset.Medium:
                    values = new GraphicsQualitySettings { preset = preset, renderScale = 1f, msaa = 2, shadows = ShadowLevel.Medium };
                    return true;
                case QualityPreset.High:
                    values = new GraphicsQualitySettings { preset = preset, renderScale = 1f, msaa = 4, shadows = ShadowLevel.High };
                    return true;
                default:
                    values = null;
                    return false;
            }
        }

        /// <summary>Sets every option to the preset's values (no-op for Custom).</summary>
        public void ApplyPreset(QualityPreset newPreset)
        {
            if (!TryGetPreset(newPreset, out var values)) { preset = QualityPreset.Custom; return; }
            preset = newPreset;
            renderScale = values.renderScale;
            msaa = values.msaa;
            shadows = values.shadows;
        }

        /// <summary>
        /// Relabels the preset after an individual option changed: the named
        /// preset whose values now match exactly, otherwise Custom.
        /// </summary>
        public void RefreshPresetLabel()
        {
            foreach (QualityPreset candidate in new[] { QualityPreset.High, QualityPreset.Medium, QualityPreset.Low })
            {
                TryGetPreset(candidate, out var values);
                if (Mathf.Approximately(values.renderScale, renderScale) && values.msaa == msaa && values.shadows == shadows)
                {
                    preset = candidate;
                    return;
                }
            }
            preset = QualityPreset.Custom;
        }

        public void Sanitize()
        {
            renderScale = float.IsNaN(renderScale) ? 1f : Mathf.Clamp(renderScale, MinRenderScale, MaxRenderScale);
            if (Array.IndexOf(MsaaOptions, msaa) < 0) msaa = 1;
            if (!Enum.IsDefined(typeof(ShadowLevel), shadows)) shadows = ShadowLevel.High;
            RefreshPresetLabel();
        }
    }
}
