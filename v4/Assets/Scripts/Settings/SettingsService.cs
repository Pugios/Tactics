using System;
using System.IO;
using UnityEngine;

namespace Tactics.Settings
{
    /// <summary>
    /// Owns the player's <see cref="GameSettings"/>: loads them from disk before
    /// the first scene, applies them, and saves them back. Gameplay and UI read
    /// <see cref="Current"/>; the settings menu is only one editor of it and
    /// calls <see cref="NotifyChanged"/> after every edit so live readers (the
    /// crosshair) and the renderer pick the change up immediately.
    /// </summary>
    public static class SettingsService
    {
        private const string FileName = "settings.json";

        private static GameSettings current;

        /// <summary>Raised after any setting changed and was applied.</summary>
        public static event Action Changed;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static GameSettings Current => current ??= Load();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            current = null;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            current = Load();
            SettingsApplier.ApplyAll(current);
        }

        /// <summary>Re-applies everything except the display mode/resolution, which only changes through <see cref="SettingsApplier.ApplyDisplay"/>.</summary>
        public static void NotifyChanged()
        {
            SettingsApplier.ApplyFrameRate(Current.video);
            SettingsApplier.ApplyGraphics(Current.graphics);
            Changed?.Invoke();
        }

        /// <summary>Swaps in a whole new settings object (e.g. Reset to Defaults) and applies it.</summary>
        public static void Replace(GameSettings settings)
        {
            current = settings ?? new GameSettings();
            current.Sanitize();
            NotifyChanged();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, Current.ToJson());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Settings] Could not save {FilePath}: {e.Message}");
            }
        }

        private static GameSettings Load()
        {
            string json = null;
            try
            {
                if (File.Exists(FilePath)) json = File.ReadAllText(FilePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Settings] Could not read {FilePath}, using defaults: {e.Message}");
            }
            return GameSettings.FromJson(json);
        }
    }
}
