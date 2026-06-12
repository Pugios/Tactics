using UnityEngine;

namespace ValorantTrainer.Sound
{
    [RequireComponent(typeof(LineRenderer), typeof(SoundEmitter))]
    public class SoundVisualizer : MonoBehaviour
    {
        [SerializeField] private int segments = 100;
        [SerializeField] private Color circleColor = new Color(0.7f, 0.7f, 0.7f, 0.2f); // Faint grey
        
        private LineRenderer lineRenderer;
        private SoundEmitter soundEmitter;

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
            soundEmitter = GetComponent<SoundEmitter>();

            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = segments;
            lineRenderer.startWidth = 0.2f;
            lineRenderer.endWidth = 0.2f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = circleColor;
            lineRenderer.endColor = circleColor;
            
            // Position at feet (assuming origin is center of a 2m capsule)
            lineRenderer.transform.localPosition = new Vector3(0, -1f, 0);
            lineRenderer.transform.localRotation = Quaternion.identity;

            CreatePoints();
        }

        private void CreatePoints()
        {
            float radius = soundEmitter.SoundRadius;
            float angle = 0f;

            for (int i = 0; i < segments; i++)
            {
                float x = Mathf.Sin(Mathf.Deg2Rad * angle) * radius;
                float z = Mathf.Cos(Mathf.Deg2Rad * angle) * radius;

                lineRenderer.SetPosition(i, new Vector3(x, 0, z));

                angle += (360f / segments);
            }
        }

        private void Update()
        {
            lineRenderer.enabled = soundEmitter.IsCurrentlySounding;
        }
    }
}