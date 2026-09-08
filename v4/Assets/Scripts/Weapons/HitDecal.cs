using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tactics.Weapons
{
    /// <summary>
    /// Lives for <see cref="lifetime"/> seconds, fading its DecalProjector out
    /// over the last <see cref="fadeDuration"/> seconds before self-destructing.
    /// </summary>
    [RequireComponent(typeof(DecalProjector))]
    public class HitDecal : MonoBehaviour
    {
        [SerializeField] private float lifetime = 10f;
        [SerializeField] private float fadeDuration = 1f;

        private DecalProjector projector;
        private float spawnTime;

        private void Awake()
        {
            projector = GetComponent<DecalProjector>();
        }

        /// <summary>Shortens (or extends) this instance's life; the fade still
        /// runs over the final stretch but never longer than the life itself.</summary>
        public void SetLifetime(float seconds)
        {
            lifetime = seconds;
            fadeDuration = Mathf.Min(fadeDuration, seconds);
        }

        private void Start()
        {
            spawnTime = Time.time;
        }

        private void Update()
        {
            float age = Time.time - spawnTime;
            float fadeStart = lifetime - fadeDuration;

            if (age >= fadeStart)
            {
                float fadeT = fadeDuration > 0f ? Mathf.Clamp01((age - fadeStart) / fadeDuration) : 1f;
                projector.fadeFactor = 1f - fadeT;
            }

            if (age >= lifetime) Destroy(gameObject);
        }
    }
}
