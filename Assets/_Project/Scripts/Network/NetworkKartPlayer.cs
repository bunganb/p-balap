using System;
using System.Collections;
using PBalap.Items;
using PBalap.Vehicle;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField, Min(0f)] private float itemTargetingDistance;
        [SerializeField, Min(0.1f)] private float itemStunDuration = 1.5f;

        private readonly NetworkVariable<bool> canDrive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> bombCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> isStunned = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private BombPickup heldBomb;
        private Coroutine stunRoutine;
        private readonly NetworkVariable<int> countdownValue = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private uint nextInputSequence;
        private uint lastProcessedInputSequence;
        private const int InputHistoryCapacity = 128;
        private readonly InputFrame[] inputHistory = new InputFrame[InputHistoryCapacity];
        private float inputSendAccumulator;
        private float snapshotSendAccumulator;
        private uint lastReconciledSequence;

        // Physics runs at 50 Hz, while the NetworkManager is configured for 30 Hz.
        // Sending more RPCs than network ticks only queues redundant input packets.
        private const float InputSendInterval = 1f / 30f;
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
        public bool IsStunned => isStunned.Value;

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
            isStunned.OnValueChanged += HandleStunnedChanged;
            countdownValue.OnValueChanged += HandleCountdownChanged;
            ApplyControlState(canDrive.Value && !isStunned.Value);
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
            if (inputSendAccumulator < InputSendInterval)
            {
                return;
            }

            inputSendAccumulator -= InputSendInterval;
            SendInputToServer();
        }

        private void LateUpdate()
        {
            if (IsServer && CanDrive && !IsStunned)
            {
                snapshotSendAccumulator += Time.deltaTime;
                if (snapshotSendAccumulator >= SnapshotSendInterval)
                {
                    snapshotSendAccumulator = 0f;
                    SendAuthoritativeSnapshot();
                }
            }
        }

        private void Update()
        {
            if (!IsOwner || !CanDrive || IsStunned || BombCount <= 0)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                RequestThrowBomb();
            }
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
                || IsStunned
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
            isStunned.OnValueChanged -= HandleStunnedChanged;
            countdownValue.OnValueChanged -= HandleCountdownChanged;
            if (stunRoutine != null)
            {
                StopCoroutine(stunRoutine);
                stunRoutine = null;
            }
            heldBomb = null;
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

        public bool AssignBombOnServer(BombPickup bomb)
        {
            if (!IsServer || !IsSpawned || bomb == null || bombCount.Value > 0)
            {
                return false;
            }

            heldBomb = bomb;
            bombCount.Value = 1;
            return true;
        }

        public bool StunOnServer()
        {
            if (!IsServer || !IsSpawned || !CanDrive)
            {
                return false;
            }

            if (stunRoutine != null)
            {
                StopCoroutine(stunRoutine);
            }

            vehicleRigidbody.linearVelocity = Vector3.zero;
            vehicleRigidbody.angularVelocity = Vector3.zero;
            arcadeController.SetInput(0f, 0f, true);
            isStunned.Value = true;
            ApplyControlState(false);
            stunRoutine = StartCoroutine(ClearStunAfterDelay());
            return true;
        }

        private IEnumerator ClearStunAfterDelay()
        {
            yield return new WaitForSeconds(itemStunDuration);

            if (IsServer && IsSpawned)
            {
                vehicleRigidbody.linearVelocity = Vector3.zero;
                vehicleRigidbody.angularVelocity = Vector3.zero;
                isStunned.Value = false;
                ApplyControlState(canDrive.Value);
            }

            stunRoutine = null;
        }

        private void RequestThrowBomb()
        {
            if (!IsSpawned || !IsOwner || BombCount <= 0)
            {
                return;
            }

            if (IsServer)
            {
                TryThrowBombOnServer();
                return;
            }

            ThrowBombServerRpc();
        }

        [Rpc(
            SendTo.Server,
            Delivery = RpcDelivery.Reliable,
            InvokePermission = RpcInvokePermission.Owner)]
        private void ThrowBombServerRpc()
        {
            TryThrowBombOnServer();
        }

        private bool TryThrowBombOnServer()
        {
            if (!IsServer || !IsSpawned || bombCount.Value <= 0 || heldBomb == null)
            {
                return false;
            }

            BombPickup bomb = heldBomb;
            NetworkKartPlayer target = FindNearestFrontTargetOnServer();
            heldBomb = null;
            bombCount.Value = 0;
            bomb.ThrowFromServer(
                transform.position + transform.forward * 2f,
                transform.forward,
                target,
                this);
            return true;
        }

        private NetworkKartPlayer FindNearestFrontTargetOnServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return null;
            }

            NetworkKartPlayer nearestTarget = null;
            float nearestDistanceSqr = itemTargetingDistance > 0f
                ? itemTargetingDistance * itemTargetingDistance
                : float.PositiveInfinity;

            NetworkKartPlayer[] players = FindObjectsByType<NetworkKartPlayer>(
                FindObjectsInactive.Exclude);
            foreach (NetworkKartPlayer player in players)
            {
                if (player == null
                    || player == this
                    || !player.IsSpawned
                    || !player.CanDrive)
                {
                    continue;
                }

                Vector3 offset = player.transform.position - transform.position;
                float distanceSqr = offset.sqrMagnitude;
                if (distanceSqr <= 0.001f
                    || distanceSqr >= nearestDistanceSqr
                    || Vector3.Dot(transform.forward, offset.normalized) <= 0f)
                {
                    continue;
                }

                nearestTarget = player;
                nearestDistanceSqr = distanceSqr;
            }

            return nearestTarget;
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
            ApplyControlState(currentValue && !isStunned.Value);
        }

        private void HandleStunnedChanged(bool previousValue, bool currentValue)
        {
            ApplyControlState(canDrive.Value && !currentValue);
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
