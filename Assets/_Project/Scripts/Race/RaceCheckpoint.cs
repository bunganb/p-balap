using System;
using UnityEngine;

/// <summary>Reports an ordered checkpoint crossing to the player's lap tracker.</summary>
[RequireComponent(typeof(Collider))]
public sealed class RaceCheckpoint : MonoBehaviour
{
    private void Awake()
    {
        Collider checkpointCollider = GetComponent<Collider>();
        checkpointCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerLap playerLap = other.GetComponentInParent<PlayerLap>();
        if (playerLap == null || (!playerLap.IsOwner && !playerLap.IsServer))
        {
            return;
        }

        RaceCheckpoint[] checkpoints = GetOrderedCheckpoints();
        int checkpointIndex = Array.IndexOf(checkpoints, this);
        if (checkpointIndex < 0)
        {
            Debug.LogWarning($"[RaceCheckpoint] {name} tidak ditemukan dalam daftar checkpoint.", this);
            return;
        }

        playerLap.OnCheckpointPassed(checkpointIndex);
    }

    public static RaceCheckpoint[] GetOrderedCheckpoints()
    {
        RaceCheckpoint[] checkpoints = FindObjectsByType<RaceCheckpoint>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        Array.Sort(checkpoints, (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));
        return checkpoints;
    }
}
