using PBalap.Network;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RaceCountdown : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private TMP_Text countdownText;

    [Header("Settings")]
    [SerializeField] private float countdownDuration = 3f;

    public int TimeToStart => Mathf.Max(1, Mathf.CeilToInt(countdownDuration));

    private void Awake()
    {
        if (countdownText == null)
        {
            countdownText = GetComponent<TMP_Text>();
        }
    }

    private void OnEnable()
    {
        NetworkKartPlayer.CountdownChanged += RenderCountdown;
        RenderCountdown(-1);
    }

    private void OnDisable()
    {
        NetworkKartPlayer.CountdownChanged -= RenderCountdown;
    }

    private void RenderCountdown(int value)
    {
        if (countdownText == null)
        {
            return;
        }

        countdownText.text = value > 0
            ? value.ToString()
            : value == 0
                ? "GO!"
                : string.Empty;
        // RaceCountdown berada pada GameObject teks yang sama. Menonaktifkan
        // GameObject akan ikut mematikan script dan melepas event network.
        countdownText.enabled = value >= 0;
    }
}
