using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Vision
{
    public class VisibleEntity : MonoBehaviour
    {
        private static readonly List<VisibleEntity> Instances = new List<VisibleEntity>();

        public static IReadOnlyList<VisibleEntity> All => Instances;

        [SerializeField] private GameObject visualRoot;
        [SerializeField] private Transform feetTransform;
        [SerializeField] private float heightOverride = -1f;
        [SerializeField] private float radiusOverride = -1f;
        [SerializeField] private bool alwaysVisible = false;

        private bool isVisible;
        private Renderer[] renderers;

        public Vector3 FeetPosition => feetTransform != null ? feetTransform.position : transform.position;
        public float HeightOverride => heightOverride;
        public float RadiusOverride => radiusOverride;

        /// <summary>
        /// Bypasses the vision query entirely — always rendered. Used for the
        /// local player's own entity (you must always see yourself) and, later,
        /// teammates once a team system exists.
        /// </summary>
        public bool AlwaysVisible => alwaysVisible;

        public void SetHeightOverride(float value) => heightOverride = value;
        public void SetAlwaysVisible(bool value) => alwaysVisible = value;

        private void OnEnable()
        {
            if (!Instances.Contains(this))
                Instances.Add(this);

            RefreshRenderers();
            isVisible = true;
            SetVisible(false);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }

        public void RefreshRenderers()
        {
            if (visualRoot == null)
                visualRoot = gameObject;

            renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        }

        public void SetVisible(bool visible)
        {
            if (renderers == null || renderers.Length == 0)
                RefreshRenderers();

            if (isVisible == visible)
                return;

            isVisible = visible;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = isVisible;
            }
        }
    }
}
