using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Tactics.Combat
{
    /// <summary>
    /// Server-side: revives this entity a few seconds after it dies. For practice
    /// targets — players respawn through PlayerMovementNetwork instead.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class AutoRevive : NetworkBehaviour
    {
        [SerializeField] private float reviveDelay = 3f;

        private Health health;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            health = GetComponent<Health>();
            health.OnDeath += HandleDeath;
        }

        public override void OnNetworkDespawn()
        {
            if (health != null) health.OnDeath -= HandleDeath;
        }

        private void HandleDeath()
        {
            StartCoroutine(ReviveAfterDelay());
        }

        private IEnumerator ReviveAfterDelay()
        {
            yield return new WaitForSeconds(reviveDelay);
            if (health != null) health.ServerRevive();
        }
    }
}
