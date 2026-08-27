using UnityEngine;

namespace Tactics.Weapons
{
    /// <summary>Brief line-renderer flash from shooter to impact point.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class ShotTracer : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.1f;

        private LineRenderer line;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
        }

        public void Setup(Vector3 origin, Vector3 point)
        {
            line.positionCount = 2;
            line.SetPosition(0, origin);
            line.SetPosition(1, point);
            Destroy(gameObject, lifetime);
        }
    }
}
