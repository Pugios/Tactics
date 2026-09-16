using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Tactics.Core;
using Tactics.Settings;

namespace Tactics.UI
{
    /// <summary>
    /// The Esc menu: Video, Graphics, Crosshair and Cheats tabs over
    /// <see cref="SettingsService"/>. Every edit is applied at once and saved
    /// when the menu closes — except window mode and resolution, which wait for
    /// Apply and then must be confirmed within <see cref="DisplayConfirmSeconds"/>
    /// or revert, so a resolution the monitor can't show undoes itself.
    ///
    /// Esc is handled here for every menu: with a menu open it asks that menu to
    /// back out a level (<see cref="IGameMenu.Cancel"/>), otherwise it opens this
    /// one — the buy menu closes first, as in Valorant. While open the player is
    /// frozen like during a plant.
    /// </summary>
    public class SettingsMenuController : MonoBehaviour, IGameMenu
    {
        private const float DisplayConfirmSeconds = 15f;
        private const string PageVisibleClass = "page--visible";
        private const string TabSelectedClass = "tab--selected";
        private const string ConfirmVisibleClass = "confirm-overlay--visible";
        private const string CustomChoice = "Custom";
        private const float ShotgunPreviewRadius = 26f;

        private enum Tab { Video, Graphics, Crosshair, Cheats }

        private static readonly string[] WindowModeChoices = { "Fullscreen", "Borderless", "Windowed" };
        private static readonly string[] MsaaChoices = { "Off", "2x MSAA", "4x MSAA", "8x MSAA" };
        private static readonly string[] ShadowChoices = { "Off", "Low", "Medium", "High" };
        private static readonly string[] PresetChoices = { "Low", "Medium", "High", CustomChoice };

        private static readonly (string Name, Color Color)[] ColorPresets =
        {
            ("White", Color.white),
            ("Green", new Color32(0, 255, 0, 255)),
            ("Yellow Green", new Color32(127, 255, 0, 255)),
            ("Green Yellow", new Color32(223, 255, 0, 255)),
            ("Yellow", new Color32(255, 255, 0, 255)),
            ("Cyan", new Color32(0, 255, 255, 255)),
            ("Pink", new Color32(255, 0, 255, 255)),
            ("Red", new Color32(255, 0, 0, 255)),
        };

        [SerializeField] private UIDocument uiDocument;

        // Survives closing, so the menu reopens where it was left.
        private static Tab lastTab = Tab.Video;

        private VisualElement root;
        private InputAction menuAction;
        private readonly List<Action> refreshers = new List<Action>();
        private readonly Dictionary<Tab, (Button Button, VisualElement Page)> tabs = new Dictionary<Tab, (Button, VisualElement)>();

        private CrosshairElement previewPlus;
        private CrosshairElement previewCircle;

        // Display: edited here, only pushed to the screen on Apply.
        private List<Vector2Int> resolutions = new List<Vector2Int>();
        private DropdownField resolutionField;
        private Button applyDisplayButton;
        private WindowMode appliedWindowMode;
        private Vector2Int appliedResolution;
        private WindowMode pendingWindowMode;
        private Vector2Int pendingResolution;

        // Display confirmation countdown after Apply.
        private VisualElement confirmDialog;
        private Label confirmCountdownLabel;
        private bool confirmPending;
        private float confirmRemaining;
        private int confirmShownSeconds = -1;
        private VideoSettings videoBeforeApply;

        public bool IsOpen { get; private set; }

        public bool FreezesPlayer => true;

        private static GameSettings Current => SettingsService.Current;

        public void Cancel()
        {
            if (confirmPending) RevertDisplay();
            else Close();
        }

        private void OnEnable()
        {
            root = uiDocument.rootVisualElement;
            root.style.display = DisplayStyle.None;
            IsOpen = false;

            menuAction = InputSystem.actions.FindAction("Menu");

            refreshers.Clear();
            tabs.Clear();
            BindTabs();
            BindVideo();
            BindGraphics();
            BindCrosshair();
            BindCheats();
            BindFooter();

            MatchRules.Changed += HandleMatchRulesChanged;
        }

        private void OnDisable()
        {
            MatchRules.Changed -= HandleMatchRulesChanged;
            Close();
        }

        private void Update()
        {
            if (menuAction != null && menuAction.WasPressedThisFrame())
            {
                if (MenuState.IsOpen) MenuState.Current.Cancel();
                else Open();
            }

            if (confirmPending) TickDisplayConfirm();
        }

