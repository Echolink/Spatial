# Spatial — Game Server Integration Guide

This document is written for game server developers integrating the Spatial physics and pathfinding system. It covers what the system does, how it is structured, what each piece of the API does, and how to wire everything together in a real server.

---

## What Spatial Does

Spatial is a **server-authoritative movement engine** for multiplayer game servers. It handles:

- **NavMesh generation** — Converts a 3D level mesh (`.obj`) into a walkable polygon network so agents know where they can go.
- **Pathfinding** — A* queries over the NavMesh to compute routes between two points.
- **Physics simulation** — BepuPhysics v2 capsule bodies with gravity, collision, and constraint-based movement.
- **Movement control** — Drives agents along their planned paths, handles terrain following, local agent avoidance, automatic replanning, knockback, and jump.

The game server only calls a small surface of the API — everything else runs automatically inside the simulation tick.

---

## Architecture Overview

```
Your Game Server
    └── World  (Spatial.Integration)           ← your main integration point
        ├── MovementController                  — path-following, local avoidance, replanning
        │   ├── PathfindingService              — DotRecast A* queries + path validation
        │   │   └── Pathfinder                  — raw NavMesh polygon queries
        │   └── MotorCharacterController        — BepuPhysics constraint-based movement
        └── PhysicsWorld                        — BepuPhysics v2 simulation (gravity, collisions)
```

**You interact with `World` only.** The subsystems below it are wired together automatically and exposed as escape hatches (`world.Physics`, `world.Movement`, `world.Pathfinding`) for advanced scenarios only.

---

## Module Overview

| Module | Assembly | Responsibility |
|---|---|---|
| `Spatial.Integration` | `Spatial.Integration.dll` | `World` façade, `MovementController`, `MotorCharacterController`, `PathfindingService` |
| `Spatial.Pathfinding` | `Spatial.Pathfinding.dll` | DotRecast wrapper, `Pathfinder`, `AgentConfig`, `NavMeshData` |
| `Spatial.Physics` | `Spatial.Physics.dll` | BepuPhysics v2 wrapper, `PhysicsWorld`, `PhysicsEntity` |
| `Spatial.MeshLoading` | `Spatial.MeshLoading.dll` | Wavefront OBJ loader |
| `Spatial.Server` | `Spatial.Server.dll` | Optional WebSocket server for Unity visualization |

---

## Core Concepts

### AgentConfig — Single Source of Truth

`AgentConfig` defines an agent's physical dimensions and traversal limits. **The same instance must be used** when baking the NavMesh and when spawning agents. If they differ, agents will fall through geometry or get stuck.

```csharp
var agentConfig = new AgentConfig
{
    Radius   = 0.4f,   // capsule radius (meters)
    Height   = 2.0f,   // capsule cylinder height (meters); total height = Height + 2*Radius
    MaxClimb = 0.5f,   // maximum step height (meters)
    MaxSlope = 45.0f,  // maximum walkable slope (degrees)
};
```

Built-in presets:

```csharp
AgentConfig.Player  // Radius=0.5, Height=2.0, MaxClimb=0.5, MaxSlope=45
AgentConfig.NPC     // Radius=0.4, Height=1.8, MaxClimb=0.3, MaxSlope=40
```

---

### NavMeshData — Baked Once, Shared Across Rooms

NavMesh baking voxelizes your level geometry and computes walkable polygons. It is expensive (100–500 ms) and must only happen **once per map per server start**. The resulting `NavMeshData` is read-only and thread-safe — share it across every room running that map.

```csharp
// At server startup, once per map (single agent size)
string meshPath = Path.Combine(AppContext.BaseDirectory, "worlds", "arena.obj");
NavMeshData bakedNavMesh = World.BakeNavMesh(meshPath, agentConfig);
```

The `.obj` mesh file should contain only the walkable and collidable geometry of your level (floor, walls, ramps). Export it from your 3D tool and place it in the `worlds/` folder.

---

### World — One Per Room Instance

