using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PBalap.Items
{
    /// <summary>Server-side spawn point for networked item pickups.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class spawnPoint : NetworkBehaviour
    {
        [SerializeField] private BombPickup itemPrefab;
        [SerializeField] private Transform itemSpawnTransform;
        [SerializeField, Min(1)] private int itemCount = 1;
        [SerializeField, Min(0f)] private float itemSpacing = 2f;

        private readonly List<BombPickup> currentItems = new List<BombPickup>();

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                PlayerLap.LapCompletedOnServer += HandleLapCompleted;
                SpawnMissingItems();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                PlayerLap.LapCompletedOnServer -= HandleLapCompleted;
            }
        }

        public void NotifyItemCollected(BombPickup item)
        {
            if (!IsServer || item == null)
            {
                return;
            }

            int itemIndex = currentItems.IndexOf(item);
            if (itemIndex < 0)
            {
                return;
            }

            currentItems[itemIndex] = null;
            CleanupMissingItems();
        }

        private void HandleLapCompleted()
        {
            if (!IsServer)
            {
                return;
            }

            CleanupMissingItems();
            SpawnMissingItems();
        }

        private void SpawnMissingItems()
        {
            if (!IsServer || itemPrefab == null)
            {
                return;
            }

            while (currentItems.Count < itemCount)
            {
                currentItems.Add(null);
            }

            for (int index = 0; index < itemCount; index++)
            {
                if (currentItems[index] == null)
                {
                    SpawnItem(index);
                }
            }
        }

        private void SpawnItem(int index)
        {
            Transform spawnTransform = itemSpawnTransform != null
                ? itemSpawnTransform
                : transform;
            float centeredIndex = index - (itemCount - 1) * 0.5f;
            Vector3 position = spawnTransform.position
                + spawnTransform.right * (centeredIndex * itemSpacing);
            BombPickup item = Instantiate(itemPrefab, position, spawnTransform.rotation);
            item.InitializeSpawnPoint(this);

            NetworkObject networkObject = item.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError(
                    $"[spawnPoint] Item prefab {itemPrefab.name} must have a NetworkObject.",
                    this);
                Destroy(item.gameObject);
                return;
            }

            networkObject.Spawn();
            currentItems[index] = item;
        }

        private void CleanupMissingItems()
        {
            for (int index = 0; index < currentItems.Count; index++)
            {
                BombPickup item = currentItems[index];
                if (item == null || !item.IsSpawned)
                {
                    currentItems[index] = null;
                }
            }
        }
    }
}