        // ---------------------------------------------------------------- open/close

        private void Open()
        {
            if (IsOpen || !MenuState.TryOpen(this)) return;
            IsOpen = true;
            root.style.display = DisplayStyle.Flex;

            CaptureAppliedDisplay();
            SelectTab(lastTab);
            RefreshAll();
        }

        private void Close()
        {
            if (!IsOpen) return;
            // Leaving with a display change unconfirmed counts as "no".
            if (confirmPending) RevertDisplay();

            IsOpen = false;
            root.style.display = DisplayStyle.None;
            MenuState.NotifyClosed(this);
            SettingsService.Save();
        }

        private void HandleMatchRulesChanged()
        {
            if (IsOpen) RefreshAll();
        }

        /// <summary>Settings were edited: apply them, and re-sync every control (a preset or color pick changes others).</summary>
        private void OnEdited()
        {
            SettingsService.NotifyChanged();
            RefreshAll();
        }

        private void RefreshAll()
        {
            foreach (Action refresh in refreshers) refresh();
        }

        // ---------------------------------------------------------------- tabs

        private void BindTabs()
        {
            AddTab(Tab.Video, "tabVideo", "pageVideo");
            AddTab(Tab.Graphics, "tabGraphics", "pageGraphics");
            AddTab(Tab.Crosshair, "tabCrosshair", "pageCrosshair");
            AddTab(Tab.Cheats, "tabCheats", "pageCheats");

            refreshers.Add(() =>
            {
                // Cheats only exist in a match that allows them.
                bool showCheats = MatchRules.CheatsAllowed;
                tabs[Tab.Cheats].Button.style.display = showCheats ? DisplayStyle.Flex : DisplayStyle.None;
                if (!showCheats && lastTab == Tab.Cheats) SelectTab(Tab.Video);
            });
        }

        private void AddTab(Tab tab, string buttonName, string pageName)
        {
            var button = root.Q<Button>(buttonName);
            var page = root.Q<VisualElement>(pageName);
            tabs[tab] = (button, page);
            button.clicked += () => SelectTab(tab);
        }

        private void SelectTab(Tab tab)
        {
            lastTab = tab;
            foreach (var pair in tabs)
            {
                bool selected = pair.Key == tab;
                pair.Value.Button.EnableInClassList(TabSelectedClass, selected);
                pair.Value.Page.EnableInClassList(PageVisibleClass, selected);
            }
        }

        // ---------------------------------------------------------------- video

        private void BindVideo()
        {
            BindDropdown("windowModeField", WindowModeChoices,
                () => (int)pendingWindowMode,
                i => pendingWindowMode = (WindowMode)i,
                applyNow: false);

            resolutionField = root.Q<DropdownField>("resolutionField");
            resolutionField.RegisterValueChangedCallback(_ =>
            {
                int i = resolutionField.index;
                if (i >= 0 && i < resolutions.Count) pendingResolution = resolutions[i];
                RefreshAll();
            });
            refreshers.Add(() =>
            {
                resolutionField.choices = resolutions.ConvertAll(ResolutionOptions.Label);
                resolutionField.SetValueWithoutNotify(ResolutionOptions.Label(pendingResolution));
            });

            applyDisplayButton = root.Q<Button>("applyDisplayButton");
            applyDisplayButton.clicked += ApplyDisplay;
            refreshers.Add(() => applyDisplayButton.SetEnabled(
                pendingWindowMode != appliedWindowMode || pendingResolution != appliedResolution));

            root.Q<Label>("displayHint").style.display = Application.isEditor ? DisplayStyle.Flex : DisplayStyle.None;

            BindToggle("vsyncToggle", () => Current.video.vSync, v => Current.video.vSync = v);

            var frameCapChoices = new List<string>();
            foreach (int cap in VideoSettings.FrameRateCaps) frameCapChoices.Add(cap <= 0 ? "Unlimited" : cap + " FPS");
            DropdownField frameCapField = BindDropdown("frameCapField", frameCapChoices.ToArray(),
                () => Array.IndexOf(VideoSettings.FrameRateCaps, Current.video.frameRateCap),
                i => Current.video.frameRateCap = VideoSettings.FrameRateCaps[i]);
            // VSync paces frames on its own; a cap would be ignored.
            refreshers.Add(() => frameCapField.SetEnabled(!Current.video.vSync));

            confirmDialog = root.Q<VisualElement>("confirmDialog");
            confirmCountdownLabel = root.Q<Label>("confirmCountdownLabel");
            root.Q<Button>("confirmKeepButton").clicked += KeepDisplay;
            root.Q<Button>("confirmRevertButton").clicked += RevertDisplay;
        }