A `World` instance represents one isolated game room. It has its own physics simulation and entity tracking. Creating a `World` is cheap (< 5 ms) because it reuses the shared `NavMeshData`.

```csharp
using var world = new World(bakedNavMesh, agentConfig);
```

Use a `using` statement or call `world.Dispose()` explicitly when the room ends to release BepuPhysics memory.

---

### Multi-Size Agents — Different Unit Sizes in the Same World

When a room contains units of meaningfully different sizes (e.g. goblins, humans, and trolls), each size needs its own NavMesh. This is because agent radius is baked into the NavMesh during voxelization — it shrinks walkable corridor widths at build time and cannot be adjusted per query.

**Why separate NavMeshes?**  
A large agent routed through a NavMesh baked for small agents will clip walls and squeeze into corridors it physically cannot fit through. The industry-standard solution (Unreal, Unity, StarCraft II) is one NavMesh per size tier.

#### Setup (server startup)

Define your size configs as long-lived instances — lookup uses **reference equality**, so the same object passed to `Add()` must be passed to `Spawn()`.

```csharp
// Define once — keep as static fields or singletons in your server
static readonly AgentConfig GoblinConfig = new() { Radius = 0.3f, Height = 1.4f, MaxClimb = 0.3f, MaxSlope = 40f };
static readonly AgentConfig HumanConfig  = new() { Radius = 0.5f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
static readonly AgentConfig TrollConfig  = new() { Radius = 1.2f, Height = 3.5f, MaxClimb = 0.8f, MaxSlope = 35f };

// Bake one NavMesh per config — expensive, do once at server startup
string meshPath = Path.Combine(AppContext.BaseDirectory, "worlds", "arena.obj");
MultiAgentNavMesh multiNavMesh = new MultiAgentNavMesh(meshPath)
    .Add(GoblinConfig)
    .Add(HumanConfig)
    .Add(TrollConfig)
    .Bake();
```

#### Room lifecycle

```csharp
// Cheap — reuses the already-baked NavMeshes
using var world = new World(multiNavMesh);

// Spawn with explicit config — capsule and NavMesh routing are both set correctly
world.Spawn(goblinId, spawnPos, GoblinConfig);
world.Spawn(playerId, spawnPos, HumanConfig);
world.Spawn(trollId,  spawnPos, TrollConfig, EntityType.Enemy);

// Move, Despawn, Update — all unchanged
world.Move(trollId, destination);
world.Update(fixedDeltaTime);
world.Despawn(goblinId);
```

#### What happens internally

| Step | What Spatial does |
|---|---|
| `Bake()` | Runs `BakeNavMesh()` for each registered config. Three configs → three bakes. |
| `new World(multiNavMesh)` | Builds one `PathfindingService` per config, backed by its own `NavMeshData`. |
| `Spawn(id, pos, config)` | Sizes the BepuPhysics capsule to `config.Radius`/`config.Height`. Snaps spawn position to the matching NavMesh. Registers the entity with its service for future Move calls. |
| `Move(id, target)` | Automatically routes path queries to the NavMesh that was baked for this entity's config. No extra call needed. |
| `Despawn(id)` | Cleans up entity config tracking and service registration. |

#### Pitfalls

**Use the same config instance at spawn time.** Lookup is by reference, not value. Creating `new AgentConfig { Radius = 0.5f, ... }` inline at `Spawn()` will not match the one registered in `MultiAgentNavMesh.Add()` and will fall back to the default NavMesh silently.

```csharp
// WRONG — creates a new instance, won't match
world.Spawn(id, pos, new AgentConfig { Radius = 0.5f, Height = 2.0f });

// CORRECT — reuse the same instance registered in .Add()
world.Spawn(id, pos, HumanConfig);
```

**Bake time scales with config count.** Each call to `Add()` adds one full bake (~100–500 ms) at startup. Limit to the size tiers your game actually uses — 3–5 is typical. Do not create one config per unit.

