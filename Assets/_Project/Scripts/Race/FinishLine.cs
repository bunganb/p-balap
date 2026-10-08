using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class FinishLine : MonoBehaviour
{
    [SerializeField] private Transform directionReference;
    [SerializeField] private float crossingEpsilon = 0.05f;
    [SerializeField] private float minimumForwardVelocityZ = 0.01f;
    [SerializeField] private bool enableDebugLogs = true;
    private readonly Dictionary<Rigidbody, float> previousSides = new Dictionary<Rigidbody, float>();
    private readonly HashSet<Rigidbody> notifiedRigidbodies = new HashSet<Rigidbody>();

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody vehicleRigidbody = other.attachedRigidbody;
        if (vehicleRigidbody == null)
        {
            LogWarning($"TriggerEnter tanpa Rigidbody: {other.name}");
            return;
        }

        Vector3 allowedDirection = GetAllowedDirection();
        if (allowedDirection.sqrMagnitude < 0.001f)
        {
            LogWarning("Arah FinishLine tidak valid. Isi directionReference atau rotasi FinishLine.");
            return;
        }

        Log($"TriggerEnter collider={other.name}, rigidbody={vehicleRigidbody.name}, "
            + $"position={vehicleRigidbody.position}, velocity={vehicleRigidbody.linearVelocity}");

        if (vehicleRigidbody.linearVelocity.z > minimumForwardVelocityZ)
        {
            NotifyLapIfPresent(other, vehicleRigidbody, "TriggerEnter velocity.z positif");
        }

        float currentSide = GetSide(vehicleRigidbody, allowedDirection);
        float previousSide = currentSide
            - Vector3.Dot(
                Vector3.ProjectOnPlane(vehicleRigidbody.linearVelocity, Vector3.up),
                allowedDirection)
            * Time.fixedDeltaTime;
        previousSides[vehicleRigidbody] = previousSide;
        CheckForForwardCrossing(other, vehicleRigidbody, allowedDirection, currentSide);
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.attachedRigidbody == null)
        {
            LogWarning($"TriggerStay tanpa Rigidbody: {other.name}");
            return;
        }

        Rigidbody vehicleRigidbody = other.attachedRigidbody;
        Vector3 allowedDirection = GetAllowedDirection();
        if (allowedDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        float currentSide = GetSide(vehicleRigidbody, allowedDirection);
        if (vehicleRigidbody.linearVelocity.z > minimumForwardVelocityZ)
        {
            NotifyLapIfPresent(other, vehicleRigidbody, "TriggerStay velocity.z positif");
        }

        CheckForForwardCrossing(other, vehicleRigidbody, allowedDirection, currentSide);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.attachedRigidbody != null)
        {
            Log($"TriggerExit collider={other.name}, rigidbody={other.attachedRigidbody.name}");
            previousSides.Remove(other.attachedRigidbody);
            notifiedRigidbodies.Remove(other.attachedRigidbody);
        }
    }

    private void CheckForForwardCrossing(
        Collider other,
        Rigidbody vehicleRigidbody,
        Vector3 allowedDirection,
        float currentSide)
    {
        if (!previousSides.TryGetValue(vehicleRigidbody, out float previousSide))
        {
            Log($"Belum ada sisi sebelumnya untuk {vehicleRigidbody.name}; menyimpan currentSide={currentSide:F3}");
            previousSides[vehicleRigidbody] = currentSide;
            return;
        }

        previousSides[vehicleRigidbody] = currentSide;
        if (vehicleRigidbody.linearVelocity.z <= minimumForwardVelocityZ
            || previousSide >= -crossingEpsilon
            || currentSide < crossingEpsilon)
        {
            return;
        }

        NotifyLapIfPresent(other, vehicleRigidbody, "perpindahan sisi");
    }

    private void NotifyLapIfPresent(
        Collider other,
        Rigidbody vehicleRigidbody,
        string detectionSource)
    {
        PlayerLap playerLap = other.GetComponentInParent<PlayerLap>();
        if (playerLap != null)
        {
            if (!notifiedRigidbodies.Add(vehicleRigidbody))
            {
                return;
            }

            Log($"CROSSING terdeteksi ({detectionSource}): {vehicleRigidbody.name}, "
                + $"velocity.z={vehicleRigidbody.linearVelocity.z:F3}, owner={playerLap.OwnerClientId}, "
                + $"isServer={playerLap.IsServer}, isOwner={playerLap.IsOwner}");
            playerLap.OnTriggerFinishLine();
        }
        else
        {
            LogWarning($"CROSSING terdeteksi tetapi PlayerLap tidak ditemukan pada {other.name} "
                + "(pastikan collider kart berada di bawah object yang memiliki PlayerLap).");
        }
    }

    private float GetSide(Rigidbody vehicleRigidbody, Vector3 allowedDirection)
    {
        return Vector3.Dot(
            Vector3.ProjectOnPlane(vehicleRigidbody.position - transform.position, Vector3.up),
            allowedDirection);
    }

    private Vector3 GetAllowedDirection()
    {
        Transform reference = directionReference != null ? directionReference : transform;
        return Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
    }

    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[FinishLine] {message}", this);
        }
    }

    private void LogWarning(string message)
    {
        if (enableDebugLogs)
        {
            Debug.LogWarning($"[FinishLine] {message}", this);
        }
    }
}