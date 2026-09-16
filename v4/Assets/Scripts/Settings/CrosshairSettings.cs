using System;
using UnityEngine;

namespace Tactics.Settings
{
    /// <summary>
    /// How the crosshair looks. The drawing code reads this live every repaint,
    /// so a change in the settings menu shows up on the very next frame.
    /// </summary>
    [Serializable]
    public class CrosshairSettings
    {
        // Slider ranges in the settings menu; Sanitize clamps a hand-edited file to them.
        public const float MaxOutlineThickness = 4f;
        public const float MaxLineLength = 20f;
        public const float MaxLineThickness = 10f;
        public const float MaxGap = 20f;
        public const float MaxDotRadius = 6f;

        public Color color = Color.white;
        public Color outlineColor = new Color(0f, 0f, 0f, 0.8f);
        public float outlineThickness = 1f;

        [Header("Plus (single-bullet weapons)")]
        public float plusLength = 6f;
        public float plusThickness = 2f;
        public float plusGap = 3f;

        [Header("Spread")]
        [Tooltip("Grow with spray: the cone widening as consecutive shots are fired.")]
        public bool showFiringError = true;
        [Tooltip("Grow with movement: the flat penalty for running, walking, crouch-walking or being airborne.")]
        public bool showMovementError = true;

        [Header("Circle (shotguns)")]
        public float circleThickness = 2f;
        public bool showCenterDot = true;
        public float dotRadius = 1.5f;

        /// <summary>
        /// Whether the "+" follows the spread at all. With both errors hidden it
        /// keeps a constant size; the shotgun circle always shows its footprint.
        /// </summary>
        public bool PlusShowsSpread => showFiringError || showMovementError;

        public void Sanitize()
        {
            color = ClampColor(color);
            outlineColor = ClampColor(outlineColor);
            outlineThickness = Clamp(outlineThickness, 0f, MaxOutlineThickness, 1f);
            plusLength = Clamp(plusLength, 0f, MaxLineLength, 6f);
            plusThickness = Clamp(plusThickness, 0.5f, MaxLineThickness, 2f);
            plusGap = Clamp(plusGap, 0f, MaxGap, 3f);
            circleThickness = Clamp(circleThickness, 0.5f, MaxLineThickness, 2f);
            dotRadius = Clamp(dotRadius, 0.5f, MaxDotRadius, 1.5f);
        }

        // NaN survives Mathf.Clamp, so a corrupt file falls back to the default.
        private static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) ? fallback : Mathf.Clamp(value, min, max);

        private static Color ClampColor(Color c) =>
            new Color(Clamp(c.r, 0f, 1f, 1f), Clamp(c.g, 0f, 1f, 1f), Clamp(c.b, 0f, 1f, 1f), Clamp(c.a, 0f, 1f, 1f));
    }
}
