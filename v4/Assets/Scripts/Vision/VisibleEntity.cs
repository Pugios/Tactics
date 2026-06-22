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

        private bool isVisible;
        private Renderer[] renderers;

        public Vector3 FeetPosition => feetTransform != null ? feetTransform.position : transform.position;
        public float HeightOverride => heightOverride;
        public float RadiusOverride => radiusOverride;

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
