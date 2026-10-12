# Multiplayer architecture baseline

1. `NetworkManager`/Transport establishes Host or Client.
2. Lobby/Relay handles room discovery and internet connectivity.
3. The server/Host is authoritative for race state, checkpoints, laps, finish time, pickups, and item effects. Each kart owner is authoritative for its movement.
4. Each vehicle owner reads local input (`W/S/A/D`, `Space`) and simulates its own
   Rigidbody. `OwnerNetworkTransform` sends the owner's position and rotation to the
   Host and the other clients at the configured 30 Hz network tick rate.
5. Non-owner vehicles are kinematic and use NetworkTransform interpolation to display
   the received movement. There is no separate position reconciliation RPC or input
   replay. The Host still owns race state, item effects, stun, and lap decisions.
6. Position-dependent gameplay requests must be validated on the Host. The current
   `NetworkDebugOverlay` is a placeholder and does not yet report network metrics.
