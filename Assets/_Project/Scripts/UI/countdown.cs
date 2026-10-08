using TMPro;
using UnityEngine;
using PBalap.Network;

/// <summary>Displays countdown values replicated by the Host player object.</summary>
public sealed class Countdown : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private TextMeshProUGUI countdownText;

    [Header("Settings")]
    [SerializeField, Min(1)] private int timeToStart = 3;

    public int TimeToStart => timeToStart;

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
        countdownText.gameObject.SetActive(value >= 0);
    }
}
