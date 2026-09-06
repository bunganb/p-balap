# Prefab workflow

Create reusable prefabs here and keep networking components on the root object.

- `Network/NetworkManager.prefab` - NetworkManager, Unity Transport, bootstrap hooks.
- `Vehicles/Kart.prefab` - NetworkObject, authoritative vehicle controller, visual child, collider.
- `Race/Checkpoint.prefab` - trigger collider and checkpoint identifier.
- `Items/PalmOilPickup.prefab` - NetworkObject, pickup trigger, server cooldown.
- `Items/HoverBomb.prefab` - NetworkObject, projectile/effect visuals, server hit validation.
- `UI/` - lobby, countdown, race HUD, result, and network debug widgets.

Use the matching script folder for component code and keep prefab changes isolated to the feature branch owning them.
