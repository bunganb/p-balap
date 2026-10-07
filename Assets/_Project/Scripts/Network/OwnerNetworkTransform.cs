using Unity.Netcode.Components;

namespace PBalap.Network
{
    /// <summary>
    /// Replicates the server-simulated kart transform through NGO.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        private const int JitterBufferTicks = 3;

        public override void OnNetworkSpawn()
        {
            // Keep several snapshots buffered on observing clients. At the scene's
            // 30 Hz tick rate this adds about 100 ms of visual delay, allowing short
            // bursts of jitter/loss to arrive before the render timeline needs them.
            InterpolationBufferTickOffset = System.Math.Max(
                InterpolationBufferTickOffset,
                JitterBufferTicks);
            base.OnNetworkSpawn();
        }

        protected override bool OnIsServerAuthoritative()
        {
            return true;
        }
    }
}
