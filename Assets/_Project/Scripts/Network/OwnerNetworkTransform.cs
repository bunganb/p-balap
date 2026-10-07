using Unity.Netcode.Components;

namespace PBalap.Network
{
    /// <summary>
    /// Replicates the server-simulated kart transform through NGO.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        private const float LocalOwnerPositionSmoothing = 0.08f;
        private const float LocalOwnerRotationSmoothing = 0.06f;

        public override void OnNetworkSpawn()
        {
            // This setting is static across every NetworkTransform. An additional
            // global buffer also delayed the non-host owner's own kart, so rely on
            // NGO's measured tick latency instead of forcing extra ticks globally.
            InterpolationBufferTickOffset = 0;
            base.OnNetworkSpawn();

            if (IsOwner && !IsServer)
            {
                // The local client still receives a server-authoritative pose, but
                // must not use the heavier smoothing intended for remote observers.
                PositionMaxInterpolationTime = LocalOwnerPositionSmoothing;
                RotationMaxInterpolationTime = LocalOwnerRotationSmoothing;
            }
        }

        protected override bool OnIsServerAuthoritative()
        {
            return true;
        }
    }
}
