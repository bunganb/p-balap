using UnityEngine;
using Unity.Netcode;
using TMPro;
using PBalap.Network;
using System.Reflection;

public class RaceManager : NetworkBehaviour
{
    public static RaceManager Instance;

    [Header("Pengaturan Lap")]
    public int totalLaps = 3;

    [Header("Referensi UI")]
    [SerializeField] private TextMeshProUGUI lapText;
    [SerializeField] private TextMeshProUGUI raceOverText;
    private bool showingPreRaceStatus;
    private bool networkCountdownActive;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnEnable()
    {
        NetworkKartPlayer.CountdownChanged += HandleCountdownChanged;
    }

    private void OnDisable()
    {
        NetworkKartPlayer.CountdownChanged -= HandleCountdownChanged;
    }

    private void Update()
    {
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
        networkCountdownActive = value >= 0;
        if (networkCountdownActive)
        {
            ClearPreRaceStatus();
        }
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
        if (raceOverText != null)
        {
            // Ganti teks saat semua orang sudah masuk garis finish
            raceOverText.text = "RACE OVER!\nALL PLAYERS FINISHED!";
        }
    }
}