**Single-config worlds are unchanged.** If you only call `new World(bakedNavMesh, agentConfig)` and `world.Spawn(id, pos)`, the old API works identically — no migration needed.

---

### Fixed Timestep Simulation

The simulation runs at a fixed timestep. You must call `world.Update(deltaTime)` every tick with a **constant** delta time. Variable delta time breaks the BepuPhysics integration and produces non-deterministic results.

| Tick Rate | Delta Time | When to Use |
|---|---|---|
| 60 Hz | `0.016f` | Standard game server |
| 125 Hz | `0.008f` | High-fidelity or competitive |
| 50 Hz | `0.020f` | Conservative, high stability |

---

## API Reference

### Startup

```csharp
// ── Single agent size ──────────────────────────────────────────────────────
// Bake navigation mesh from .obj file (do once at server startup)
NavMeshData World.BakeNavMesh(string meshFilePath, AgentConfig agentConfig)

// Create a world for a room (do once per room/match/instance)
World world = new World(NavMeshData navMesh, AgentConfig agentConfig)

// ── Multiple agent sizes ───────────────────────────────────────────────────
// Bake one NavMesh per size tier (do once at server startup)
MultiAgentNavMesh multiNavMesh = new MultiAgentNavMesh(meshFilePath)
    .Add(smallConfig).Add(largeConfig).Bake()

// Create a world that supports all registered sizes
World world = new World(MultiAgentNavMesh multiNavMesh)
```

### Simulation Tick

```csharp
// Call every server tick with a fixed delta time
world.Update(float deltaTime)
```

### Agent Lifecycle

```csharp
// Add an agent at a position; position is automatically snapped to the NavMesh
world.Spawn(int entityId, Vector3 position)

// Add an agent with a specific size (multi-size worlds — pass same AgentConfig instance used in .Add())
world.Spawn(int entityId, Vector3 position, AgentConfig agentConfig)

// Remove an agent; safely stops movement before removing physics body
world.Despawn(int entityId)

// Instantly move an agent to a new position (respawn, GM teleport)
world.Teleport(int entityId, Vector3 position)
```

Entity IDs must be unique within a single `World` instance. Two separate worlds can both have an entity with id `1`.

### Obstacle Lifecycle

Obstacles are static collidable objects spawned at runtime — trees, crates, summoned walls. Spawning adds a physics collider and rebuilds the NavMesh tiles in the obstacle's footprint so agents route around it automatically.

```csharp
// Spawn a static obstacle; returns a SpawnResult
SpawnResult result = world.SpawnObstacle(int entityId, Vector3 position, Vector3 size,
                                         bool forceSpawn = false)

// Remove an obstacle; restores NavMesh walkability in the affected tiles
world.DespawnObstacle(int entityId)
```

`position` is the **base-centre** of the box (feet level). `size` is full dimensions (width, height, depth).

#### SpawnResult fields

| Field | Type | Meaning |
|---|---|---|
| `Spawned` | `bool` | `true` if the obstacle was created |
| `Entity` | `PhysicsEntity?` | The physics body; `null` if spawn was rejected |
| `DisplacedEntityIds` | `IReadOnlyList<int>` | Units that overlapped the footprint (see below) |

#### Overlap behaviour

If a unit occupies the spawn footprint the outcome depends on `forceSpawn`:

**`forceSpawn: false` (default — prevention-first)**

Spawn is rejected. `Spawned == false` and `DisplacedEntityIds` lists the blocking units. The game server should relocate those units and retry.

```csharp
var result = world.SpawnObstacle(treeId, position, treeSize);
if (!result.Spawned)
{
    foreach (var unitId in result.DisplacedEntityIds)
        world.Move(unitId, safeNearbyPoint);
    // retry once units have moved away
}
```

**`forceSpawn: true` (scripted / world-event spawns)**

Blocking units are pushed to the nearest point outside the obstacle's footprint before the static body is created — no physics launch, no penetration. Their movement is stopped; re-issue `Move()` so they resume pathing.

