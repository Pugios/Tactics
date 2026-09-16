using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Tactics.Settings;

namespace Tactics.Tests.EditMode
{
    public class GameSettingsTests
    {
        // --- Loading ---

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json at all {")]
        public void FromJson_UnreadableInput_GivesDefaults(string json)
        {
            GameSettings settings = GameSettings.FromJson(json);

            Assert.AreEqual(GameSettings.CurrentVersion, settings.version);
            Assert.IsFalse(settings.video.HasResolution);
            Assert.AreEqual(QualityPreset.High, settings.graphics.preset);
            Assert.IsTrue(settings.crosshair.showFiringError);
            Assert.IsTrue(settings.crosshair.showMovementError);
        }

        [Test]
        public void FromJson_RoundTripsEveryEdit()
        {
            var original = new GameSettings();
            original.video.windowMode = WindowMode.Windowed;
            original.video.width = 1600;
            original.video.height = 900;
            original.video.frameRateCap = 144;
            original.graphics.ApplyPreset(QualityPreset.Low);
            original.crosshair.color = new Color(0f, 1f, 1f, 0.5f);
            original.crosshair.showMovementError = false;
            original.crosshair.plusGap = 7.5f;

            GameSettings loaded = GameSettings.FromJson(original.ToJson());

            Assert.AreEqual(WindowMode.Windowed, loaded.video.windowMode);
            Assert.AreEqual(1600, loaded.video.width);
            Assert.AreEqual(900, loaded.video.height);
            Assert.AreEqual(144, loaded.video.frameRateCap);
            Assert.AreEqual(QualityPreset.Low, loaded.graphics.preset);
            Assert.AreEqual(0.75f, loaded.graphics.renderScale, 1e-5f);
            Assert.AreEqual(new Color(0f, 1f, 1f, 0.5f), loaded.crosshair.color);
            Assert.IsFalse(loaded.crosshair.showMovementError);
            Assert.AreEqual(7.5f, loaded.crosshair.plusGap, 1e-5f);
        }

        [Test]
        public void FromJson_MissingSections_AreFilledWithDefaults()
        {
            GameSettings settings = GameSettings.FromJson("{\"version\":1}");

            Assert.IsNotNull(settings.video);
            Assert.IsNotNull(settings.graphics);
            Assert.IsNotNull(settings.crosshair);
        }

        [Test]
        public void FromJson_OutOfRangeValues_AreClamped()
        {
            const string json = "{\"video\":{\"windowMode\":9,\"width\":-5,\"height\":1080,\"frameRateCap\":77}," +
                                "\"graphics\":{\"renderScale\":3.0,\"msaa\":3,\"shadows\":42}," +
                                "\"crosshair\":{\"plusLength\":500,\"plusThickness\":-1,\"color\":{\"r\":2,\"g\":0.5,\"b\":-1,\"a\":1}}}";

            GameSettings settings = GameSettings.FromJson(json);

            Assert.AreEqual(WindowMode.Borderless, settings.video.windowMode);
            Assert.IsFalse(settings.video.HasResolution, "a half-valid size is no size");
            Assert.AreEqual(0, settings.video.frameRateCap, "a cap the menu can't show becomes unlimited");
            Assert.AreEqual(GraphicsQualitySettings.MaxRenderScale, settings.graphics.renderScale);
            Assert.AreEqual(1, settings.graphics.msaa);
            Assert.AreEqual(ShadowLevel.High, settings.graphics.shadows);
            Assert.AreEqual(CrosshairSettings.MaxLineLength, settings.crosshair.plusLength);
            Assert.AreEqual(0.5f, settings.crosshair.plusThickness);
            Assert.AreEqual(new Color(1f, 0.5f, 0f, 1f), settings.crosshair.color);
        }

        // --- Graphics presets ---

        [Test]
        public void ApplyPreset_ThenRelabel_KeepsThePreset()
        {
            foreach (QualityPreset preset in new[] { QualityPreset.Low, QualityPreset.Medium, QualityPreset.High })
            {
                var graphics = new GraphicsQualitySettings();
                graphics.ApplyPreset(preset);
                graphics.RefreshPresetLabel();
                Assert.AreEqual(preset, graphics.preset);
            }
        }

        [Test]
        public void EditingOneOption_RelabelsAsCustom_AndBackWhenItMatchesAgain()
        {
            var graphics = new GraphicsQualitySettings();
            graphics.ApplyPreset(QualityPreset.High);

            graphics.msaa = 8;
            graphics.RefreshPresetLabel();
            Assert.AreEqual(QualityPreset.Custom, graphics.preset);

            graphics.msaa = 2;
            graphics.shadows = ShadowLevel.Medium;
            graphics.RefreshPresetLabel();
            Assert.AreEqual(QualityPreset.Medium, graphics.preset);
        }

        [Test]
        public void ApplyPreset_Custom_LeavesOptionsUntouched()
        {
            var graphics = new GraphicsQualitySettings();
            graphics.ApplyPreset(QualityPreset.Low);
            graphics.ApplyPreset(QualityPreset.Custom);

            Assert.AreEqual(0.75f, graphics.renderScale, 1e-5f);
            Assert.AreEqual(1, graphics.msaa);
            Assert.AreEqual(ShadowLevel.Off, graphics.shadows);
        }

        // --- Crosshair ---

        [Test]
        public void PlusShowsSpread_OnlyWhenAnErrorIsShown()
        {
            var crosshair = new CrosshairSettings { showFiringError = false, showMovementError = false };
            Assert.IsFalse(crosshair.PlusShowsSpread);

            crosshair.showMovementError = true;
            Assert.IsTrue(crosshair.PlusShowsSpread);

            crosshair.showMovementError = false;
            crosshair.showFiringError = true;
            Assert.IsTrue(crosshair.PlusShowsSpread);
        }

        // --- Resolutions ---

        [Test]
        public void ResolutionOptions_DeduplicatesRefreshRates_LargestFirst()
        {
            var reported = new List<Vector2Int>
            {
                new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1920, 1080),
                new Vector2Int(1920, 1200), new Vector2Int(0, 0),
            };

            List<Vector2Int> options = ResolutionOptions.Build(reported, new Vector2Int(1600, 900));

            CollectionAssert.AreEqual(new[]
            {
                new Vector2Int(1920, 1200), new Vector2Int(1920, 1080),
                new Vector2Int(1600, 900), new Vector2Int(1280, 720),
            }, options);
        }
    }
}
