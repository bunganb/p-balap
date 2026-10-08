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
        [SerializeField] private OwnerNetworkTransform networkTransform;

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

        private uint nextInputSequence;
        private uint lastProcessedInputSequence;
        private const int InputHistoryCapacity = 128;
        private readonly InputFrame[] inputHistory = new InputFrame[InputHistoryCapacity];
        private float snapshotSendAccumulator;
        private uint lastReconciledSequence;

        private const float SnapshotSendInterval = 1f / 20f;
        private const float ReconciliationPositionThreshold = 0.15f;
        private const float ReconciliationRotationThreshold = 3f;

        private struct InputFrame
        {
            public uint Sequence;
            public float Steering;
            public float Throttle;
            public bool Brake;
        }

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

            if (networkTransform == null)
            {
                networkTransform = GetComponent<OwnerNetworkTransform>();
            }

            ApplyControlState(false);
        }

        public override void OnNetworkSpawn()
        {
            canDrive.OnValueChanged += HandleCanDriveChanged;
            countdownValue.OnValueChanged += HandleCountdownChanged;
            ApplyControlState(canDrive.Value);
            ApplyTransformPresentation();
            Debug.Log(
                $"[NetworkKart] Spawned object {NetworkObjectId}, owner {OwnerClientId}, "
                + $"localOwner={IsOwner}, position={transform.position}.");
        }

        private void FixedUpdate()
        {
            if (!IsOwner || !CanDrive || IsServer)
            {
                return;
            }

            SendInputToServer();
        }

        private void LateUpdate()
        {
            ApplyTransformPresentation();
            if (IsServer && CanDrive)
            {
                snapshotSendAccumulator += Time.deltaTime;
                if (snapshotSendAccumulator >= SnapshotSendInterval)
                {
                    snapshotSendAccumulator = 0f;
                    SendAuthoritativeSnapshot();
                }
            }
        }

        private void ApplyTransformPresentation()
        {
            if (networkTransform == null || IsServer)
            {
                return;
            }

            // The local owner already predicts its Rigidbody immediately. Keep
            // server snapshots for correction, but do not display the remote
            // interpolation buffer on top of that prediction.
            networkTransform.Interpolate = !IsOwner;
        }

        private void SendInputToServer()
        {
            float steering = arcadeController.SteeringInput;
            float throttle = arcadeController.ThrottleInput;
            bool brake = arcadeController.BrakeInput;

            uint sequence = ++nextInputSequence;
            inputHistory[(int)(sequence % InputHistoryCapacity)] = new InputFrame
            {
                Sequence = sequence,
                Steering = steering,
                Throttle = throttle,
                Brake = brake
            };

            SubmitInputServerRpc(
                steering,
                throttle,
                brake,
                sequence);
        }

        private void SendAuthoritativeSnapshot()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            ClientRpcParams target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            };
            ReconcileClientRpc(
                transform.position,
                transform.rotation,
                vehicleRigidbody.linearVelocity,
                lastProcessedInputSequence,
                target);
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void ReconcileClientRpc(
            Vector3 serverPosition,
            Quaternion serverRotation,
            Vector3 serverVelocity,
            uint acknowledgedSequence,
            ClientRpcParams clientRpcParams = default)
        {
            if (!IsOwner || IsServer || acknowledgedSequence <= lastReconciledSequence)
            {
                return;
            }

            if (acknowledgedSequence > nextInputSequence)
            {
                return;
            }

            lastReconciledSequence = acknowledgedSequence;
            float positionError = Vector3.Distance(transform.position, serverPosition);
            float rotationError = Quaternion.Angle(transform.rotation, serverRotation);
            if (positionError <= ReconciliationPositionThreshold
                && rotationError <= ReconciliationRotationThreshold)
            {
                return;
            }

            transform.SetPositionAndRotation(serverPosition, serverRotation);
            vehicleRigidbody.linearVelocity = serverVelocity;
            vehicleRigidbody.angularVelocity = Vector3.zero;

            uint firstPendingSequence = acknowledgedSequence + 1;
            uint lastPendingSequence = nextInputSequence;
            if (lastPendingSequence - firstPendingSequence + 1 > InputHistoryCapacity)
            {
                firstPendingSequence = lastPendingSequence - InputHistoryCapacity + 1;
            }

            for (uint sequence = firstPendingSequence; sequence <= lastPendingSequence; sequence++)
            {
                InputFrame frame = inputHistory[(int)(sequence % InputHistoryCapacity)];
                if (frame.Sequence != sequence)
                {
                    continue;
                }

                arcadeController.SetInput(frame.Steering, frame.Throttle, frame.Brake);
                arcadeController.ReplayMovementStep(Time.fixedDeltaTime);
            }
        }

        [ServerRpc(RequireOwnership = true, Delivery = RpcDelivery.Unreliable)]
        private void SubmitInputServerRpc(
            float steering,
            float throttle,
            bool brake,
            uint inputSequence)
        {
            if (!CanDrive
                || inputSequence <= lastProcessedInputSequence
                || float.IsNaN(steering)
                || float.IsInfinity(steering)
                || float.IsNaN(throttle)
                || float.IsInfinity(throttle))
            {
                return;
            }

            lastProcessedInputSequence = inputSequence;
            arcadeController.SetInput(steering, throttle, brake);
        }

        public override void OnNetworkDespawn()
        {
            canDrive.OnValueChanged -= HandleCanDriveChanged;
            countdownValue.OnValueChanged -= HandleCountdownChanged;
            if (networkTransform != null && !IsServer)
            {
                networkTransform.Interpolate = true;
            }
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
            bool canSimulate = IsServer && raceAllowsDriving
                || localOwner && raceAllowsDriving;

            if (arcadeController != null)
            {
                arcadeController.SetControlEnabled(localOwner && raceAllowsDriving);
                arcadeController.SetLocalInputEnabled(localOwner && raceAllowsDriving);
                arcadeController.SetSimulationEnabled(canSimulate);
                arcadeController.SetCameraEnabled(localOwner);
            }

            if (vehicleRigidbody == null)
            {
                return;
            }

            if (!canSimulate && !vehicleRigidbody.isKinematic)
            {
                vehicleRigidbody.linearVelocity = Vector3.zero;
                vehicleRigidbody.angularVelocity = Vector3.zero;
            }

            // The server and local owner simulate. Remote instances are kinematic
            // presentation proxies driven by the server-authoritative NetworkTransform.
            vehicleRigidbody.interpolation = canSimulate
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.Interpolate;
            vehicleRigidbody.useGravity = canSimulate;
            vehicleRigidbody.isKinematic = !canSimulate;
        }
    }
}