```csharp
var result = world.SpawnObstacle(treeId, position, treeSize, forceSpawn: true);
foreach (var unitId in result.DisplacedEntityIds)
    world.Move(unitId, theirOriginalDestination);
```

NavMesh tile updates only occur when the world was baked with `NavMeshConfiguration.EnableTileUpdates = true`. On a monolithic NavMesh the physics collider still blocks units, but the pathfinder will not route around the obstacle.

---

### Movement Commands

```csharp
// Issue a move order; returns details about the path
MovementResponse response = world.Move(int entityId, Vector3 target, float speed = 5f)

// Stop an agent in place (keeps it in the world)
world.StopMove(int entityId)
```

`MovementResponse` fields:

| Field | Type | Meaning |
|---|---|---|
| `Success` | `bool` | Whether any path was found |
| `WasTargetAdjusted` | `bool` | Target was unreachable; a nearby point was used |
| `AdjustmentReason` | `string?` | Why the target was adjusted |
| `ActualStart` | `Vector3` | NavMesh-snapped start position |
| `ActualTarget` | `Vector3` | NavMesh-snapped destination |
| `EstimatedTime` | `float` | Seconds at current speed |
| `IsPartialPath` | `bool` | Path only reaches part-way to target |

The system applies a **4-tier fallback** automatically:
1. Direct path to target
2. Nearest reachable point near the target (within 5 m)
3. Partial path toward the target
4. Failure (no walkable path found at all)

### Physics Abilities

```csharp
// Knock agent in a direction with force (for combat)
world.Knockback(int entityId, Vector3 direction, float force)

// Make an agent jump (only works when grounded)
bool jumped = world.Jump(int entityId, float jumpForce = 5f)
```

After a knockback, pathfinding resumes automatically when the agent lands.

### State Queries

```csharp
// Authoritative world-space position (send this to clients)
Vector3  world.GetPosition(int entityId)

// Current velocity (useful for animation blending)
Vector3  world.GetVelocity(int entityId)

// Grounded / airborne / recovering
CharacterState world.GetState(int entityId)  // GROUNDED | AIRBORNE | RECOVERING

// Check if a position is on the walkable NavMesh
bool     world.IsValidPosition(Vector3 position)

// Snap an arbitrary position to the nearest NavMesh surface
Vector3? world.SnapToNavMesh(Vector3 position)
```

`CharacterState` uses:
- `GROUNDED` — on the floor; abilities and movement apply
- `AIRBORNE` — mid-air (jumping or knocked back); pathfinding paused
- `RECOVERING` — just landed; one short grace period before pathfinding resumes

### Events

Subscribe before spawning agents so you do not miss early events.

```csharp
// An agent has started moving (positions are NavMesh-snapped)
world.OnMovementStarted += (entityId, startPos, targetPos) => { };

// An agent has reached its destination
world.OnDestinationReached += (entityId, finalPos) => { };

// Path was automatically replanned (no action needed from your code)
world.OnPathReplanned += (entityId) => { };

// Agent advanced to next waypoint; fraction is 0.0 → 1.0
world.OnMovementProgress += (entityId, fraction) => { };
```

### Cleanup

```csharp
world.Dispose()  // releases BepuPhysics memory — call when room ends
```

---

## Integration Workflow

### Server Startup

```
1. Load configuration (map names, agent presets, tick rate)
2. For each map: World.BakeNavMesh(meshPath, agentConfig) → store NavMeshData
3. Start networking (accept player connections)
4. For each new room: new World(bakedNavMesh, agentConfig)
```

### Room Lifecycle

```
Room opens
  → new World(bakedNavMesh, agentConfig)
  → subscribe to world events

Player joins
  → world.Spawn(playerId, spawnPoint)
  → let settle for a few ticks

Gameplay loop (every tick)
  → apply player input commands (Move, Jump, Knockback, etc.)
  → world.Update(fixedDeltaTime)
  → read positions: world.GetPosition(id)
  → broadcast state to clients

Player leaves
  → world.Despawn(playerId)

Room ends
  → world.Dispose()
```

