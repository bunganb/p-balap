using UnityEngine;
using Unity.Netcode;
using TMPro;

public class RaceManager : NetworkBehaviour
{
    public static RaceManager Instance;

    [Header("Pengaturan Lap")]
    public int totalLaps = 3;

    [Header("Referensi UI")]
    [SerializeField] private TextMeshProUGUI lapText;
    [SerializeField] private TextMeshProUGUI raceOverText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
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
            raceOverText.text = "YOU FINISHED!\nWAITING FOR OTHERS...";
        }
    }

    // Mengecek apakah semua pemain di dalam arena sudah lap 3
    public void CheckRaceCompletion()
    {
        if (!IsServer) return;

        PlayerLap[] allPlayers = FindObjectsByType<PlayerLap>(FindObjectsSortMode.None);
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