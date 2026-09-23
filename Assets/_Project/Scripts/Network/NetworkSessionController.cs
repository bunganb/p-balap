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
        Disconnecting,
        Failed
    }

    /// <summary>
    /// Creates or joins a Relay room, assigns player spawn slots, and gates race start.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class NetworkSessionController : MonoBehaviour
    {
        private const int MinimumPlayers = 2;
        private const int ClientConnectTimeoutMilliseconds = 15000;
        private const float KartSnapshotRefreshInterval = 0.25f;
        private static readonly Vector3[] SpawnPositions =
        {
            new Vector3(-2.84617305f, 0.567728043f, 26.8396702f),
            new Vector3(-0.170000002f, 0.567728043f, 25.1200008f),
            new Vector3(-5.38000011f, 0.567728043f, 24.2700005f),
            new Vector3(-3.25999999f, 0.567728043f, 21.6200008f)
        };

        [Header("Room")]
        [SerializeField, Range(MinimumPlayers, 4)] private int maxPlayers = 4;
        [SerializeField] private string relayConnectionType = "dtls";

        [Header("Temporary Network UI")]
        [SerializeField] private bool showRuntimeUi = true;
        [SerializeField, Range(30, 144)] private int targetFrameRate = 60;

        [Header("Scene References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;

        private bool operationInProgress;
        private bool joinedAsClient;
        private bool hostingSession;
        private bool intentionalShutdown;
        private bool applicationQuitting;
        private bool raceStarted;
        private string joinCode = string.Empty;
        private string joinCodeInput = string.Empty;
        private string lastError = string.Empty;
        private string latestNotice = "Belum ada aktivitas network.";
        private NetworkSessionState state = NetworkSessionState.Disconnected;
        private TaskCompletionSource<bool> clientConnectionCompletion;
        private readonly List<ulong> connectedClientIds = new List<ulong>();
        private readonly List<string> activityMessages = new List<string>();
        private readonly List<string> kartSnapshotLabels = new List<string>();
        private readonly Dictionary<ulong, int> spawnSlotByClientId = new Dictionary<ulong, int>();
        private float nextKartSnapshotRefreshTime;

        public event Action<NetworkSessionState> StateChanged;
        public event Action<string> JoinCodeChanged;
        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action<string> NoticeChanged;
        public event Action ReturnToMenuRequested;

        public NetworkSessionState State => state;
        public string JoinCode => joinCode;
        public string JoinCodeInput
        {
            get => joinCodeInput;
            set => joinCodeInput = NormalizeJoinCode(value);
        }
        public string LastError => lastError;
        public string LatestNotice => latestNotice;
        public IReadOnlyList<ulong> ConnectedClientIds => connectedClientIds;
        public IReadOnlyList<string> ActivityMessages => activityMessages;
        public bool IsHost => networkManager != null && networkManager.IsHost;
        public bool IsConnected => networkManager != null && networkManager.IsListening;
        public bool IsRaceStarted => raceStarted;
        public int ConnectedPlayerCount => networkManager != null && networkManager.IsListening
            ? networkManager.ConnectedClients.Count
            : 0;

        private void Awake()
        {
            Application.targetFrameRate = targetFrameRate;

            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (unityTransport == null)
            {
                unityTransport = GetComponent<UnityTransport>();
            }

            if (networkManager != null)
            {
                networkManager.NetworkConfig.ConnectionApproval = true;
                networkManager.ConnectionApprovalCallback = ApproveConnection;
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

        private void OnDestroy()
        {
            if (networkManager != null && networkManager.ConnectionApprovalCallback == ApproveConnection)
            {
                networkManager.ConnectionApprovalCallback = null;
            }
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
            hostingSession = false;
            joinedAsClient = false;
            lastError = string.Empty;
            connectedClientIds.Clear();
            spawnSlotByClientId.Clear();
            activityMessages.Clear();
            raceStarted = false;
            SetJoinCode(string.Empty);
            SetNotice("Membuat room Relay...");

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

                // StartHost invokes the local connected callback synchronously.
                hostingSession = true;
                if (!networkManager.StartHost())
                {
                    hostingSession = false;
                    SetFailure("Relay berhasil dibuat, tetapi NetworkManager gagal menjalankan Host.");
                    return false;
                }

                SetJoinCode(code);
                SetState(NetworkSessionState.Hosting);
                SetNotice($"Room berhasil dibuat. Menunggu Client (1/{maxPlayers}).");
                Debug.Log($"[Network] Host aktif. Join code: {code}");
                return true;
            }
            catch (Exception exception)
            {
                hostingSession = false;
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
            hostingSession = false;
            joinedAsClient = false;
            lastError = string.Empty;
            connectedClientIds.Clear();
            activityMessages.Clear();
            raceStarted = false;
            JoinCodeInput = normalizedCode;
            SetNotice($"Mencari room {normalizedCode}...");

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
                SetNotice("Berhasil terhubung ke Host.");
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

        public bool LeaveRoom()
        {
            if (networkManager == null || !networkManager.IsListening || hostingSession)
            {
                SetFailure("Tidak ada koneksi Client yang dapat ditinggalkan.");
                return false;
            }

            ShutdownSession("Anda keluar dari room.");
            return true;
        }

        public bool StartRace()
        {
            if (networkManager == null || !networkManager.IsHost || !hostingSession)
            {
                SetNotice("Hanya Host yang dapat memulai balapan.");
                return false;
            }

            if (raceStarted)
            {
                SetNotice("Balapan sudah dimulai.");
                return false;
            }

            if (ConnectedPlayerCount < MinimumPlayers)
            {
                SetNotice($"Balapan membutuhkan minimal {MinimumPlayers} pemain.");
                return false;
            }

            List<NetworkKartPlayer> playerKarts = new List<NetworkKartPlayer>();
            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                NetworkKartPlayer kart = client.PlayerObject == null
                    ? null
                    : client.PlayerObject.GetComponent<NetworkKartPlayer>();
                if (kart == null)
                {
                    SetNotice($"Player object Client {client.ClientId} belum siap sebagai kart network.");
                    return false;
                }

                playerKarts.Add(kart);
            }

            raceStarted = true;
            lastError = string.Empty;
            foreach (NetworkKartPlayer kart in playerKarts)
            {
                kart.SetCanDriveOnServer(true);
            }

            SetNotice($"Balapan dimulai untuk {ConnectedPlayerCount} pemain.");
            Debug.Log($"[Network] Race started by Host with {ConnectedPlayerCount} players.");
            return true;
        }

        public bool CloseRoom()
        {
            if (networkManager == null || !networkManager.IsListening || !hostingSession)
            {
                SetFailure("Tidak ada room Host yang dapat ditutup.");
                return false;
            }

            ShutdownSession("Room ditutup. Anda dapat membuat room baru.");
            return true;
        }

        private void ShutdownSession(string notice)
        {
            intentionalShutdown = true;
            operationInProgress = false;
            SetState(NetworkSessionState.Disconnecting);
            clientConnectionCompletion?.TrySetResult(false);

            try
            {
                networkManager.Shutdown();
            }
            finally
            {
                connectedClientIds.Clear();
                spawnSlotByClientId.Clear();
                joinedAsClient = false;
                hostingSession = false;
                raceStarted = false;
                lastError = string.Empty;
                SetJoinCode(string.Empty);
                SetState(NetworkSessionState.Disconnected);
                SetNotice(notice);
                intentionalShutdown = false;
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
            if (!connectedClientIds.Contains(clientId))
            {
                connectedClientIds.Add(clientId);
            }

            if (networkManager != null
                && clientId == networkManager.LocalClientId
                && networkManager.IsClient
                && !hostingSession)
            {
                clientConnectionCompletion?.TrySetResult(true);
                SetNotice("Koneksi ke Host dikonfirmasi oleh NGO.");
            }
            else if (hostingSession && networkManager != null && clientId != networkManager.LocalClientId)
            {
                SetNotice($"Client {clientId} bergabung. Players: {ConnectedPlayerCount}/{maxPlayers}.");
            }
            else if (hostingSession)
            {
                SetNotice("Host aktif dan siap menerima Client.");
            }

            ClientConnected?.Invoke(clientId);
            Debug.Log($"[Network] Client connected: {clientId}. Players: {ConnectedPlayerCount}/{maxPlayers}");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            connectedClientIds.Remove(clientId);
            spawnSlotByClientId.Remove(clientId);

            bool localClientDisconnected = networkManager != null
                && clientId == networkManager.LocalClientId
                && !hostingSession;

            if (localClientDisconnected)
            {
                clientConnectionCompletion?.TrySetResult(false);

                if (joinedAsClient && !intentionalShutdown && !applicationQuitting)
                {
                    joinedAsClient = false;
                    SetJoinCode(string.Empty);
                    string reason = networkManager.DisconnectReason;
                    SetFailure(string.IsNullOrWhiteSpace(reason)
                        ? "Koneksi terputus karena Host keluar atau room ditutup."
                        : $"Koneksi terputus: {reason}");
                    SetNotice("Host keluar. Kembali ke menu Network.");
                    ReturnToMenuRequested?.Invoke();
                }
            }
            else if (hostingSession && !intentionalShutdown && !applicationQuitting)
            {
                SetNotice($"Client {clientId} keluar. Players: {ConnectedPlayerCount}/{maxPlayers}.");
            }

            ClientDisconnected?.Invoke(clientId);
            Debug.Log($"[Network] Client disconnected: {clientId}. Players: {ConnectedPlayerCount}/{maxPlayers}");
        }

        private void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = false;
            response.CreatePlayerObject = false;
            response.PlayerPrefabHash = null;
            response.Position = null;
            response.Rotation = null;

            if (raceStarted)
            {
                response.Approved = false;
                response.Reason = "Balapan sudah dimulai.";
                return;
            }

            int spawnSlot = FindAvailableSpawnSlot();
            if (spawnSlot < 0 || spawnSlotByClientId.Count >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "Room sudah penuh.";
                return;
            }

            spawnSlotByClientId[request.ClientNetworkId] = spawnSlot;
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = SpawnPositions[spawnSlot];
            response.Rotation = Quaternion.identity;
            response.Reason = string.Empty;

            Debug.Log(
                $"[Network] Approved Client {request.ClientNetworkId} at spawn slot {spawnSlot + 1}: "
                + $"{SpawnPositions[spawnSlot]}.");
        }

        private int FindAvailableSpawnSlot()
        {
            for (int slot = 0; slot < Mathf.Min(maxPlayers, SpawnPositions.Length); slot++)
            {
                if (!spawnSlotByClientId.ContainsValue(slot))
                {
                    return slot;
                }
            }

            return -1;
        }

        private bool IsLocalKartAllowedToDrive()
        {
            if (networkManager == null
                || !networkManager.IsListening
                || networkManager.LocalClient == null
                || networkManager.LocalClient.PlayerObject == null)
            {
                return false;
            }

            NetworkKartPlayer kart = networkManager.LocalClient.PlayerObject.GetComponent<NetworkKartPlayer>();
            return kart != null && kart.CanDrive;
        }

        private void DrawSpawnedKartSnapshots()
        {
            if (networkManager == null || !networkManager.IsListening || networkManager.SpawnManager == null)
            {
                kartSnapshotLabels.Clear();
                return;
            }

            GUILayout.Space(8f);
            GUILayout.Label("Kart network (posisi snapshot):");

            if (Event.current.type == EventType.Layout
                && Time.unscaledTime >= nextKartSnapshotRefreshTime)
            {
                nextKartSnapshotRefreshTime = Time.unscaledTime + KartSnapshotRefreshInterval;
                kartSnapshotLabels.Clear();

                foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
                {
                    if (!networkObject.TryGetComponent(out NetworkKartPlayer kart))
                    {
                        continue;
                    }

                    string role = networkObject.IsOwner ? "local" : "remote";
                    Vector3 position = networkObject.transform.position;
                    kartSnapshotLabels.Add(
                        $"- Obj {networkObject.NetworkObjectId} | Owner {networkObject.OwnerClientId} | "
                        + $"{role} | ({position.x:F2}, {position.y:F2}, {position.z:F2})");
                }
            }

            foreach (string snapshotLabel in kartSnapshotLabels)
            {
                GUILayout.Label(snapshotLabel);
            }
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

        private void SetNotice(string message)
        {
            latestNotice = message ?? string.Empty;
            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {latestNotice}";
            activityMessages.Add(timestampedMessage);

            const int maximumActivityMessages = 5;
            if (activityMessages.Count > maximumActivityMessages)
            {
                activityMessages.RemoveAt(0);
            }

            NoticeChanged?.Invoke(latestNotice);
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

            GUILayout.BeginArea(new Rect(20f, 20f, 560f, 700f), GUI.skin.box);
            GUILayout.Label("P, Balap! - Network Room");
            GUILayout.Label($"Status: {state}");
            GUILayout.Label($"Info: {latestNotice}");

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
                GUILayout.Label($"Race: {(raceStarted ? "Racing" : "Preparing")}");

                if (GUILayout.Button("Copy Join Code"))
                {
                    GUIUtility.systemCopyBuffer = joinCode;
                }

                GUILayout.Space(8f);
                GUILayout.Label("Daftar koneksi yang diketahui Host:");
                foreach (ulong clientId in connectedClientIds)
                {
                    string role = networkManager != null && clientId == networkManager.LocalClientId
                        ? "Host (local)"
                        : $"Client {clientId}";
                    GUILayout.Label($"- {role}");
                }

                if (!raceStarted)
                {
                    bool previousGuiEnabled = GUI.enabled;
                    GUI.enabled = ConnectedPlayerCount >= MinimumPlayers;
                    if (GUILayout.Button("Start Race (Host)", GUILayout.Height(36f)))
                    {
                        StartRace();
                    }

                    GUI.enabled = previousGuiEnabled;
                    if (ConnectedPlayerCount < MinimumPlayers)
                    {
                        GUILayout.Label($"Menunggu pemain lain. Minimal {MinimumPlayers} pemain.");
                    }
                }

                DrawSpawnedKartSnapshots();

                if (GUILayout.Button("Close Room (Host)", GUILayout.Height(32f)))
                {
                    CloseRoom();
                }
            }

            if (state == NetworkSessionState.Joined)
            {
                GUILayout.Label($"Terhubung sebagai Client ke room: {joinCode}");
                GUILayout.Label(IsLocalKartAllowedToDrive()
                    ? "Race: Racing - kart dapat dikendalikan."
                    : "Race: Preparing - menunggu Host menekan Start Race.");
                DrawSpawnedKartSnapshots();

                if (GUILayout.Button("Leave Room (Client)", GUILayout.Height(32f)))
                {
                    LeaveRoom();
                }
            }

            if (!string.IsNullOrWhiteSpace(lastError))
            {
                GUILayout.Label(lastError);
            }

            if (activityMessages.Count > 0)
            {
                GUILayout.Space(10f);
                GUILayout.Label("Aktivitas terbaru:");
                foreach (string activityMessage in activityMessages)
                {
                    GUILayout.Label(activityMessage);
                }
            }

            GUILayout.EndArea();
        }
    }
}
