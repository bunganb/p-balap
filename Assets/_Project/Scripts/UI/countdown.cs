using UnityEngine;
using Unity.Netcode;
using TMPro; 
using System.Collections;
using PBalap.Vehicle; 

public class Countdown : NetworkBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private TextMeshProUGUI countdownText;
    
    [Header("Settings")]
    [SerializeField] private int timeToStart = 3;

    // OnNetworkSpawn menggantikan Start() di multiplayer
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (countdownText != null)
        {
            countdownText.text = "GET READY!";
        }

        // Kunci kontrol saat jaringan baru tersambung
        SetAllVehiclesControl(false);

        // Jika yang menekan tombol adalah Host (Server), mulai hitungan mundur
        if (IsServer)
        {
            StartCoroutine(CountdownRoutine());
        }
    }

    private IEnumerator CountdownRoutine()
    {
        // Jeda 2 detik sebelum angka muncul, agar game sempat memuat mobil pemain
        yield return new WaitForSeconds(2f); 

        int timer = timeToStart;

        while (timer > 0)
        {
            // Menyuruh semua client mengupdate tulisan angka
            UpdateTextClientRpc(timer.ToString());
            
            // Kita pastikan mobil tetap terkunci setiap detik (berguna jika ada pemain yang telat loading)
            LockControlsClientRpc(); 
            
            yield return new WaitForSeconds(1f);
            timer--;
        }

        // Hitungan selesai
        UpdateTextClientRpc("GO!");
        EnableControlsClientRpc(); // Menyuruh semua client membuka kunci mobilnya

        // Hilangkan teks "GO!" setelah 1 detik
        yield return new WaitForSeconds(1f);
        UpdateTextClientRpc(""); 
    }

    [ClientRpc]
    private void UpdateTextClientRpc(string text)
    {
        if (countdownText != null)
        {
            countdownText.text = text;
        }
    }

    [ClientRpc]
    private void LockControlsClientRpc()
    {
        SetAllVehiclesControl(false);
    }

    [ClientRpc]
    private void EnableControlsClientRpc()
    {
        SetAllVehiclesControl(true);
    }

    private void SetAllVehiclesControl(bool isEnabled)
    {
        // Cari semua mobil yang ada di arena balap
        ArcadeVehicleController[] vehicles = FindObjectsByType<ArcadeVehicleController>(FindObjectsSortMode.None);
        
        foreach (var v in vehicles)
        {
            v.SetControlEnabled(isEnabled);
        }
    }
}