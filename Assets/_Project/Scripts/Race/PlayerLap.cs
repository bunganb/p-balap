using UnityEngine;
using Unity.Netcode;

public class PlayerLap : NetworkBehaviour
{
    // Variabel jaringan yang hanya bisa ditambah oleh Server, tapi dibaca semua pemain
    public NetworkVariable<int> lapsCompleted = new NetworkVariable<int>(
        0, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    private float lastLapTime;

    public override void OnNetworkSpawn()
    {
        // Dengarkan jika ada perubahan nilai lap dari server
        lapsCompleted.OnValueChanged += OnLapChanged;

        // Update UI saat pertama kali mobil muncul
        UpdateLapUI();
    }

    public override void OnNetworkDespawn()
    {
        lapsCompleted.OnValueChanged -= OnLapChanged;
    }

    private void OnLapChanged(int previousValue, int newValue)
    {
        UpdateLapUI();
    }

    private void UpdateLapUI()
    {
        // Pastikan hanya update UI di layar milik pemain ini sendiri (jangan update UI dari data mobil musuh)
        if (IsOwner && RaceManager.Instance != null)
        {
            RaceManager.Instance.UpdateLocalLapUI(lapsCompleted.Value);
        }
    }

    // Dipanggil oleh garis finish (HANYA DI SERVER)
    public void OnTriggerFinishLine()
    {
        if (!IsServer) return;

        // Cooldown 5 detik agar tidak double hit (lap nambah 2x) saat melewati collider
        if (Time.time - lastLapTime < 5f) return;

        lastLapTime = Time.time;
        lapsCompleted.Value++; // Tambah lap

        // Lapor ke RaceManager untuk mengecek apakah semua pemain sudah selesai
        RaceManager.Instance.CheckRaceCompletion();
    }
}