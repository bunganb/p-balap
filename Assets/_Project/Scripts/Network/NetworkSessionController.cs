using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace PBalap.Network
{
    public enum NetworkSessionState
    {
        Disconnected,
        Initializing,
        CreatingRoom,
        Hosting,
        Failed
    }

    /// <summary>
    /// Creates a Relay allocation, exposes its join code, and starts NGO as Host.
    /// Client joining and player-prefab integration are handled by later issues.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class NetworkSessionController : MonoBehaviour
    {
        private const int MinimumPlayers = 2;

        [Header("Room")]
        [SerializeField, Range(MinimumPlayers, 4)] private int maxPlayers = 4;
        [SerializeField] private string relayConnectionType = "dtls";

        [Header("Temporary Network UI")]
        [SerializeField] private bool showRuntimeUi = true;

        [Header("Scene References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;

        private bool operationInProgress;
        private string joinCode = string.Empty;
        private string lastError = string.Empty;
        private NetworkSessionState state = NetworkSessionState.Disconnected;

        public event Action<NetworkSessionState> StateChanged;
        public event Action<string> JoinCodeChanged;

        public NetworkSessionState State => state;
        public string JoinCode => joinCode;
        public string LastError => lastError;
        public bool IsHost => networkManager != null && networkManager.IsHost;
        public bool IsConnected => networkManager != null && networkManager.IsListening;
        public int ConnectedPlayerCount => networkManager != null && networkManager.IsListening
            ? networkManager.ConnectedClients.Count
            : 0;

        private void Awake()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (unityTransport == null)
            {
                unityTransport = GetComponent<UnityTransport>();
            }
        }

        private void OnEnable()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void OnDisable()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        public async Task<bool> InitializeServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                SetState(NetworkSessionState.Initializing);
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            return AuthenticationService.Instance.IsSignedIn;
        }

        public async Task<bool> CreateRoomAsync()
        {
            if (operationInProgress)
            {
                return false;
            }

            if (networkManager == null || unityTransport == null)
            {
                SetFailure("NetworkManager atau UnityTransport belum terpasang pada scene Network.");
                return false;
            }

            if (networkManager.IsListening)
            {
                SetFailure("Network session sudah berjalan. Tutup session sebelum membuat room baru.");
                return false;
            }

            operationInProgress = true;
            lastError = string.Empty;
            SetJoinCode(string.Empty);

            try
            {
                if (!await InitializeServicesAsync())
                {
                    SetFailure("Unity Authentication gagal melakukan sign-in.");
                    return false;
                }

                SetState(NetworkSessionState.CreatingRoom);

                // Relay counts joining clients; the Host occupies the remaining player slot.
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
                string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                RelayServerEndpoint endpoint = allocation.ServerEndpoints.Find(candidate =>
                    string.Equals(candidate.ConnectionType, relayConnectionType, StringComparison.OrdinalIgnoreCase));

                if (endpoint == null)
                {
                    throw new InvalidOperationException(
                        $"Relay endpoint '{relayConnectionType}' tidak tersedia untuk allocation ini.");
                }

                unityTransport.SetRelayServerData(
                    endpoint.Host,
                    checked((ushort)endpoint.Port),
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    null,
                    endpoint.Secure);

                if (!networkManager.StartHost())
                {
                    SetFailure("Relay berhasil dibuat, tetapi NetworkManager gagal menjalankan Host.");
                    return false;
                }

                SetJoinCode(code);
                SetState(NetworkSessionState.Hosting);
                Debug.Log($"[Network] Host aktif. Join code: {code}");
                return true;
            }
            catch (Exception exception)
            {
                SetFailure($"Gagal membuat room: {exception.Message}");
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                operationInProgress = false;
            }
        }

        private async void CreateRoomFromUi()
        {
            await CreateRoomAsync();
        }

        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[Network] Client connected: {clientId}. Players: {ConnectedPlayerCount}/{maxPlayers}");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"[Network] Client disconnected: {clientId}. Players: {ConnectedPlayerCount}/{maxPlayers}");
        }

        private void SetState(NetworkSessionState nextState)
        {
            state = nextState;
            StateChanged?.Invoke(state);
        }

        private void SetJoinCode(string code)
        {
            joinCode = code ?? string.Empty;
            JoinCodeChanged?.Invoke(joinCode);
        }

        private void SetFailure(string message)
        {
            lastError = message;
            SetState(NetworkSessionState.Failed);
            Debug.LogError($"[Network] {message}");
        }

        private void OnGUI()
        {
            if (!showRuntimeUi)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20f, 20f, 430f, 230f), GUI.skin.box);
            GUILayout.Label("P, Balap! - Network Host");
            GUILayout.Label($"Status: {state}");

            if (!IsConnected && !operationInProgress && GUILayout.Button("Create Room (Host)", GUILayout.Height(36f)))
            {
                CreateRoomFromUi();
            }

            if (operationInProgress)
            {
                GUILayout.Label("Menghubungkan ke Unity Services...");
            }

            if (state == NetworkSessionState.Hosting)
            {
                GUILayout.Label($"Join Code: {joinCode}");
                GUILayout.Label($"Players: {ConnectedPlayerCount}/{maxPlayers}");

                if (GUILayout.Button("Copy Join Code"))
                {
                    GUIUtility.systemCopyBuffer = joinCode;
                }
            }

            if (!string.IsNullOrWhiteSpace(lastError))
            {
                GUILayout.Label(lastError);
            }

            GUILayout.EndArea();
        }
    }
}
