using System.Collections;
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

    private bool isRaceStarted;
    private Coroutine countdownRoutine;

    public int TimeToStart => Mathf.Max(1, Mathf.CeilToInt(countdownDuration));

    private void Awake()
    {
        if (countdownText != null)
        {
            countdownText.enabled = false;
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

        if (countdownRoutine != null)
        {
            StopCoroutine(countdownRoutine);
            countdownRoutine = null;
        }
    }

    public void StartCountdownServerRpc()
    {
        if (countdownRoutine == null && !isRaceStarted)
        {
            countdownRoutine = StartCoroutine(StartCountdownRoutine());
        }
    }

    private IEnumerator StartCountdownRoutine()
    {
        isRaceStarted = false;

        for (int value = TimeToStart; value > 0; value--)
        {
            RenderCountdown(value);
            yield return new WaitForSeconds(1f);
        }

        isRaceStarted = true;
        RenderCountdown(0);
        yield return new WaitForSeconds(1f);
        RenderCountdown(-1);
        countdownRoutine = null;
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
        countdownText.enabled = value >= 0;
    }

    public bool IsRaceStarted()
    {
        return isRaceStarted;
    }
}
