using UnityEngine;
using Unity.Netcode;
using PBalap.Vehicle; // Wajib ditambahkan agar bisa mengambil komponen kontroler mobil

public class PlayerLap : NetworkBehaviour
{
    public NetworkVariable<int> lapsCompleted = new NetworkVariable<int>(
        0, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    private float lastLapTime;
    private bool isFinished = false; // Mencegah lap bertambah terus saat sudah selesai

    public override void OnNetworkSpawn()
    {
        lapsCompleted.OnValueChanged += OnLapChanged;
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
        if (IsOwner && RaceManager.Instance != null)
        {
            RaceManager.Instance.UpdateLocalLapUI(lapsCompleted.Value);
        }
    }

    public void OnTriggerFinishLine()
    {
        // Jangan jalankan jika ini di client atau pemain ini sudah mencapai batas lap
        if (!IsServer || isFinished) return;

        if (Time.time - lastLapTime < 5f) return;

        lastLapTime = Time.time;
        lapsCompleted.Value++;

        // Cek apakah pemain INI sudah menyelesaikan 3 Lap
        if (lapsCompleted.Value >= RaceManager.Instance.totalLaps)
        {
            isFinished = true;
            StopThisCarClientRpc(); // Hentikan mobil pemain ini saja
        }

        // Selalu lapor ke manager untuk mengecek apakah SEMUA pemain sudah selesai
        RaceManager.Instance.CheckRaceCompletion();
    }

    [ClientRpc]
    private void StopThisCarClientRpc()
    {
        // Matikan kontrol gas/stir HANYA untuk mobil ini di layar semua orang
        ArcadeVehicleController car = GetComponent<ArcadeVehicleController>();
        if (car != null)
        {
            car.SetControlEnabled(false);
        }

        // Ubah UI di layar pemain yang baru saja finish ini
        if (IsOwner && RaceManager.Instance != null)
        {
            RaceManager.Instance.ShowWaitingUI();
        }
    }
}