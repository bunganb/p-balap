using UnityEngine;
using Unity.Netcode;
using TMPro;
using PBalap.Network;

public class RaceManager : NetworkBehaviour
{
    public static RaceManager Instance;

    [Header("Pengaturan Lap")]
    public int totalLaps = 3;

    [Header("Referensi UI")]
    [SerializeField] private TextMeshProUGUI lapText;
    [SerializeField] private TextMeshProUGUI raceOverText;
    private bool showingPreRaceStatus;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        if (raceOverText == null)
        {
            return;
        }

        NetworkSessionController session = FindAnyObjectByType<NetworkSessionController>();
        if (session == null || !session.IsConnected || session.IsRaceStarted || session.IsRaceStartInProgress)
        {
            if (showingPreRaceStatus)
            {
                raceOverText.text = "";
                showingPreRaceStatus = false;
            }

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

    // Mengecek apakah semua pemain di dalam arena sudah lap 3
    public void CheckRaceCompletion()
    {
        if (!IsServer) return;

        PlayerLap[] allPlayers = FindObjectsByType<PlayerLap>();
        bool allFinished = true;

        foreach (var player in allPlayers)
        {
            if (player.lapsCompleted.Value < totalLaps)
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