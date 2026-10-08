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
    private bool isFinished = false; // Mencegah lap bertambah setelah crossing finish akhir

    public bool IsFinished => isFinished;

    public override void OnNetworkSpawn()
    {
        lapsCompleted.OnValueChanged += OnLapChanged;
        UpdateLapUI();
        Debug.Log($"[PlayerLap] Spawned object={name}, owner={OwnerClientId}, "
            + $"isServer={IsServer}, isOwner={IsOwner}, laps={lapsCompleted.Value}", this);
    }

    public override void OnNetworkDespawn()
    {
        lapsCompleted.OnValueChanged -= OnLapChanged;
    }

    private void OnLapChanged(int previousValue, int newValue)
    {
        Debug.Log($"[PlayerLap] Lap berubah {previousValue} -> {newValue}, "
            + $"owner={OwnerClientId}, isOwner={IsOwner}", this);
        UpdateLapUI();
    }

    private void UpdateLapUI()
    {
        if (IsOwner && RaceManager.Instance != null)
        {
            RaceManager.Instance.UpdateLocalLapUI(lapsCompleted.Value);
        }
        else
        {
            Debug.LogWarning($"[PlayerLap] UI lap tidak diperbarui. "
                + $"isOwner={IsOwner}, RaceManager.Instance null={RaceManager.Instance == null}", this);
        }
    }

    public void OnTriggerFinishLine()
    {
        if (!IsServer)
        {
            if (IsOwner && IsSpawned)
            {
                Debug.Log("[PlayerLap] Crossing dari owner client, mengirim ServerRpc.", this);
                RequestFinishLineServerRpc();
            }
            else
            {
                Debug.LogWarning($"[PlayerLap] Crossing diabaikan: isOwner={IsOwner}, isSpawned={IsSpawned}", this);
            }

            return;
        }

        CompleteLapOnServer();
    }

    [ServerRpc]
    private void RequestFinishLineServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[PlayerLap] ServerRpc ditolak: sender={serverRpcParams.Receive.SenderClientId}, "
                + $"owner={OwnerClientId}", this);
            return;
        }

        CompleteLapOnServer();
    }

    private void CompleteLapOnServer()
    {
        if (!IsServer || isFinished)
        {
            Debug.Log($"[PlayerLap] Lap tidak diproses: isServer={IsServer}, isFinished={isFinished}", this);
            return;
        }

        if (Time.time - lastLapTime < 5f)
        {
            Debug.Log($"[PlayerLap] Lap diabaikan cooldown: {Time.time - lastLapTime:F2}s sejak lap terakhir.", this);
            return;
        }

        lastLapTime = Time.time;
        lapsCompleted.Value++;
        Debug.Log($"[PlayerLap] LAP BERTAMBAH menjadi {lapsCompleted.Value} pada server.", this);

        // Lap 3/3 masih harus memberi kesempatan satu putaran terakhir.
        // Crossing berikutnya menyelesaikan balapan, tetapi nilai UI tetap dibatasi oleh RaceManager.
        if (lapsCompleted.Value > RaceManager.Instance.totalLaps)
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