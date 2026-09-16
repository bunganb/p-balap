using PBalap.Vehicle;
using Unity.Netcode;
using UnityEngine;

namespace PBalap.Network
{
    /// <summary>Connects kart ownership and the replicated race gate to local vehicle control.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(OwnerNetworkTransform))]
    [RequireComponent(typeof(ArcadeVehicleController), typeof(Rigidbody))]
    public sealed class NetworkKartPlayer : NetworkBehaviour
    {
        [SerializeField] private ArcadeVehicleController arcadeController;
        [SerializeField] private Rigidbody vehicleRigidbody;

        private readonly NetworkVariable<bool> canDrive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool CanDrive => canDrive.Value;

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
            ApplyControlState(canDrive.Value);
            Debug.Log(
                $"[NetworkKart] Spawned object {NetworkObjectId}, owner {OwnerClientId}, "
                + $"localOwner={IsOwner}, position={transform.position}.");
        }

        public override void OnNetworkDespawn()
        {
            canDrive.OnValueChanged -= HandleCanDriveChanged;
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

        private void HandleCanDriveChanged(bool previousValue, bool currentValue)
        {
            ApplyControlState(currentValue);
        }

        private void ApplyControlState(bool raceAllowsDriving)
        {
            bool localOwner = IsSpawned && IsOwner;
            bool localCanDrive = localOwner && raceAllowsDriving;

            if (arcadeController != null)
            {
                arcadeController.SetControlEnabled(localCanDrive);
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

            // Only the owner simulates physics. Remote peers display snapshots.
            vehicleRigidbody.useGravity = localCanDrive;
            vehicleRigidbody.isKinematic = !localCanDrive;
        }
    }
}
