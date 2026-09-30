using UnityEngine;
using Unity.Netcode;
using TMPro;
using PBalap.Vehicle;

public class RaceManager : NetworkBehaviour
{
    public static RaceManager Instance; // Singleton agar mudah dipanggil script lain

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

    // Fungsi ini dipanggil dari mobil pemain untuk update tulisan UI
    public void UpdateLocalLapUI(int completedLaps)
    {
        // Lap minimal 1, maksimal sesuai totalLaps (agar tidak tampil lap 4/3)
        int displayLap = Mathf.Clamp(completedLaps + 1, 1, totalLaps);
        
        if (lapText != null)
        {
            lapText.text = $"LAP: {displayLap} / {totalLaps}";
        }
    }

    // Fungsi ini hanya dijalankan oleh Server
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
                break; // Ada 1 saja pemain yang belum selesai, balapan berlanjut
            }
        }

        // Jika SEMUA pemain sudah mencapai total lap
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
            raceOverText.text = "RACE FINISHED!";
        }

        // Hentikan paksa semua mobil
        ArcadeVehicleController[] vehicles = FindObjectsByType<ArcadeVehicleController>(FindObjectsSortMode.None);
        foreach (var v in vehicles)
        {
            v.SetControlEnabled(false);
        }
    }
}