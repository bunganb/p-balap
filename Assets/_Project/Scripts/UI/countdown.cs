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

    private bool isRacing = false; 
    private bool isCountdownStarted = false; 

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (countdownText != null)
        {
            // Pesan untuk mengingatkan Host
            countdownText.text = IsServer ? "PRESS 'ENTER' TO START" : "WAITING FOR HOST...";
        }
    }

    private void Update()
    {
        // Terus kunci mobil selama balapan belum berstatus "GO!"
        if (!isRacing)
        {
            SetAllVehiclesControl(false);
        }

        // Cek jika dia Host, hitungan belum mulai, dan menekan Enter (Return) atau Numpad Enter (KeypadEnter)
        if (IsServer && !isCountdownStarted)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Debug.Log("Tombol Enter ditekan oleh Host! Memulai hitungan mundur...");
                isCountdownStarted = true; 
                TriggerCountdownClientRpc(); 
            }
        }
    }

    [ClientRpc]
    private void TriggerCountdownClientRpc()
    {
        // Berjalan serentak di layar Host dan Client
        StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        int timer = timeToStart;

        while (timer > 0)
        {
            if (countdownText != null) countdownText.text = timer.ToString();
            
            yield return new WaitForSeconds(1f);
            timer--;
        }

        // Hitungan selesai
        if (countdownText != null) countdownText.text = "GO!";
        isRacing = true; // Matikan pengunci otomatis di Update()
        SetAllVehiclesControl(true); // Lepas kunci semua mobil

        // Hilangkan teks "GO!" setelah 1 detik
        yield return new WaitForSeconds(1f);
        if (countdownText != null) countdownText.text = ""; 
    }

    private void SetAllVehiclesControl(bool isEnabled)
    {
        ArcadeVehicleController[] vehicles = FindObjectsByType<ArcadeVehicleController>(FindObjectsSortMode.None);
        
        foreach (var v in vehicles)
        {
            v.SetControlEnabled(isEnabled);
        }
    }
}