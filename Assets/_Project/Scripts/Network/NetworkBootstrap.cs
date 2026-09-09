using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace PBalap.Network
{
    /// <summary>Persistent entry point for the networking scene.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private NetworkSessionController sessionController;

        public NetworkManager Manager => networkManager != null ? networkManager : NetworkManager.Singleton;
        public NetworkSessionController SessionController => sessionController;

        private void Awake()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (sessionController == null)
            {
                sessionController = GetComponent<NetworkSessionController>();
            }

            DontDestroyOnLoad(gameObject);
        }
    }
}