        /// <summary>What the screen is showing right now, as the menu's starting point.</summary>
        private void CaptureAppliedDisplay()
        {
            VideoSettings video = Current.video;
            appliedWindowMode = video.HasResolution ? video.windowMode : SettingsApplier.FromFullScreenMode(Screen.fullScreenMode);
            appliedResolution = video.HasResolution
                ? new Vector2Int(video.width, video.height)
                : new Vector2Int(Screen.width, Screen.height);
            pendingWindowMode = appliedWindowMode;
            pendingResolution = appliedResolution;

            var reported = new List<Vector2Int>();
            foreach (Resolution r in Screen.resolutions) reported.Add(new Vector2Int(r.width, r.height));
            resolutions = ResolutionOptions.Build(reported, appliedResolution);
        }

        private void ApplyDisplay()
        {
            VideoSettings video = Current.video;
            videoBeforeApply = new VideoSettings { windowMode = video.windowMode, width = video.width, height = video.height };

            video.windowMode = pendingWindowMode;
            video.width = pendingResolution.x;
            video.height = pendingResolution.y;
            SettingsApplier.ApplyDisplay(video);

            confirmPending = true;
            confirmRemaining = DisplayConfirmSeconds;
            confirmShownSeconds = -1;
            confirmDialog.AddToClassList(ConfirmVisibleClass);
            TickDisplayConfirm();
        }

        private void TickDisplayConfirm()
        {
            // Unscaled: a paused or slowed game must not stall the safety revert.
            confirmRemaining -= Time.unscaledDeltaTime;
            if (confirmRemaining <= 0f)
            {
                RevertDisplay();
                return;
            }

            int seconds = Mathf.CeilToInt(confirmRemaining);
            if (seconds == confirmShownSeconds) return;
            confirmShownSeconds = seconds;
            confirmCountdownLabel.text = $"Reverting in {seconds} second{(seconds == 1 ? "" : "s")}.";
        }

        private void KeepDisplay()
        {
            if (!confirmPending) return;
            HideDisplayConfirm();
            appliedWindowMode = pendingWindowMode;
            appliedResolution = pendingResolution;
            SettingsService.Save();
            RefreshAll();
        }

        private void RevertDisplay()
        {
            if (!confirmPending) return;
            HideDisplayConfirm();

            VideoSettings video = Current.video;
            video.windowMode = videoBeforeApply.windowMode;
            video.width = videoBeforeApply.width;
            video.height = videoBeforeApply.height;

            // Put the screen back to what it showed before Apply — even when
            // the player had never chosen a resolution (the saved 0 × 0 stays).
            SettingsApplier.ApplyDisplay(new VideoSettings
            {
                windowMode = appliedWindowMode,
                width = appliedResolution.x,
                height = appliedResolution.y,
            });

            pendingWindowMode = appliedWindowMode;
            pendingResolution = appliedResolution;
            RefreshAll();
        }

        private void HideDisplayConfirm()
        {
            confirmPending = false;
            confirmDialog.RemoveFromClassList(ConfirmVisibleClass);
        }

        // ---------------------------------------------------------------- graphics

        private void BindGraphics()
        {
            BindDropdown("presetField", PresetChoices,
                () => (int)Current.graphics.preset,
                // Picking "Custom" changes nothing, so the label snaps back to
                // whichever preset the values still match.
                i => Current.graphics.ApplyPreset((QualityPreset)i));

            var renderScale = root.Q<SliderInt>("renderScaleSlider");
            renderScale.lowValue = Mathf.RoundToInt(GraphicsQualitySettings.MinRenderScale * 100f);
            renderScale.highValue = Mathf.RoundToInt(GraphicsQualitySettings.MaxRenderScale * 100f);
            renderScale.RegisterValueChangedCallback(e =>
            {
                Current.graphics.renderScale = e.newValue / 100f;
                Current.graphics.RefreshPresetLabel();
                OnEdited();
            });
            refreshers.Add(() => renderScale.SetValueWithoutNotify(Mathf.RoundToInt(Current.graphics.renderScale * 100f)));

            BindDropdown("msaaField", MsaaChoices,
                () => Array.IndexOf(GraphicsQualitySettings.MsaaOptions, Current.graphics.msaa),
                i =>
                {
                    Current.graphics.msaa = GraphicsQualitySettings.MsaaOptions[i];
                    Current.graphics.RefreshPresetLabel();
                });

            BindDropdown("shadowsField", ShadowChoices,
                () => (int)Current.graphics.shadows,
                i =>
                {
                    Current.graphics.shadows = (ShadowLevel)i;
                    Current.graphics.RefreshPresetLabel();
                });
        }

