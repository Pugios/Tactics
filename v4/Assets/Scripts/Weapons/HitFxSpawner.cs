using UnityEngine;

namespace Tactics.Weapons
{
    /// <summary>
    /// Purely cosmetic, client-local spawner for shot feedback: a brief tracer
    /// line and a persistent decal at the impact point. Every peer receives the
    /// same server-resolved impact via <see cref="WeaponController"/>'s
    /// ShotImpactClientRpc and calls into this to draw it — nothing here is
    /// networked itself.
    /// </summary>
    public class HitFxSpawner : MonoBehaviour
    {
        public static HitFxSpawner Instance { get; private set; }

        // The decal prefabs' near clip plane sits exactly at the projector's
        // pivot (offset.z - size.z/2 == 0), so spawning flush on the surface
        // leaves zero margin and the surface flickers in/out of the projection
        // volume from normal/mesh floating-point noise. Pulling the pivot back
        // along the outward normal gives it breathing room.
        private const float SurfaceClearance = 0.03f;

        [SerializeField] private ShotTracer tracerPrefab;
        [SerializeField] private HitDecal environmentDecalPrefab;
        [SerializeField] private HitDecal enemyDecalPrefab;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void SpawnImpact(Vector3 origin, Vector3 point, Vector3 decalDirection, bool isEnemyHit, Transform target)
        {
            if (tracerPrefab != null)
            {
                ShotTracer tracer = Instantiate(tracerPrefab);
                tracer.Setup(origin, point);
            }

            HitDecal decalPrefab = isEnemyHit ? enemyDecalPrefab : environmentDecalPrefab;
            if (decalPrefab == null) return;

            Quaternion rotation = Quaternion.identity;
            Vector3 spawnPoint = point;
            if (decalDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 forward = decalDirection.normalized;
                // LookRotation's up hint must not be parallel to forward or the
                // basis degenerates into an arbitrary, unstable roll. Vector3.up
                // works for wall decals but is parallel to straight-down ground
                // decals, so fall back to Vector3.forward only in that case.
                Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                rotation = Quaternion.LookRotation(forward, up);
                spawnPoint = point - forward * SurfaceClearance;
            }
            HitDecal decal = Instantiate(decalPrefab, spawnPoint, rotation);

            if (isEnemyHit && target != null)
            {
                decal.transform.SetParent(target, worldPositionStays: true);
            }
        }
    }
}
