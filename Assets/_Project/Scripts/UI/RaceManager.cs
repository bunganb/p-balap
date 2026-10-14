using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;
using PBalap.Network;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class RaceManager : NetworkBehaviour
{
    public static RaceManager Instance;

    [Header("Pengaturan Lap")]
    public int totalLaps = 3;
    [SerializeField, Min(0f)] private float preCountdownDelay = 1f;

    [Header("Referensi UI")]
    [SerializeField] private TextMeshProUGUI lapText;
    [SerializeField] private TextMeshProUGUI raceOverText;
    [SerializeField] private GameObject preGameCanvas;
    [SerializeField] private GameObject inGameCanvas;
    private bool showingPreRaceStatus;
    private bool networkCountdownActive;
    private int latestCountdownValue = -1;
    private NetworkSessionController session;
    private GameObject selectionPanel;
    private GameObject hostPanel;
    private GameObject clientPanel;
    private GameObject clientWaitPanel;
    private TMP_InputField joinCodeInput;
    private TMP_Text countdownText;
    private TextMeshProUGUI[] hostPlayerStatuses = new TextMeshProUGUI[0];
    private TextMeshProUGUI hostJoinCodeText;
    private bool clientPanelRequested;
    private Coroutine startRaceRoutine;
    private bool raceFinished;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        session = FindAnyObjectByType<NetworkSessionController>();
        if (preGameCanvas == null)
        {
            preGameCanvas = GameObject.Find("UI");
        }

        if (inGameCanvas == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            foreach (Canvas canvas in canvases)
            {
                if (canvas.gameObject.name == "In-Game UI")
                {
                    inGameCanvas = canvas.gameObject;
                    break;
                }
            }
        }

        if (inGameCanvas != null)
        {
            Transform countdownObject = inGameCanvas.transform.Find("count");
            countdownText = countdownObject == null
                ? null
                : countdownObject.GetComponent<TMP_Text>();
        }

        if (preGameCanvas != null)
        {
            Transform root = preGameCanvas.transform;
            selectionPanel = root.Find("#1 - select")?.gameObject;
            hostPanel = root.Find("#2 - Host")?.gameObject;
            clientPanel = root.Find("#3 - Client")?.gameObject;
            clientWaitPanel = root.Find("#3 - Client/wait")?.gameObject;
            CacheHostStatusTexts();
            WirePreGameButtons(root);
        }
    }

    private void OnEnable()
    {
        NetworkKartPlayer.CountdownChanged += HandleCountdownChanged;
        if (session != null)
        {
            session.StateChanged += HandleSessionStateChanged;
        }
    }

    private void OnDisable()
    {
        NetworkKartPlayer.CountdownChanged -= HandleCountdownChanged;
        if (session != null)
        {
            session.StateChanged -= HandleSessionStateChanged;
        }

        if (startRaceRoutine != null)
        {
            StopCoroutine(startRaceRoutine);
            startRaceRoutine = null;
        }
    }

    private void Start()
    {
        SetGameplayUiActive(false);
        SyncPreGameUi();
    }

    private void WirePreGameButtons(Transform root)
    {
        Button hostButton = root.Find("#1 - select/host")?.GetComponent<Button>();
        Button joinButton = root.Find("#1 - select/join")?.GetComponent<Button>();
        Button startButton = root.Find("#2 - Host/Start-btn")?.GetComponent<Button>();
        Button copyButton = root.Find("#2 - Host/copy-btn")?.GetComponent<Button>();
        Button hostBackButton = root.Find("#2 - Host/x-button")?.GetComponent<Button>();
        Button clientBackButton = root.Find("#3 - Client/x-button")?.GetComponent<Button>();
        joinCodeInput = root.Find("#3 - Client/inputCode")?.GetComponent<TMP_InputField>();

        if (hostButton != null)
        {
            hostButton.onClick.AddListener(CreateRoom);
        }

        if (joinButton != null)
        {
            joinButton.onClick.AddListener(ShowClientPanel);
        }

        if (startButton != null)
        {
            startButton.onClick.AddListener(StartRaceFromUi);
        }

        if (copyButton != null)
        {
            copyButton.onClick.AddListener(CopyJoinCode);
        }

        if (hostBackButton != null)
        {
            hostBackButton.onClick.AddListener(ReturnToSelection);
        }

        if (clientBackButton != null)
        {
            clientBackButton.onClick.AddListener(ReturnToSelection);
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.onValueChanged.AddListener(UpdateJoinCode);
            joinCodeInput.onSubmit.AddListener(JoinRoom);
        }
    }

    private void CreateRoom()
    {
        if (session == null)
        {
            Debug.LogError("[RaceManager] NetworkSessionController tidak ditemukan.", this);
            return;
        }

        clientPanelRequested = false;
        session.CreateRoomFromUi();
    }

    private void ShowClientPanel()
    {
        clientPanelRequested = true;
        SetPreGamePanel(selectionPanel, false);
        SetPreGamePanel(hostPanel, false);
        SetPreGamePanel(clientPanel, true);
    }

    private void ReturnToSelection()
    {
        if (startRaceRoutine != null)
        {
            StopCoroutine(startRaceRoutine);
            startRaceRoutine = null;
        }

        if (session != null)
        {
            if (session.IsHostingSession)
            {
                session.CloseRoom();
            }
            else if (session.IsConnected)
            {
                session.LeaveRoom();
            }
        }

        clientPanelRequested = false;
        SetGameplayUiActive(false);
        SetPreGamePanel(selectionPanel, true);
        SetPreGamePanel(hostPanel, false);
        SetPreGamePanel(clientPanel, false);
        UpdateClientPanel(false);
    }

    private void JoinRoom(string _)
    {
        if (session == null)
        {
            Debug.LogError("[RaceManager] NetworkSessionController tidak ditemukan.", this);
            return;
        }

        session.JoinRoomFromUi();
    }

    private void StartRaceFromUi()
    {
        if (session == null || startRaceRoutine != null)
        {
            return;
        }

        startRaceRoutine = StartCoroutine(StartRaceAfterDelay());
    }

    private IEnumerator StartRaceAfterDelay()
    {
        if (preCountdownDelay > 0f)
        {
            yield return new WaitForSeconds(preCountdownDelay);
        }

        startRaceRoutine = null;
        if (session != null)
        {
            SetGameplayUiActive(true);
            if (!session.StartRace())
            {
                SetGameplayUiActive(false);
            }
        }
    }

    private void CopyJoinCode()
    {
        if (session != null && !string.IsNullOrWhiteSpace(session.JoinCode))
        {
            GUIUtility.systemCopyBuffer = session.JoinCode;
        }
    }

    private void UpdateJoinCode(string value)
    {
        if (session != null)
        {
            session.JoinCodeInput = value;
        }
    }

    private void HandleSessionStateChanged(NetworkSessionState state)
    {
        if (state == NetworkSessionState.Disconnected
            || state == NetworkSessionState.Hosting
            || state == NetworkSessionState.Joined)
        {
            raceFinished = false;
        }

        SyncPreGameUi();
    }

    private void HandleRaceFinishedInput()
    {
        if (session == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (session.IsHostingSession)
            {
                session.CloseRoom();
            }
            else if (session.IsConnected)
            {
                session.LeaveRoom();
            }

            raceFinished = false;
            SetGameplayUiActive(false);
            return;
        }

        if (session.IsHostingSession
            && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            raceFinished = false;
            if (raceOverText != null)
            {
                raceOverText.text = "";
            }
            SetGameplayUiActive(true);
            if (!session.RestartRace())
            {
                raceFinished = true;
            }
        }
    }

    private void SyncPreGameUi()
    {
        if (session == null || preGameCanvas == null)
        {
            return;
        }

        bool inRace = session.IsRaceStartInProgress
            || session.IsRaceStarted
            || networkCountdownActive
            || IsRaceStartedOnNetwork();
        if (inRace)
        {
            SetGameplayUiActive(true);
            return;
        }

        SetGameplayUiActive(false);
        bool isHost = session.State == NetworkSessionState.Hosting;
        bool isClient = session.State == NetworkSessionState.Joined
            || session.State == NetworkSessionState.Connecting;
        bool showClientPanel = isClient || clientPanelRequested;
        bool isSelecting = !isHost && !showClientPanel;

        SetPreGamePanel(selectionPanel, isSelecting);
        SetPreGamePanel(hostPanel, isHost);
        SetPreGamePanel(clientPanel, showClientPanel);
        UpdateClientPanel(session.State == NetworkSessionState.Joined);
        UpdateHostPanel(isHost);
    }

    private void UpdateClientPanel(bool joined)
    {
        if (joinCodeInput != null)
        {
            joinCodeInput.gameObject.SetActive(!joined);
        }

        if (clientWaitPanel != null)
        {
            clientWaitPanel.SetActive(joined);
        }
    }

    private void CacheHostStatusTexts()
    {
        if (hostPanel == null)
        {
            return;
        }

        List<TextMeshProUGUI> statusTexts = new List<TextMeshProUGUI>();
        TextMeshProUGUI[] texts = hostPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in texts)
        {
            if (text.gameObject.name == "playerStatus")
            {
                statusTexts.Add(text);
            }
            else if (text.gameObject.name == "codeTxt")
            {
                hostJoinCodeText = text;
            }
        }

        hostPlayerStatuses = statusTexts.ToArray();
    }

    private void UpdateHostPanel(bool isHost)
    {
        if (hostJoinCodeText != null)
        {
            hostJoinCodeText.text = session != null && !string.IsNullOrWhiteSpace(session.JoinCode)
                ? $"Code Room : {session.JoinCode}"
                : "Code Room :";
        }

        int onlinePlayers = isHost && session != null
            ? Mathf.Min(session.ConnectedPlayerCount, hostPlayerStatuses.Length)
            : 0;

        for (int index = 0; index < hostPlayerStatuses.Length; index++)
        {
            TextMeshProUGUI statusText = hostPlayerStatuses[index];
            bool online = index < onlinePlayers;
            statusText.text = online ? "Online" : "offline";
            statusText.color = online ? Color.green : Color.red;
        }
    }

    private void SetGameplayUiActive(bool active)
    {
        if (preGameCanvas != null)
        {
            preGameCanvas.SetActive(!active);
        }

        if (inGameCanvas != null)
        {
            inGameCanvas.SetActive(active);
        }

        if (active)
        {
            RenderCountdown(latestCountdownValue);
        }
    }

    private static void SetPreGamePanel(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }

    private void Update()
    {
        SyncPreGameUi();

        if (raceFinished)
        {
            HandleRaceFinishedInput();
            return;
        }

        if (raceOverText == null)
        {
            return;
        }

        NetworkSessionController session = FindAnyObjectByType<NetworkSessionController>();
        if (session == null || !session.IsConnected)
        {
            ClearPreRaceStatus();
            return;
        }

        // Race state is replicated through NetworkKartPlayer. The session controller
        // itself is local, so its race flags are not reliable on clients.
        if (session.IsRaceStarted
            || session.IsRaceStartInProgress
            || networkCountdownActive
            || IsRaceStartedOnNetwork())
        {
            ClearPreRaceStatus();
            return;
        }

        if (session.IsHostingSession)
        {
            raceOverText.text = session.ConnectedPlayerCount >= session.MinimumPlayersRequired
                ? "PRESS ENTER TO START"
                : "WAITING FOR PLAYERS...";
        }
        else
        {
            raceOverText.text = "WAITING FOR HOST TO START...";
        }

        showingPreRaceStatus = true;
    }

    private void HandleCountdownChanged(int value)
    {
        latestCountdownValue = value;
        networkCountdownActive = value >= 0;
        if (networkCountdownActive)
        {
            raceFinished = false;
            if (raceOverText != null)
            {
                raceOverText.text = "";
            }
            SetGameplayUiActive(true);
            RenderCountdown(value);
            ClearPreRaceStatus();
        }
    }

    private void RenderCountdown(int value)
    {
        if (countdownText == null)
        {
            return;
        }

        countdownText.text = value > 0
            ? value.ToString()
            : value == 0
                ? "GO!"
                : string.Empty;
        countdownText.gameObject.SetActive(value >= 0);
    }

    private static bool IsRaceStartedOnNetwork()
    {
        NetworkKartPlayer[] karts = FindObjectsByType<NetworkKartPlayer>();
        foreach (NetworkKartPlayer kart in karts)
        {
            if (kart.CanDrive)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearPreRaceStatus()
    {
        if (!showingPreRaceStatus)
        {
            return;
        }

        raceOverText.text = "";
        showingPreRaceStatus = false;
    }

    public override void OnNetworkSpawn()
    {
        if (raceOverText != null) raceOverText.text = "";
    }

    public void UpdateLocalLapUI(int completedLaps)
    {
        int displayLap = Mathf.Min(completedLaps, totalLaps);
        if (lapText != null)
        {
            lapText.text = $"LAP: {displayLap} / {totalLaps}";
            Debug.Log($"[RaceManager] UI lap diperbarui: {lapText.text}", this);
        }
        else
        {
            Debug.LogWarning("[RaceManager] lapText belum di-assign di Inspector.", this);
        }
    }

    // FUNGSI BARU: Dipanggil dari PlayerLap saat 1 pemain berhasil mencapai lap 3 duluan
    public void ShowWaitingUI()
    {
        if (raceOverText != null)
        {
            showingPreRaceStatus = false;
            raceOverText.text = "YOU FINISHED!\nWAITING FOR OTHERS...";
        }
    }

    // Mengecek apakah semua pemain di dalam arena sudah melewati finish akhir
    public void CheckRaceCompletion()
    {
        if (!IsServer) return;

        PlayerLap[] allPlayers = FindObjectsByType<PlayerLap>();
        bool allFinished = true;

        foreach (var player in allPlayers)
        {
            if (!IsPlayerFinished(player))
            {
                allFinished = false;
                break; 
            }
        }

        // Jika benar-benar SEMUA pemain sudah selesai
        if (allFinished && allPlayers.Length > 0)
        {
            EndRaceClientRpc();
        }
    }

    private static bool IsPlayerFinished(PlayerLap player)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo field = typeof(PlayerLap).GetField("isFinished", flags)
            ?? typeof(PlayerLap).GetField("finished", flags);

        if (field != null && field.FieldType == typeof(bool))
        {
            return (bool)field.GetValue(player);
        }

        PropertyInfo property = typeof(PlayerLap).GetProperty("IsFinished", flags)
            ?? typeof(PlayerLap).GetProperty("HasFinished", flags);

        return property != null && property.PropertyType == typeof(bool)
            && (bool)property.GetValue(player);
    }

    [ClientRpc]
    private void EndRaceClientRpc()
    {
        raceFinished = true;
        if (raceOverText != null)
        {
            raceOverText.text = session != null && session.IsHostingSession
                ? "RACE OVER!\nENTER: RESTART\nESC: MENU"
                : "RACE OVER!\nESC: MENU";
        }
    }
}