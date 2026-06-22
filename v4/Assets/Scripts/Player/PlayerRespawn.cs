using UnityEngine;

namespace Tactics.Player
{
    public class PlayerRespawn : MonoBehaviour
    {
        [Header("Respawn Settings")]
        [SerializeField] private float outOfBoundsY = -10f;
        [SerializeField] private Vector3 defaultSpawnPosition = new Vector3(-20f, 17f, 8f);
        
        private CharacterController characterController;
        private Vector3 lastGroundedPosition;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            lastGroundedPosition = defaultSpawnPosition;
        }

        private void Start()
        {
            RespawnTo(defaultSpawnPosition);
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

        public void SetDefaultSpawn(Vector3 position)
        {
            defaultSpawnPosition = position;
        }
    }
}
