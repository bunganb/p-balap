using Unity.Netcode;
using UnityEngine;

namespace PBalap.Network
{
    /// <summary>Entry point for the network bootstrap prefab.</summary>
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;

        public NetworkManager Manager => networkManager != null ? networkManager : NetworkManager.Singleton;
    }
}