### Client State Broadcast

After every `world.Update()`, read positions and broadcast to connected clients:

```csharp
foreach (var playerId in activePlayers)
{
    var pos   = world.GetPosition(playerId);
    var vel   = world.GetVelocity(playerId);
    var state = world.GetState(playerId);
    // serialize and send to clients
}
```

---

## Common Pitfalls

**AgentConfig mismatch** — If the NavMesh was baked with different values than those used at spawn time, agents will fall through geometry or navigate to unreachable polygons. In single-config worlds, pass the same `AgentConfig` instance to both `BakeNavMesh` and `new World(...)`. In multi-size worlds, pass the same instance to `MultiAgentNavMesh.Add()` and `world.Spawn()`.

**Wrong instance at Spawn (multi-size)** — `MultiAgentNavMesh` uses reference equality to look up the NavMesh for a spawned entity. Passing `new AgentConfig { Radius = 0.5f }` inline at Spawn time will not match the instance registered in `Add()` and silently falls back to the default NavMesh. Keep configs as static fields and reuse them.

**Variable delta time** — Passing a variable `deltaTime` to `world.Update()` breaks the BepuPhysics integration. Always use a fixed timestep (e.g., `0.016f`).

**Forgetting to settle after spawn** — Agents need a few physics ticks to stabilize after spawning (gravity settles the capsule onto the surface). Run 20–30 ticks before issuing the first `Move()`.

**Skipping Dispose** — `World.Dispose()` releases unmanaged BepuPhysics memory. If you omit it, that memory leaks. Use a `using` block.

**Despawning without stopping** — `world.Despawn()` handles this correctly already. Never remove an entity from the physics world manually without going through `world.Despawn()`.

**Re-baking every room** — `BakeNavMesh` takes 100–500 ms. Call it once at startup, not each time a room opens.

**Spawning an obstacle on top of a unit** — Without overlap checking, BepuPhysics would generate a violent depenetration impulse that launches the unit unpredictably. Always use `SpawnObstacle`'s return value: check `Spawned` and handle `DisplacedEntityIds`, or pass `forceSpawn: true` for scripted events that must succeed unconditionally.

---

## Optional: NavMesh Tile Updates (Dynamic Geometry)

For maps with doors, destructible bridges, or collapsible floors, bake with tile support enabled:

```csharp
var navConfig = new NavMeshConfiguration { EnableTileUpdates = true };
var bakedNavMesh = World.BakeNavMesh(meshPath, agentConfig, navConfig);
```

This produces a tiled NavMesh. You can then update individual tiles at runtime (e.g., remove walkable polygons behind a closed gate) without re-baking the entire mesh.

---

## Optional: Visualization Server

A WebSocket visualization server is included for debugging with the Unity client.

```csharp
var vizServer = new VisualizationServer();
vizServer.Start(8181);

// In your game loop, broadcast state after each world.Update()
if (vizServer.HasClients())
{
    var state = new SimulationState { /* fill from world.GetPosition() etc. */ };
    vizServer.BroadcastState(state);
}

// On shutdown
vizServer.Stop();
```

The Unity client (`Unity/QUICK_START.md`) connects to `ws://localhost:8181` and renders agent positions, paths, and the NavMesh in real time.

---

## File Locations

| File | Purpose |
|---|---|
| `Spatial.Integration/World.cs` | Full façade API with doc comments |
| `Spatial.Pathfinding/AgentConfig.cs` | All AgentConfig fields |
| `Spatial.Integration/PathfindingConfiguration.cs` | Tuning parameters for pathfinding |
| `Spatial.Integration/MovementController.cs` | Internal movement pipeline |
| `Spatial.TestHarness/GameServerIntegrationSample.cs` | Runnable scenarios covering all features |
| `Spatial.TestHarness/MyGameServer.cs` | Example game server skeleton with placeholders |

Run the integration sample with:

```bash
dotnet run --project Spatial.TestHarness -- sample
```
