# Multiplayer architecture baseline

1. `NetworkManager`/Transport establishes Host or Client.
2. Lobby/Relay handles room discovery and internet connectivity.
3. The server/Host is authoritative for race state, checkpoints, laps, finish time, collision result, pickups, and item effects.
4. Each vehicle owner sends input (`W/S/A/D`, `Space`) to the server.
5. The vehicle applies prediction for the local driver, then reconciles against server snapshots. Remote vehicles use interpolation.
6. Debug UI exposes ping, tick, packet loss, and correction distance.
