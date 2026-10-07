using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class FinishLine : MonoBehaviour
{
    [SerializeField] private Transform directionReference;
    [SerializeField] private float crossingEpsilon = 0.05f;
    private readonly Dictionary<Rigidbody, float> previousSides = new Dictionary<Rigidbody, float>();

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        Rigidbody vehicleRigidbody = other.attachedRigidbody;
        if (vehicleRigidbody == null)
        {
            return;
        }

        Vector3 allowedDirection = GetAllowedDirection();
        if (allowedDirection.sqrMagnitude < 0.001f)
        {
            return;
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
        if (!NetworkManager.Singleton.IsServer || other.attachedRigidbody == null)
        {
            return;
        }

        Rigidbody vehicleRigidbody = other.attachedRigidbody;
        Vector3 allowedDirection = GetAllowedDirection();
        if (allowedDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        float currentSide = GetSide(vehicleRigidbody, allowedDirection);
        CheckForForwardCrossing(other, vehicleRigidbody, allowedDirection, currentSide);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.attachedRigidbody != null)
        {
            previousSides.Remove(other.attachedRigidbody);
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
            previousSides[vehicleRigidbody] = currentSide;
            return;
        }

        previousSides[vehicleRigidbody] = currentSide;
        if (previousSide >= -crossingEpsilon || currentSide < crossingEpsilon)
        {
            return;
        }

        PlayerLap playerLap = other.GetComponentInParent<PlayerLap>();
        if (playerLap != null)
        {
            playerLap.OnTriggerFinishLine();
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
}