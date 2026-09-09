using System.Collections;
using UnityEngine;
using Unity.Netcode;
using TMPro; // Digunakan jika memakai TextMeshPro

public class RaceCountdown : NetworkBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private TMP_Text countdownText; // Drag UI TextMeshPro ke sini di Inspector

    [Header("Settings")]
    [SerializeField] private float countdownDuration = 3f;

    // NetworkVariable agar angka countdown tersinkron otomatis ke semua Client
    private NetworkVariable<int> currentCountdown = new NetworkVariable<int>(-1);
    private NetworkVariable<bool> isRaceStarted = new NetworkVariable<bool>(false);

    public override void OnNetworkSpawn()
    {
        // Berlangganan perubahan nilai jaringan (NetworkVariable)
        currentCountdown.OnValueChanged += OnCountdownValueChanged;
        isRaceStarted.OnValueChanged += OnRaceStartedValueChanged;

        // Sembunyikan teks di awal
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        // Jalankan countdown otomatis jika ini Server/Host
        if (IsServer)
        {
            StartCoroutine(StartCountdownRoutine());
        }
    }

    public override void OnNetworkDespawn()
    {
        currentCountdown.OnValueChanged -= OnCountdownValueChanged;
        isRaceStarted.OnValueChanged -= OnRaceStartedValueChanged;
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartCountdownServerRpc()
    {
        if (IsServer)
        {
            StartCoroutine(StartCountdownRoutine());
        }
    }

    private IEnumerator StartCountdownRoutine()
    {
        isRaceStarted.Value = false;
        
        // Hitung mundur dari 3, 2, 1
        for (int i = (int)countdownDuration; i > 0; i--)
        {
            currentCountdown.Value = i;
            yield return new WaitForSeconds(1f);
        }

        // Saat nol / selesai
        currentCountdown.Value = 0;
        isRaceStarted.Value = true;

        // Tunggu 1 detik saat teks "GO!" muncul, lalu sembunyikan UI
        yield return new WaitForSeconds(1f);
        currentCountdown.Value = -1;
    }

    private void OnCountdownValueChanged(int previousValue, int newValue)
    {
        if (countdownText == null) return;

        if (newValue > 0)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = newValue.ToString();
        }
        else if (newValue == 0)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = "GOOOO!";
        }
        else
        {
            countdownText.gameObject.SetActive(false);
        }
    }

    private void OnRaceStartedValueChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            Debug.Log("Balapan Dimulai! Mobil boleh bergerak.");
            // Nanti di sini bisa memanggil script kontrol mobil Michael (Orang 2) untuk mengaktifkan input
        }
    }

    // Helper method yang bisa dipanggil oleh script lain untuk cek apakah race sudah jalan
    public bool IsRaceStarted()
    {
        return isRaceStarted.Value;
    }
}