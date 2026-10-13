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
        [SerializeField, Min(0.1f)] private float throwDuration = 4f;
        [SerializeField, Min(0f)] private float throwArcHeight = 2.5f;
        [SerializeField, Min(0.1f)] private float hitRadius = 1.25f;
        [SerializeField, Min(0.1f)] private float homingSpeed = 14f;
        [SerializeField, Min(0.1f)] private float maximumChaseDistance = 16f;
        [SerializeField, Min(0f)] private float homingDelay = 0.25f;
        [SerializeField, Min(0.1f)] private float gravity = 15f;
        [SerializeField] private ParticleSystem explosionEffect;
        [SerializeField, Min(0f)] private float impactCleanupDelay = 0.2f;
        [SerializeField] private Collider pickupCollider;

        private readonly NetworkVariable<bool> isAvailable = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> isThrown = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Renderer[] pickupRenderers;
        private Coroutine respawnRoutine;
        private spawnPoint sourceSpawnPoint;
        private bool isHeld;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private readonly Collider[] hitColliders = new Collider[16];

        public bool IsAvailable => isAvailable.Value;

        public void InitializeSpawnPoint(spawnPoint source)
        {
            if (sourceSpawnPoint == null)
            {
                sourceSpawnPoint = source;
            }
        }

        private void Awake()
        {
            if (pickupCollider == null)
            {
                pickupCollider = GetComponent<Collider>();
            }

            pickupRenderers = GetComponentsInChildren<Renderer>(true);
            if (explosionEffect == null)
            {
                explosionEffect = transform.root.GetComponentInChildren<ParticleSystem>(true);
            }

            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        public override void OnNetworkSpawn()
        {
            isAvailable.OnValueChanged += HandleAvailabilityChanged;
            isThrown.OnValueChanged += HandleThrownChanged;
            ApplyAvailability(isAvailable.Value);
        }

        public override void OnNetworkDespawn()
        {
            isAvailable.OnValueChanged -= HandleAvailabilityChanged;
            isThrown.OnValueChanged -= HandleThrownChanged;
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
            isHeld = true;
            ApplyAvailability(false);

            if (sourceSpawnPoint != null)
            {
                sourceSpawnPoint.NotifyItemCollected(this);
            }
            else
            {
                if (respawnRoutine != null)
                {
                    StopCoroutine(respawnRoutine);
                }

                respawnRoutine = StartCoroutine(RespawnAfterDelay());
            }
            Debug.Log(
                $"[BombPickup] Player {player.OwnerClientId} picked up {name}. "
                + $"Inventory={player.BombCount}.");
        }

        public void ThrowFromServer(
            Vector3 position,
            Vector3 direction,
            NetworkKartPlayer target,
            NetworkKartPlayer thrower)
        {
            if (!IsServer || !IsSpawned || isAvailable.Value || !isHeld)
            {
                return;
            }

            isHeld = false;
            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }

            respawnRoutine = StartCoroutine(ThrowRoutine(position, direction, target, thrower));
        }

        private IEnumerator ThrowRoutine(
            Vector3 position,
            Vector3 direction,
            NetworkKartPlayer target,
            NetworkKartPlayer thrower)
        {
            Vector3 throwDirection = Vector3.ProjectOnPlane(
                direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward,
                Vector3.up).normalized;
            if (throwDirection.sqrMagnitude < 0.001f)
            {
                throwDirection = Vector3.forward;
            }

            float elapsed = 0f;
            float verticalVelocity = Mathf.Sqrt(2f * gravity * throwArcHeight);

            transform.SetPositionAndRotation(position, Quaternion.LookRotation(throwDirection));
            SetThrownState();

            while (elapsed < throwDuration)
            {
                float deltaTime = Time.deltaTime;
                elapsed += deltaTime;

                if (target != null
                    && (!target.IsSpawned
                        || !target.CanDrive
                        || target == thrower
                        || IsBeyondChaseDistance(thrower, target)))
                {
                    target = null;
                }

                Vector3 previousPosition = transform.position;
                if (target != null && elapsed >= homingDelay)
                {
                    Vector3 targetPosition = target.transform.position + Vector3.up * 0.55f;
                    Vector3 toTarget = targetPosition - transform.position;
                    transform.position = Vector3.MoveTowards(
                        transform.position,
                        targetPosition,
                        homingSpeed * deltaTime);
                    if (toTarget.sqrMagnitude > 0.001f)
                    {
                        transform.rotation = Quaternion.LookRotation(toTarget.normalized);
                    }
                }
                else
                {
                    verticalVelocity -= gravity * deltaTime;
                    transform.position += throwDirection * (throwSpeed * deltaTime)
                        + Vector3.up * (verticalVelocity * deltaTime);
                    transform.rotation = Quaternion.LookRotation(throwDirection);
                }

                if (target != null && FindHitTarget(target) && target.StunOnServer())
                {
                    PlayExplosionEffectRpc(transform.position);
                    yield return new WaitForSeconds(impactCleanupDelay);
                    FinishThrownItem();
                    yield break;
                }

                if (TryGetLandingPoint(previousPosition, transform.position, out Vector3 landingPoint))
                {
                    PlayExplosionEffectRpc(landingPoint);
                    yield return new WaitForSeconds(impactCleanupDelay);
                    FinishThrownItem();
                    yield break;
                }

                yield return null;
            }

            FinishThrownItem();
        }

        private bool IsBeyondChaseDistance(NetworkKartPlayer thrower, NetworkKartPlayer target)
        {
            if (thrower == null || !thrower.IsSpawned)
            {
                return true;
            }

            Vector3 offset = target.transform.position - thrower.transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude > maximumChaseDistance * maximumChaseDistance;
        }

        private bool TryGetLandingPoint(
            Vector3 previousPosition,
            Vector3 currentPosition,
            out Vector3 landingPoint)
        {
            landingPoint = currentPosition;
            Vector3 displacement = currentPosition - previousPosition;
            if (displacement.y >= 0f || displacement.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            if (Physics.Raycast(
                    previousPosition,
                    displacement.normalized,
                    out RaycastHit hit,
                    displacement.magnitude,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y <= 0.35f)
                {
                    return false;
                }

                landingPoint = hit.point + hit.normal * 0.08f;
                return true;
            }

            return false;
        }

        [Rpc(SendTo.Everyone, Delivery = RpcDelivery.Reliable)]
        private void PlayExplosionEffectRpc(Vector3 position)
        {
            if (explosionEffect == null)
            {
                return;
            }

            GameObject effect = Instantiate(explosionEffect.gameObject, position, Quaternion.identity);
            effect.name = "BombExplosionVFX";
            effect.SetActive(true);

            ParticleSystem particleSystem = effect.GetComponent<ParticleSystem>();
            if (particleSystem == null)
            {
                Destroy(effect);
                return;
            }

            particleSystem.Clear(true);
            particleSystem.Play(true);
            ParticleSystem.MainModule main = particleSystem.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
        }

        private void FinishThrownItem()
        {
            if (sourceSpawnPoint != null)
            {
                if (IsSpawned)
                {
                    NetworkObject.Despawn(true);
                }

                return;
            }

            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            isThrown.Value = false;
            isAvailable.Value = true;
            ApplyAvailability(true);
            respawnRoutine = null;
        }

        private bool FindHitTarget(NetworkKartPlayer target)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                hitRadius,
                hitColliders,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                NetworkKartPlayer hitPlayer = hitColliders[i] != null
                    ? hitColliders[i].GetComponentInParent<NetworkKartPlayer>()
                    : null;
                if (hitPlayer == target && hitPlayer.IsSpawned && hitPlayer.CanDrive)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetThrownState()
        {
            if (IsServer)
            {
                isThrown.Value = true;
            }

            ApplyAvailability(isAvailable.Value);
        }

        private void HandleThrownChanged(bool previousValue, bool currentValue)
        {
            ApplyAvailability(isAvailable.Value);
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
                    pickupRenderer.enabled = available || isThrown.Value;
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

    }
}
