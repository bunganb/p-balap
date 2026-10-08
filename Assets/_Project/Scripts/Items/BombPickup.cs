using System.Collections;
using PBalap.Network;
using Unity.Netcode;
using UnityEngine;

namespace PBalap.Items
{
    /// <summary>Network-synchronized bomb pickup that respawns after a cooldown.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(Collider))]
    public sealed class BombPickup : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float respawnDelay = 8f;
        [SerializeField, Min(0.5f)] private float pickupValidationDistance = 4f;
        [SerializeField, Min(0.1f)] private float throwSpeed = 12f;
        [SerializeField, Min(0.1f)] private float throwDuration = 1.5f;
        [SerializeField] private Collider pickupCollider;

        private readonly NetworkVariable<bool> isAvailable = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Renderer[] pickupRenderers;
        private Coroutine respawnRoutine;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        public bool IsAvailable => isAvailable.Value;

        private void Awake()
        {
            if (pickupCollider == null)
            {
                pickupCollider = GetComponent<Collider>();
            }

            pickupRenderers = GetComponentsInChildren<Renderer>(true);
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        public override void OnNetworkSpawn()
        {
            isAvailable.OnValueChanged += HandleAvailabilityChanged;
            ApplyAvailability(isAvailable.Value);
        }

        public override void OnNetworkDespawn()
        {
            isAvailable.OnValueChanged -= HandleAvailabilityChanged;
            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsSpawned || !isAvailable.Value)
            {
                return;
            }

            NetworkKartPlayer player = other.GetComponentInParent<NetworkKartPlayer>();
            if (player == null || !player.IsSpawned || !player.IsOwner)
            {
                return;
            }

            if (IsServer)
            {
                TryCollectOnServer(player.NetworkObject);
                return;
            }

            RequestPickupRpc(player.NetworkObjectId);
        }

        [Rpc(
            SendTo.Server,
            Delivery = RpcDelivery.Reliable,
            InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestPickupRpc(
            ulong playerNetworkObjectId,
            RpcParams rpcParams = default)
        {
            if (!isAvailable.Value
                || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                    playerNetworkObjectId,
                    out NetworkObject playerObject)
                || playerObject.OwnerClientId != rpcParams.Receive.SenderClientId)
            {
                return;
            }

            TryCollectOnServer(playerObject);
        }

        private void TryCollectOnServer(NetworkObject playerObject)
        {
            if (!IsServer
                || !isAvailable.Value
                || playerObject == null
                || !playerObject.TryGetComponent(out NetworkKartPlayer player))
            {
                return;
            }

            float maximumDistanceSqr = pickupValidationDistance * pickupValidationDistance;
            if ((player.transform.position - transform.position).sqrMagnitude > maximumDistanceSqr
                || !player.AssignBombOnServer(this))
            {
                return;
            }

            isAvailable.Value = false;
            ApplyAvailability(false);

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }

            respawnRoutine = StartCoroutine(RespawnAfterDelay());
            Debug.Log(
                $"[BombPickup] Player {player.OwnerClientId} picked up {name}. "
                + $"Inventory={player.BombCount}.");
        }

        public void ThrowFromServer(
            Vector3 position,
            Vector3 direction,
            NetworkKartPlayer target)
        {
            if (!IsServer || !IsSpawned || isAvailable.Value)
            {
                return;
            }

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }

            respawnRoutine = StartCoroutine(ThrowRoutine(position, direction, target));
        }

        private IEnumerator ThrowRoutine(
            Vector3 position,
            Vector3 direction,
            NetworkKartPlayer target)
        {
            Vector3 throwDirection = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : transform.forward;
            float elapsed = 0f;
            Vector3 startPosition = position;

            transform.SetPositionAndRotation(startPosition, Quaternion.LookRotation(throwDirection));
            SetThrownState();

            while (elapsed < throwDuration)
            {
                elapsed += Time.deltaTime;
                if (target != null && target.IsSpawned)
                {
                    Vector3 targetOffset = target.transform.position - transform.position;
                    if (targetOffset.sqrMagnitude > 0.001f)
                    {
                        throwDirection = targetOffset.normalized;
                    }
                }

                transform.position += throwDirection * throwSpeed * Time.deltaTime;
                if (throwDirection.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(throwDirection);
                }

                yield return null;
            }

            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            isAvailable.Value = true;
            ApplyAvailability(true);
            respawnRoutine = null;
        }

        private void SetThrownState()
        {
            if (pickupCollider != null)
            {
                pickupCollider.enabled = false;
            }

            if (pickupRenderers == null)
            {
                return;
            }

            foreach (Renderer pickupRenderer in pickupRenderers)
            {
                if (pickupRenderer != null)
                {
                    pickupRenderer.enabled = true;
                }
            }
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);

            if (IsServer && IsSpawned)
            {
                isAvailable.Value = true;
                ApplyAvailability(true);
            }

            respawnRoutine = null;
        }

        private void HandleAvailabilityChanged(bool previousValue, bool currentValue)
        {
            ApplyAvailability(currentValue);
        }

        private void ApplyAvailability(bool available)
        {
            if (pickupCollider != null)
            {
                pickupCollider.enabled = available;
            }

            if (pickupRenderers == null)
            {
                return;
            }

            foreach (Renderer pickupRenderer in pickupRenderers)
            {
                if (pickupRenderer != null)
                {
                    pickupRenderer.enabled = available;
                }
            }
        }
    }
}
