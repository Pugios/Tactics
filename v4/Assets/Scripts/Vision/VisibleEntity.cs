using UnityEngine;

namespace ValorantTrainer.Vision
{
    public class VisibleEntity : MonoBehaviour
    {
        [SerializeField] private GameObject visualRoot;
        private bool isVisible = true;

        private Renderer[] renderers;

        private void Awake()
        {
            if (visualRoot == null) visualRoot = gameObject;
            renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        }

        public void SetVisible(bool visible)
        {
            if (isVisible == visible) return;
            
            isVisible = visible;
            foreach (var r in renderers)
            {
                if (r != null) r.enabled = visible;
            }
        }
}
}
