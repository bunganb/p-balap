using UnityEngine;

namespace PBalap.Items
{
    /// <summary>Server-validated pickup and one-slot inventory integration point.</summary>
    public sealed class ItemPickup : MonoBehaviour
    {
        [SerializeField] private float cooldownSeconds = 3f;

        public float CooldownSeconds => cooldownSeconds;
    }
}
