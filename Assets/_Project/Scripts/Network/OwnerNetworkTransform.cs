using Unity.Netcode.Components;

namespace PBalap.Network
{
    /// <summary>
    /// Lets the owning client publish its kart transform through NGO.
    /// This is intended for the current movement replication test; race-critical
    /// results remain controlled by the Host.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
