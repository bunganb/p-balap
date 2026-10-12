using Unity.Netcode.Components;

namespace PBalap.Network
{
    /// <summary>
    /// Replicates the owner-simulated kart transform through NGO.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