        // ---------------------------------------------------------------- crosshair

        private void BindCrosshair()
        {
            previewPlus = AddPreview("previewPlus", circle: false);
            previewCircle = AddPreview("previewCircle", circle: true);
            refreshers.Add(() =>
            {
                previewPlus.Settings = Current.crosshair;
                previewCircle.Settings = Current.crosshair;
                previewPlus.MarkDirtyRepaint();
                previewCircle.MarkDirtyRepaint();
            });

            BindToggle("showFiringErrorToggle", () => Current.crosshair.showFiringError, v => Current.crosshair.showFiringError = v);
            BindToggle("showMovementErrorToggle", () => Current.crosshair.showMovementError, v => Current.crosshair.showMovementError = v);

            var colorChoices = new List<string>();
            foreach (var preset in ColorPresets) colorChoices.Add(preset.Name);
            colorChoices.Add(CustomChoice);
            BindDropdown("colorPresetField", colorChoices.ToArray(),
                () => FindColorPreset(Current.crosshair.color),
                i =>
                {
                    if (i >= ColorPresets.Length) return; // "Custom": edit the hex instead
                    Color picked = ColorPresets[i].Color;
                    picked.a = Current.crosshair.color.a;
                    Current.crosshair.color = picked;
                });

            var hexField = root.Q<TextField>("colorHexField");
            hexField.isDelayed = true; // commit on Enter/focus loss, not on every keystroke
            hexField.RegisterValueChangedCallback(e =>
            {
                string hex = e.newValue.Trim().TrimStart('#');
                if (hex.Length == 6 && ColorUtility.TryParseHtmlString("#" + hex, out Color parsed))
                {
                    parsed.a = Current.crosshair.color.a;
                    Current.crosshair.color = parsed;
                }
                OnEdited(); // an invalid entry is simply overwritten by the current color
            });
            refreshers.Add(() => hexField.SetValueWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(Current.crosshair.color)));

            BindSlider("opacitySlider", 0.05f, () => Current.crosshair.color.a, v => Current.crosshair.color.a = v);
            BindSlider("outlineThicknessSlider", 0.5f, () => Current.crosshair.outlineThickness, v => Current.crosshair.outlineThickness = v);
            BindSlider("outlineOpacitySlider", 0.05f, () => Current.crosshair.outlineColor.a, v => Current.crosshair.outlineColor.a = v);

            BindSlider("plusLengthSlider", 0.5f, () => Current.crosshair.plusLength, v => Current.crosshair.plusLength = v);
            BindSlider("plusThicknessSlider", 0.5f, () => Current.crosshair.plusThickness, v => Current.crosshair.plusThickness = v);
            BindSlider("plusGapSlider", 0.5f, () => Current.crosshair.plusGap, v => Current.crosshair.plusGap = v);

            BindSlider("circleThicknessSlider", 0.5f, () => Current.crosshair.circleThickness, v => Current.crosshair.circleThickness = v);
            BindToggle("centerDotToggle", () => Current.crosshair.showCenterDot, v => Current.crosshair.showCenterDot = v);
            Slider dotRadius = BindSlider("dotRadiusSlider", 0.5f, () => Current.crosshair.dotRadius, v => Current.crosshair.dotRadius = v);
            refreshers.Add(() => dotRadius.SetEnabled(Current.crosshair.showCenterDot));
        }

        private CrosshairElement AddPreview(string swatchName, bool circle)
        {
            var swatch = root.Q<VisualElement>(swatchName);
            var element = new CrosshairElement();
            swatch.Add(element);
            // Centre it in the swatch; a fixed stand-in radius shows the circle's look.
            swatch.RegisterCallback<GeometryChangedEvent>(_ =>
                element.SetState(swatch.contentRect.size * 0.5f, circle, circle ? ShotgunPreviewRadius : 0f));
            return element;
        }

