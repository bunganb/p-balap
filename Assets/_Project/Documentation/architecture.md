# Multiplayer architecture baseline

1. `NetworkManager`/Transport establishes Host or Client.
2. Lobby/Relay handles room discovery and internet connectivity.
3. The server/Host is authoritative for race state, checkpoints, laps, finish time, collision result, pickups, and item effects.
4. Each vehicle owner sends sequenced input (`W/S/A/D`, `Space`) to the server through
   `NetworkKartPlayer`. Input changes are sent immediately, steady-state input is capped
   at the 30 Hz network tick, and the unreliable heartbeat is sequence-validated so stale
   input packets are ignored without building a reliable-message backlog.
5. The local owner predicts movement immediately with the same arcade controller. The
   server applies the accepted input to its authoritative Rigidbody, and
   `OwnerNetworkTransform` replicates authoritative snapshots back to clients.
   Remote vehicles use NetworkTransform interpolation and the owner is corrected by
   the authoritative snapshot stream.
6. Debug UI exposes ping, tick, packet loss, and correction distance.
