using UnityEngine;

namespace PBalap.UI
{
    /// <summary>HUD integration point for ping, tick, packet loss, and correction distance.</summary>
    public sealed class NetworkDebugOverlay : MonoBehaviour
    {
        [SerializeField] private bool showByDefault = true;

        public bool ShowByDefault => showByDefault;
    }
}
