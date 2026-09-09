using System;
using System.Collections.Generic;
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
        Connecting,
        Joined,
        Failed
    }

    /// <summary>
    /// Creates or joins a Relay room and starts NGO as Host or Client.
    /// Player-prefab integration is handled by a later issue.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class NetworkSessionController : MonoBehaviour
    {
        private const int MinimumPlayers = 2;
        private const int ClientConnectTimeoutMilliseconds = 15000;

        [Header("Room")]
        [SerializeField, Range(MinimumPlayers, 4)] private int maxPlayers = 4;
        [SerializeField] private string relayConnectionType = "dtls";

        [Header("Temporary Network UI")]
        [SerializeField] private bool showRuntimeUi = true;

        [Header("Scene References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;

        private bool operationInProgress;
        private bool joinedAsClient;
        private bool applicationQuitting;
        private string joinCode = string.Empty;
        private string joinCodeInput = string.Empty;
        private string lastError = string.Empty;
        private NetworkSessionState state = NetworkSessionState.Disconnected;
        private TaskCompletionSource<bool> clientConnectionCompletion;

        public event Action<NetworkSessionState> StateChanged;
        public event Action<string> JoinCodeChanged;

        public NetworkSessionState State => state;
        public string JoinCode => joinCode;
        public string JoinCodeInput
        {
            get => joinCodeInput;
            set => joinCodeInput = NormalizeJoinCode(value);
        }
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

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
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
                RelayServerEndpoint endpoint = FindRelayEndpoint(allocation.ServerEndpoints);

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

        public async Task<bool> JoinRoomAsync(string code)
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
                SetFailure("Network session sudah berjalan. Tutup session sebelum join room lain.");
                return false;
            }

            string normalizedCode = NormalizeJoinCode(code);
            if (!IsJoinCodeValid(normalizedCode))
            {
                SetFailure("Join code kosong atau formatnya tidak valid.");
                return false;
            }

            operationInProgress = true;
            joinedAsClient = false;
            lastError = string.Empty;
            JoinCodeInput = normalizedCode;

            try
            {
                if (!await InitializeServicesAsync())
                {
                    SetFailure("Unity Authentication gagal melakukan sign-in.");
                    return false;
                }

                SetState(NetworkSessionState.Connecting);

                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(normalizedCode);
                RelayServerEndpoint endpoint = FindRelayEndpoint(allocation.ServerEndpoints);

                unityTransport.SetRelayServerData(
                    endpoint.Host,
                    checked((ushort)endpoint.Port),
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    allocation.HostConnectionData,
                    endpoint.Secure);

                clientConnectionCompletion = new TaskCompletionSource<bool>();
                if (!networkManager.StartClient())
                {
                    SetFailure("Relay ditemukan, tetapi NetworkManager gagal menjalankan Client.");
                    return false;
                }

                Task completedTask = await Task.WhenAny(
                    clientConnectionCompletion.Task,
                    Task.Delay(ClientConnectTimeoutMilliseconds));

                bool connected = completedTask == clientConnectionCompletion.Task
                    && await clientConnectionCompletion.Task;

                if (!connected)
                {
                    string disconnectReason = networkManager.DisconnectReason;
                    if (networkManager.IsListening)
                    {
                        networkManager.Shutdown();
                    }

                    SetFailure(string.IsNullOrWhiteSpace(disconnectReason)
                        ? "Gagal terhubung. Room mungkin penuh atau Host sudah keluar."
                        : $"Gagal terhubung: {disconnectReason}");
                    return false;
                }

                joinedAsClient = true;
                SetJoinCode(normalizedCode);
                SetState(NetworkSessionState.Joined);
                Debug.Log($"[Network] Client bergabung ke room {normalizedCode}.");
                return true;
            }
            catch (RelayServiceException exception)
            {
                SetFailure(GetRelayJoinErrorMessage(exception));
                Debug.LogException(exception);
                return false;
            }
            catch (Exception exception)
            {
                SetFailure($"Gagal join room: {exception.Message}");
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                clientConnectionCompletion = null;
                operationInProgress = false;
            }
        }

        private async void CreateRoomFromUi()
        {
            await CreateRoomAsync();
        }

        private async void JoinRoomFromUi()
        {
            await JoinRoomAsync(joinCodeInput);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (networkManager != null
                && clientId == networkManager.LocalClientId
                && networkManager.IsClient
                && !networkManager.IsHost)
            {
                clientConnectionCompletion?.TrySetResult(true);
            }

            Debug.Log($"[Network] Client connected: {clientId}. Players: {ConnectedPlayerCount}/{maxPlayers}");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            bool localClientDisconnected = networkManager != null
                && clientId == networkManager.LocalClientId
                && !networkManager.IsHost;

            if (localClientDisconnected)
            {
                clientConnectionCompletion?.TrySetResult(false);

                if (joinedAsClient && !applicationQuitting)
                {
                    joinedAsClient = false;
                    string reason = networkManager.DisconnectReason;
                    SetFailure(string.IsNullOrWhiteSpace(reason)
                        ? "Koneksi terputus karena Host keluar atau room ditutup."
                        : $"Koneksi terputus: {reason}");
                }
            }

            Debug.Log($"[Network] Client disconnected: {clientId}. Players: {ConnectedPlayerCount}/{maxPlayers}");
        }

        private RelayServerEndpoint FindRelayEndpoint(List<RelayServerEndpoint> endpoints)
        {
            RelayServerEndpoint endpoint = endpoints?.Find(candidate =>
                string.Equals(candidate.ConnectionType, relayConnectionType, StringComparison.OrdinalIgnoreCase));

            if (endpoint == null)
            {
                throw new InvalidOperationException(
                    $"Relay endpoint '{relayConnectionType}' tidak tersedia untuk allocation ini.");
            }

            return endpoint;
        }

        private static string NormalizeJoinCode(string code)
        {
            return string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToUpperInvariant();
        }

        private static bool IsJoinCodeValid(string code)
        {
            if (code.Length < 4 || code.Length > 12)
            {
                return false;
            }

            foreach (char character in code)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetRelayJoinErrorMessage(RelayServiceException exception)
        {
            switch (exception.Reason)
            {
                case RelayExceptionReason.JoinCodeNotFound:
                case RelayExceptionReason.AllocationNotFound:
                case RelayExceptionReason.EntityNotFound:
                case RelayExceptionReason.Gone:
                    return "Join code tidak ditemukan atau room sudah ditutup.";

                case RelayExceptionReason.Conflict:
                    return "Room sudah penuh atau tidak lagi menerima pemain.";

                case RelayExceptionReason.InvalidArgument:
                case RelayExceptionReason.InvalidRequest:
                    return "Format join code tidak valid.";

                case RelayExceptionReason.NetworkError:
                case RelayExceptionReason.RequestTimeOut:
                case RelayExceptionReason.ServiceUnavailable:
                case RelayExceptionReason.GatewayTimeout:
                    return "Tidak dapat menghubungi Relay. Periksa koneksi internet lalu coba lagi.";

                default:
                    return $"Gagal join room: {exception.Message}";
            }
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

            GUILayout.BeginArea(new Rect(20f, 20f, 430f, 340f), GUI.skin.box);
            GUILayout.Label("P, Balap! - Network Room");
            GUILayout.Label($"Status: {state}");

            if (!IsConnected && !operationInProgress)
            {
                if (GUILayout.Button("Create Room (Host)", GUILayout.Height(36f)))
                {
                    CreateRoomFromUi();
                }

                GUILayout.Space(12f);
                GUILayout.Label("Join Room dari laptop lain:");
                joinCodeInput = GUILayout.TextField(joinCodeInput, 12).ToUpperInvariant();

                if (GUILayout.Button("Join Room (Client)", GUILayout.Height(36f)))
                {
                    JoinRoomFromUi();
                }
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

            if (state == NetworkSessionState.Joined)
            {
                GUILayout.Label($"Terhubung sebagai Client ke room: {joinCode}");
                GUILayout.Label("Menunggu integrasi Player Prefab pada issue berikutnya.");
            }

            if (!string.IsNullOrWhiteSpace(lastError))
            {
                GUILayout.Label(lastError);
            }

            GUILayout.EndArea();
        }
    }
}