        private static int FindColorPreset(Color color)
        {
            for (int i = 0; i < ColorPresets.Length; i++)
            {
                Color preset = ColorPresets[i].Color;
                if (Mathf.Abs(preset.r - color.r) < 0.01f && Mathf.Abs(preset.g - color.g) < 0.01f && Mathf.Abs(preset.b - color.b) < 0.01f)
                    return i;
            }
            return ColorPresets.Length; // Custom
        }

        // ---------------------------------------------------------------- cheats

        private void BindCheats()
        {
            var status = root.Q<Label>("cheatsStatusLabel");
            refreshers.Add(() => status.text =
                !MatchRules.CheatsAllowed ? "Cheats are disabled for this match."
                : MatchRules.CanEditCheats ? "Cheats apply to everyone in this match."
                : "Only the host can change cheats.");

            BindCheatToggle("infiniteAmmoToggle", () => MatchRules.InfiniteAmmo, MatchRules.SetInfiniteAmmo);
            BindCheatToggle("alwaysBuyToggle", () => MatchRules.AlwaysBuy, MatchRules.SetAlwaysBuy);
            BindCheatToggle("infiniteCreditsToggle", () => MatchRules.InfiniteCredits, MatchRules.SetInfiniteCredits);
        }

        private void BindCheatToggle(string name, Func<bool> get, Action<bool> set)
        {
            var toggle = root.Q<Toggle>(name);
            // Cheats are match state, not player settings: no save, and the
            // replicated value comes back through MatchRules.Changed.
            toggle.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                RefreshAll();
            });
            refreshers.Add(() =>
            {
                toggle.SetValueWithoutNotify(get());
                toggle.SetEnabled(MatchRules.CanEditCheats);
            });
        }

        // ---------------------------------------------------------------- footer

        private void BindFooter()
        {
            root.Q<Button>("resumeButton").clicked += Close;
            root.Q<Button>("resetTabButton").clicked += ResetCurrentTab;
            root.Q<Button>("quitButton").clicked += QuitGame;
        }

        private void ResetCurrentTab()
        {
            switch (lastTab)
            {
                case Tab.Video:
                    // Window mode and resolution are left alone: they only ever
                    // change through Apply and its confirmation.
                    var defaults = new VideoSettings();
                    Current.video.vSync = defaults.vSync;
                    Current.video.frameRateCap = defaults.frameRateCap;
                    break;
                case Tab.Graphics:
                    Current.graphics = new GraphicsQualitySettings();
                    break;
                case Tab.Crosshair:
                    Current.crosshair = new CrosshairSettings();
                    break;
                case Tab.Cheats:
                    MatchRules.SetInfiniteAmmo(false);
                    MatchRules.SetAlwaysBuy(false);
                    MatchRules.SetInfiniteCredits(false);
                    break;
            }
            OnEdited();
        }

        private void QuitGame()
        {
            Close();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- binding helpers

        private void BindToggle(string name, Func<bool> get, Action<bool> set)
        {
            var toggle = root.Q<Toggle>(name);
            toggle.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                OnEdited();
            });
            refreshers.Add(() => toggle.SetValueWithoutNotify(get()));
        }

        private Slider BindSlider(string name, float step, Func<float> get, Action<float> set)
        {
            var slider = root.Q<Slider>(name);
            slider.RegisterValueChangedCallback(e =>
            {
                float snapped = Mathf.Clamp(Mathf.Round(e.newValue / step) * step, slider.lowValue, slider.highValue);
                set(snapped);
                OnEdited();
            });
            refreshers.Add(() => slider.SetValueWithoutNotify(get()));
            return slider;
        }

        private DropdownField BindDropdown(string name, string[] choices, Func<int> getIndex, Action<int> setIndex, bool applyNow = true)
        {
            var dropdown = root.Q<DropdownField>(name);
            dropdown.choices = new List<string>(choices);
            dropdown.RegisterValueChangedCallback(_ =>
            {
                int index = dropdown.index;
                if (index < 0) return;
                setIndex(index);
                if (applyNow) OnEdited();
                else RefreshAll();
            });
            refreshers.Add(() =>
            {
                int index = Mathf.Clamp(getIndex(), 0, choices.Length - 1);
                dropdown.SetValueWithoutNotify(choices[index]);
            });
            return dropdown;
        }
    }
}
