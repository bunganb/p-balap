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

        private uint nextInputSequence;
        private uint lastProcessedInputSequence;
        private float inputSendAccumulator;
        private bool hasSentInput;

        private const float InputSendRate = 30f;
        private const float InputSendInterval = 1f / InputSendRate;

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

        private void FixedUpdate()
        {
            if (!IsOwner || !CanDrive || IsServer)
            {
                return;
            }

            inputSendAccumulator += Time.fixedDeltaTime;
            if (!hasSentInput)
            {
                inputSendAccumulator = InputSendInterval;
            }

            // Input is state, so a lost unreliable packet is recovered by the next heartbeat.
            // Keep a strict cap at the NGO tick rate to avoid a reliable-message backlog.
            if (inputSendAccumulator < InputSendInterval)
            {
                return;
            }

            inputSendAccumulator = Mathf.Min(inputSendAccumulator, InputSendInterval);
            inputSendAccumulator -= InputSendInterval;
            hasSentInput = true;

            SubmitInputServerRpc(
                arcadeController.SteeringInput,
                arcadeController.ThrottleInput,
                arcadeController.BrakeInput,
                ++nextInputSequence);
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
