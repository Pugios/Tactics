using UnityEngine;
using Tactics.Vision;

namespace Tactics.Player
{
    /// <summary>
    /// Drives the crouch presentation: shrinks the player's visual mesh and the
    /// capsule fed to the CPU-layer entity-visibility check. Runs for every
    /// instance (owner and remote copies alike) since every peer needs to see
    /// every player's crouch pose and evaluate their shrunk capsule.
    /// </summary>
    [RequireComponent(typeof(PlayerMovementNetwork))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerCrouchVisuals : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private VisibleEntity visibleEntity;
        [SerializeField] private float standingVisualScaleY = 1f;
        [SerializeField] private float crouchVisualScaleY = 0.6f;
        [SerializeField] private float crouchVisibilityHeight = 1.2f;

        private PlayerMovementNetwork movementNetwork;
        private CharacterController characterController;
        private Vector3 standingLocalPosition;

        private void Awake()
        {
            movementNetwork = GetComponent<PlayerMovementNetwork>();
            characterController = GetComponent<CharacterController>();
            if (visualRoot != null) standingLocalPosition = visualRoot.localPosition;
        }

        private void LateUpdate()
        {
            bool crouching = movementNetwork.CurrentIsCrouching;

            if (visualRoot != null)
            {
                float scaleY = crouching ? crouchVisualScaleY : standingVisualScaleY;
                visualRoot.localScale = new Vector3(visualRoot.localScale.x, scaleY, visualRoot.localScale.z);

                float targetLocalY = standingLocalPosition.y;
                if (crouching)
                {
                    // Anchor the shrunk mesh's bottom to the CharacterController's
                    // actual ground contact point, not to the standing mesh's own
                    // (already elevated) bottom edge — otherwise it stays floating.
                    float groundLocalY = characterController.center.y - characterController.height * 0.5f;
                    targetLocalY = groundLocalY + scaleY * 0.5f;
                }
                visualRoot.localPosition = new Vector3(standingLocalPosition.x, targetLocalY, standingLocalPosition.z);
            }

            if (visibleEntity != null)
                visibleEntity.SetHeightOverride(crouching ? crouchVisibilityHeight : -1f);
        }
    }
}
