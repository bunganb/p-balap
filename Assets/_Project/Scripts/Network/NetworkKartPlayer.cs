using PBalap.Vehicle;
using Unity.Netcode;
using UnityEngine;

namespace PBalap.Network
{
    /// <summary>Routes owner input to the server and controls server-side kart simulation.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(OwnerNetworkTransform))]
    [RequireComponent(typeof(ArcadeVehicleController), typeof(Rigidbody))]
    public sealed class NetworkKartPlayer : NetworkBehaviour
    {
        private const float InputSendInterval = 1f / 30f;
        private const float InputTimeout = 0.25f;
        private const float AutomaticThrottle = 1f;
        private const float StaleSteeringRecoverySpeed = 4f;
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

        private void FixedUpdate()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner && IsClient && CanDrive && !IsServer)
            {
                inputSendTimer -= Time.fixedDeltaTime;
                if (inputSendTimer <= 0f)
                {
                    SubmitInputRpc(
                        ++localInputSequence,
                        arcadeController.SteeringInput,
                        arcadeController.ThrottleInput,
                        arcadeController.BrakeInput);
                    inputSendTimer = InputSendInterval;
                }
            }

            if (IsServer && !IsOwner && CanDrive)
            {
                bool inputExpired = Time.time - lastServerInputTime > InputTimeout;
                if (inputExpired)
                {
                    // A lost UDP input packet must not stop an automatically-moving kart.
                    // Release steering gradually and continue forward until fresh input arrives.
                    serverSteeringInput = Mathf.MoveTowards(
                        serverSteeringInput,
                        0f,
                        StaleSteeringRecoverySpeed * Time.fixedDeltaTime);
                }

                arcadeController.SetInput(
                    serverSteeringInput,
                    inputExpired ? AutomaticThrottle : serverThrottleInput,
                    inputExpired ? false : serverBrakeInput);
            }
        }

        private float serverSteeringInput;
        private float serverThrottleInput;
        private bool serverBrakeInput;
        private float inputSendTimer;
        private float lastServerInputTime;
        private uint localInputSequence;
        private uint lastServerInputSequence;
        private bool hasReceivedServerInput;

        [Rpc(
            SendTo.Server,
            Delivery = RpcDelivery.Unreliable,
            InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputRpc(
            uint sequence,
            float steering,
            float throttle,
            bool brake,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            // Unreliable packets can arrive out of order. Never let an older input
            // overwrite a newer steering/brake state that the server already applied.
            if (hasReceivedServerInput && !IsSequenceNewer(sequence, lastServerInputSequence))
            {
                return;
            }

            lastServerInputSequence = sequence;
            hasReceivedServerInput = true;
            serverSteeringInput = Mathf.Clamp(steering, -1f, 1f);
            serverThrottleInput = Mathf.Clamp(throttle, -1f, 1f);
            serverBrakeInput = brake;
            lastServerInputTime = Time.time;
        }

        private static bool IsSequenceNewer(uint candidate, uint current)
        {
            return unchecked((int)(candidate - current)) > 0;
        }

        private void ApplyControlState(bool raceAllowsDriving)
        {
            bool localOwner = IsSpawned && IsOwner;
            bool localCanDrive = localOwner && raceAllowsDriving;
            bool serverCanDrive = IsServer && raceAllowsDriving;

            if (arcadeController != null)
            {
                arcadeController.SetControlEnabled(localCanDrive);
                arcadeController.SetSimulationEnabled(serverCanDrive);
                arcadeController.SetCameraEnabled(localOwner);
            }

            if (vehicleRigidbody == null)
            {
                return;
            }

            if (!serverCanDrive && !vehicleRigidbody.isKinematic)
            {
                vehicleRigidbody.linearVelocity = Vector3.zero;
                vehicleRigidbody.angularVelocity = Vector3.zero;
            }

            if (!raceAllowsDriving)
            {
                serverSteeringInput = 0f;
                serverThrottleInput = 0f;
                serverBrakeInput = false;
                inputSendTimer = 0f;
                localInputSequence = 0;
                lastServerInputSequence = 0;
                hasReceivedServerInput = false;
            }

            // The server simulates physics. Clients display replicated snapshots.
            vehicleRigidbody.useGravity = serverCanDrive;
            vehicleRigidbody.isKinematic = !serverCanDrive;
        }
    }
}
