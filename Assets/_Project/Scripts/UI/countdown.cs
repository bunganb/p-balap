using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>Displays a server-driven countdown on every connected player.</summary>
public sealed class Countdown : NetworkBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private TextMeshProUGUI countdownText;

    [Header("Settings")]
    [SerializeField, Min(1)] private int timeToStart = 3;

    private readonly NetworkVariable<int> countdownValue = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Coroutine countdownRoutine;

    public event Action CountdownCompletedOnServer;
    public bool IsCountingDown => countdownRoutine != null;

    public override void OnNetworkSpawn()
    {
        countdownValue.OnValueChanged += HandleCountdownChanged;
        RenderCountdown(countdownValue.Value);
    }

    public override void OnNetworkDespawn()
    {
        countdownValue.OnValueChanged -= HandleCountdownChanged;
        if (countdownRoutine != null)
        {
            StopCoroutine(countdownRoutine);
            countdownRoutine = null;
        }
    }

    public bool StartCountdownOnServer()
    {
        if (!IsServer || !IsSpawned || countdownRoutine != null)
        {
            return false;
        }

        countdownRoutine = StartCoroutine(CountdownRoutine());
        return true;
    }

    private IEnumerator CountdownRoutine()
    {
        for (int timer = timeToStart; timer > 0; timer--)
        {
            countdownValue.Value = timer;
            yield return new WaitForSeconds(1f);
        }

        countdownValue.Value = 0;
        CountdownCompletedOnServer?.Invoke();

        yield return new WaitForSeconds(1f);
        countdownValue.Value = -1;
        countdownRoutine = null;
    }

    private void HandleCountdownChanged(int previousValue, int currentValue)
    {
        RenderCountdown(currentValue);
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
