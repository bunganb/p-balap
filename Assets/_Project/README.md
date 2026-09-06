# P, Balap! project structure

This folder contains project-owned assets for the multiplayer kart-racing prototype described in the GDD.

## Team ownership

| Owner | Folder | Responsibility |
| --- | --- | --- |
| Bunga - Network | `Scripts/Network`, `Prefabs/Network` | Netcode, Host/Client, Relay/Lobby, spawn, ownership, disconnect |
| Michael - Vehicle | `Scripts/Vehicle`, `Prefabs/Vehicles` | Input, server movement, interpolation, prediction/reconciliation |
| Iqbal - Race | `Scripts/Race`, `Scenes/Race` | Track, grid, checkpoints, laps, countdown, finish, results |
| Fariz - Item | `Scripts/Items`, `Prefabs/Items` | Pickup, one-slot inventory, validation, cooldown, item effects |
| QA/Integration | `Tests`, `Documentation`, `Scenes/Bootstrap` | Build, two-instance tests, latency/packet-loss checks, integration notes |

## Scene flow

`Bootstrap/Menu` -> `Lobby` -> `Race` -> `Results` -> `Lobby`.

The server/Host owns race state, checkpoint validation, lap/finish ordering, collision outcomes, and pickup/item validation. Clients send input and present replicated state with smoothing.

## Branch convention

Use one branch per workstream: `feature/network`, `feature/vehicle`, `feature/race`, `feature/items`, and `qa/integration`. Merge through `develop`; keep `main` buildable.
