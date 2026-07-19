using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Tactics.Netcode
{
    [RequireComponent(typeof(UnityTransport))]
    public class ConnectionBootstrap : MonoBehaviour
    {
        [SerializeField] private ushort port = 7777;

        private string ipAddress = "127.0.0.1";
        private UnityTransport transport;

        private void Awake()
        {
            transport = GetComponent<UnityTransport>();
        }

        private void OnGUI()
        {
            if (NetworkManager.Singleton == null) return;

            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            {
                string role = NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client";
                GUI.Label(new Rect(10, 10, 300, 20), $"{role} — connected clients: {NetworkManager.Singleton.ConnectedClients.Count}");
                return;
            }

            GUI.Label(new Rect(10, 10, 40, 20), "IP:");
            ipAddress = GUI.TextField(new Rect(50, 10, 120, 20), ipAddress);

            if (GUI.Button(new Rect(10, 35, 80, 25), "Host"))
            {
                transport.SetConnectionData(ipAddress, port);
                NetworkManager.Singleton.StartHost();
            }

            if (GUI.Button(new Rect(100, 35, 80, 25), "Join"))
            {
                transport.SetConnectionData(ipAddress, port);
                NetworkManager.Singleton.StartClient();
            }
        }
    }
}
