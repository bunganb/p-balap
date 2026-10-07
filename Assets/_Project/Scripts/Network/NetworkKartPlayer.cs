using System;
using PBalap.Vehicle;
using Unity.Netcode;
using UnityEngine;

namespace PBalap.Network
{
    /// <summary>Controls owner-side kart simulation while the server owns race state.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(OwnerNetworkTransform))]
    [RequireComponent(typeof(ArcadeVehicleController), typeof(Rigidbody))]
    public sealed class NetworkKartPlayer : NetworkBehaviour
    {
        public static event Action<int> CountdownChanged;

        [SerializeField] private ArcadeVehicleController arcadeController;
        [SerializeField] private Rigidbody vehicleRigidbody;

        private readonly NetworkVariable<bool> canDrive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> bombCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> countdownValue = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool CanDrive => canDrive.Value;
        public int BombCount => bombCount.Value;

        private void Awake()
        {
            if (arcadeController == null)
            {
                arcadeController = GetComponent<ArcadeVehicleController>();
            }

            if (vehicleRigidbody == null)
            {
                vehicleRigidbody = GetComponent<Rigidbody>();
            }

            ApplyControlState(false);
        }

        public override void OnNetworkSpawn()
        {
            canDrive.OnValueChanged += HandleCanDriveChanged;
            countdownValue.OnValueChanged += HandleCountdownChanged;
            ApplyControlState(canDrive.Value);
            Debug.Log(
                $"[NetworkKart] Spawned object {NetworkObjectId}, owner {OwnerClientId}, "
                + $"localOwner={IsOwner}, position={transform.position}.");
        }

        public override void OnNetworkDespawn()
        {
            canDrive.OnValueChanged -= HandleCanDriveChanged;
            countdownValue.OnValueChanged -= HandleCountdownChanged;
            ApplyControlState(false);
        }

        public void SetCanDriveOnServer(bool enabled)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[NetworkKart] Only the server can change the drive state.");
                return;
            }

            canDrive.Value = enabled;
        }

        public bool TryStoreBombOnServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return false;
            }

            bombCount.Value++;
            return true;
        }

        public bool SetCountdownOnServer(int value)
        {
            if (!IsServer || !IsSpawned)
            {
                return false;
            }

            countdownValue.Value = value;
            return true;
        }

        private void HandleCanDriveChanged(bool previousValue, bool currentValue)
        {
            ApplyControlState(currentValue);
        }

        private void HandleCountdownChanged(int previousValue, int currentValue)
        {
            CountdownChanged?.Invoke(currentValue);
        }

        private void ApplyControlState(bool raceAllowsDriving)
        {
            bool localOwner = IsSpawned && IsOwner;
            bool localCanDrive = localOwner && raceAllowsDriving;

            if (arcadeController != null)
            {
                arcadeController.SetControlEnabled(localCanDrive);
                arcadeController.SetSimulationEnabled(localCanDrive);
                arcadeController.SetCameraEnabled(localOwner);
            }

            if (vehicleRigidbody == null)
            {
                return;
            }

            if (!localCanDrive && !vehicleRigidbody.isKinematic)
            {
                vehicleRigidbody.linearVelocity = Vector3.zero;
                vehicleRigidbody.angularVelocity = Vector3.zero;
            }

            // The owner simulates immediately. Non-owner instances are kinematic
            // presentation proxies driven by the owner-authoritative NetworkTransform.
            vehicleRigidbody.interpolation = localCanDrive
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.None;
            vehicleRigidbody.useGravity = localCanDrive;
            vehicleRigidbody.isKinematic = !localCanDrive;
        }
    }
}
