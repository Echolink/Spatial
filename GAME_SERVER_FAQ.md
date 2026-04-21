# Game Server Integration — FAQ

**Related**: `GAME_SERVER_INTEGRATION.md`  
**Last Updated**: 2026-04-07

---

## Q1. How do I change the world mesh path away from `Spatial.TestHarness/worlds/`?

The path is just a string you pass to `World.BakeNavMesh()`. Nothing is hardcoded in Spatial — the integration guide uses `AppContext.BaseDirectory` as a convention for the test harness. In your game server, use whatever path fits your deployment:

```csharp
// Absolute path
string meshPath = "/srv/gameserver/worlds/dungeon.obj";

// Relative to the server executable
string meshPath = Path.Combine(AppContext.BaseDirectory, "worlds", "dungeon.obj");

// From config or environment variable
string meshPath = config["WorldMeshPath"];

NavMeshData baked = World.BakeNavMesh(meshPath, agentConfig);
```

The `.csproj` copy-to-output rule in `Spatial.TestHarness` is a test convenience only. Your server just needs the `.obj` file readable at the path you supply — put it wherever your deployment requires.

---

## Q2. What are the valid `EntityType` values when calling `world.Spawn`?

```csharp
EntityType.Player           // Player character — collides with world + NPCs, not other players
EntityType.NPC              // Friendly NPC — collides with world + players
EntityType.Enemy            // Hostile NPC
EntityType.StaticObject     // Immovable level prop (barrel, crate) — blocks movement, does not pathfind
EntityType.Obstacle         // Dynamic blocker — can be temporary or permanent, blocks pathfinding
EntityType.TemporaryObstacle // Like Obstacle but intended for auto-despawn use cases
EntityType.Projectile       // Spell, arrow, etc. — used for collision pair filtering
```

The primary effect of `EntityType` is **collision filtering**. For example:
- `Player` entities do not collide with each other, but do collide with `NPC` and `Enemy`.
- `StaticObject` has a physics body (blocks movement) but is never given a path.
- `Projectile` is registered in collision handlers via `CollisionEventSystem.RegisterHandler`.

Use `Player` for player-controlled characters, `NPC`/`Enemy` for server-controlled units, and `StaticObject` for decoration that just needs a physics presence.

---

## Q3. When is `OnPathReplanned` triggered?

`OnPathReplanned` fires when the system **successfully** computes a new path automatically, without you calling `Move()` again. It is triggered from the periodic validation tick (every `PathfindingConfiguration.PathValidationInterval` seconds). There are two conditions that cause a replan attempt:

1. **Stuck detection** — the unit moved less than `StuckDetectionThreshold` meters for `StuckDetectionCount` consecutive validation intervals.
2. **Waypoint invalidation** — the next `PathValidationLookaheadWaypoints` waypoints ahead are checked against the live NavMesh. If any are no longer on a valid walkable surface (e.g. after a `RebuildNavMeshRegion` call), a replan is triggered from the unit's current position.

If the replan succeeds, `OnPathReplanned` fires and the unit continues moving on the new path. If the replan fails, movement is stopped.

**You do not need to call `Move()` again after receiving this event.** The unit handles it internally.

---

## Q4. What happens to units when `RebuildNavMeshRegion` is called?

There are two cases:

**Unit walking through the rebuilt area**  
The current path is not cancelled immediately. On the next `PathValidationInterval` tick the system validates the lookahead waypoints against the updated NavMesh. If any are now invalid (e.g. a bridge tile was removed), an automatic replan is triggered from the unit's current position — `OnPathReplanned` fires and the unit reroutes.

**Unit standing on top of the rebuilt area**  
Physics bodies are fully decoupled from the NavMesh. The tile rebuild only changes pathfinding queries — it does not move, teleport, or drop the unit. The unit will continue to stand there via physics. If it then issues a new `Move()` command, the path will reflect the updated geometry.

**Prerequisite**: `RebuildNavMeshRegion` requires the NavMesh to have been baked with `EnableTileUpdates = true`. Calling it on a monolithic NavMesh returns `0` and does nothing.

```csharp
// Must be baked with this config for tile updates to work
var navConfig = new NavMeshConfiguration { EnableTileUpdates = true, TileSize = 32.0f };
NavMeshData baked = World.BakeNavMesh(meshPath, agentConfig, navConfig);
```

---

## Q5. Can I bake the NavMesh once and save it to a file to avoid rebaking on restart?

**Not currently.** `NavMeshData` wraps a DotRecast `DtNavMesh` object and there is no binary serialization path in the codebase. (`NavMeshData.ExportToObj` exists, but that is for visualization only — it cannot be reloaded.)

However, the bake cost is addressed by **sharing one bake across all rooms for the same map**. Since `NavMeshData` is read-only and thread-safe, a single bake can serve hundreds of concurrent rooms:

```csharp
// At process startup — bake once per map (100–500 ms each)
var maps = new Dictionary<string, NavMeshData>
{
    ["dungeon"]   = World.BakeNavMesh(Path.Combine(baseDir, "worlds/dungeon.obj"),   agentConfig),
    ["arena"]     = World.BakeNavMesh(Path.Combine(baseDir, "worlds/arena.obj"),     agentConfig),
    ["overworld"] = World.BakeNavMesh(Path.Combine(baseDir, "worlds/overworld.obj"), agentConfig),
};

// Per room open — no rebake, just a new World wrapping the shared bake
var world = new World(maps["dungeon"], agentConfig, pfConfig);
```

In practice this means "once per server process per map", not "once per room per session". Server restarts will rebake, which is typically acceptable as startup overhead.

If bake time becomes a bottleneck, DotRecast supports binary tile serialization at the library level — a save/load path could be added on top of `NavMeshData`, but it is not implemented yet.

---

## Q6. What does `BroadcastSnapshotToClients(snapshot)` do?

It is **pseudocode** — a placeholder in the guide representing your own network send. Spatial does not include a networking layer. You replace it with whatever transport your server uses:

```csharp
// Example — replace with your actual networking code
void BroadcastSnapshotToClients(WorldSnapshot snapshot)
{
    byte[] packet = mySerializer.Serialize(snapshot);
    foreach (var client in connectedClients)
        client.Send(packet);
}
```

The only Spatial-side networking in this repository is `Spatial.Server` — a Fleck WebSocket server that streams simulation state to the Unity visualizer. That is a development tool, not part of the production `World` API, and is not used in real game server deployments.
