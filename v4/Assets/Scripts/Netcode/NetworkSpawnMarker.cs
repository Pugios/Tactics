using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Tactics.Netcode
{
    /// <summary>
    /// Scene marker that server-spawns a network prefab at its own position when
    /// the session starts. Used instead of placing NetworkObjects directly in the
    /// scene because despawn-destroying in-scene network objects is discouraged
    /// by Netcode (late-join bookkeeping); dynamic spawning also serves future
    /// round resets and death drops. Used for the spike pickup and the target
    /// dummies.
    /// </summary>
    public class NetworkSpawnMarker : MonoBehaviour
    {
        [FormerlySerializedAs("spikePickupPrefab")]
        [SerializeField] private GameObject prefab;

        private void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (nm.IsServer) Spawn();
            else nm.OnServerStarted += Spawn;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnServerStarted -= Spawn;
        }

        private void Spawn()
        {
            if (prefab == null) return;
            GameObject obj = Instantiate(prefab, transform.position, transform.rotation);
            obj.GetComponent<NetworkObject>().Spawn();
        }
    }
}
