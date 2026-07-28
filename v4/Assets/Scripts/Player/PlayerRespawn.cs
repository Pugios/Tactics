using Unity.Netcode;
using UnityEngine;

namespace Tactics.Player
{
    public class PlayerRespawn : NetworkBehaviour
    {
        [Header("Respawn Settings")]
        [SerializeField] private float outOfBoundsY = -10f;
        [Tooltip("Used only when the scene has no PlayerSpawnPoint markers.")]
        [SerializeField] private Vector3 defaultSpawnPosition = new Vector3(-20f, 17f, 8f);

        // Round-robins every player across the scene's combined spawn point pool
        // so a full 10-player lobby spreads out and a 2-player lobby doesn't stack
        // both players on the same point.
        private static int nextSpawnIndex;
        private int assignedSpawnIndex = -1;

        public Vector3 DefaultSpawnPosition => ResolveSpawnPosition();

        private CharacterController characterController;
        private Vector3 lastGroundedPosition;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            lastGroundedPosition = defaultSpawnPosition;
        }

        private void Start()
        {
            RespawnTo(DefaultSpawnPosition);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) enabled = false;
        }

        private void Update()
        {
            if (characterController.isGrounded)
            {
                lastGroundedPosition = transform.position;
            }

            if (transform.position.y < outOfBoundsY)
            {
                RespawnTo(lastGroundedPosition);
            }
        }

        public void RespawnTo(Vector3 position)
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            
            // CharacterController must be disabled to warp the player
            bool wasEnabled = characterController != null && characterController.enabled;
            if (characterController != null) characterController.enabled = false;
            
            transform.position = position;
            
            if (characterController != null) characterController.enabled = wasEnabled;
            
            Debug.Log($"[PlayerRespawn] Player respawned to {position}");
        }

        private Vector3 ResolveSpawnPosition()
        {
            var points = PlayerSpawnPoint.All;
            if (points.Count == 0) return defaultSpawnPosition;

            if (assignedSpawnIndex < 0) assignedSpawnIndex = nextSpawnIndex++;
            return points[assignedSpawnIndex % points.Count].transform.position;
        }
    }
}
