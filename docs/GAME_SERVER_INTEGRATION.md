# Game Server Integration Guide

**Last Updated**: 2026-04-19  
**Status**: Production Ready  
**Primary API**: `World` façade (`Spatial.Integration`)

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Creating a World](#2-creating-a-world)
3. [Spawning and Despawning Units](#3-spawning-and-despawning-units)
   - [Resource Node Obstacles](#resource-node-obstacles-trees-rocks-etc)
   - [Multi-size Agents](#multi-size-agents)
4. [Requesting Unit Movement](#4-requesting-unit-movement)
5. [Events and Callbacks](#5-events-and-callbacks)
6. [Reading Unit Positions and States](#6-reading-unit-positions-and-states)
7. [Physics Interactions (Knockback, Jump, Push)](#7-physics-interactions)
8. [Runtime NavMesh Updates (Doors, Destructibles)](#8-runtime-navmesh-updates)
9. [The Game Loop](#9-the-game-loop)
10. [Multi-Room / Multi-Instance Servers](#10-multi-room--multi-instance-servers)
11. [Configuration Reference](#11-configuration-reference)
12. [Production Checklist](#12-production-checklist)

---

## 1. Architecture Overview

Spatial is a **server-authoritative** physics + pathfinding library. Your game server owns all gameplay logic (health, skills, sessions) and delegates position authority entirely to Spatial.

```
Your Game Server
      │
      │  world.Spawn()   world.Move()   world.Update()
      ▼
┌─────────────────────────────────────────────────┐
│  World  (Spatial.Integration)                   │
│  ── single entry point for the whole system ──  │
│                                                 │
│  ┌─────────────────────┐  ┌────────────────┐   │
│  │  MovementController  │  │ PathfindingService│  │
│  │  (Option D fallback) │  │ (DotRecast)    │   │
│  └──────────┬──────────┘  └───────┬────────┘   │
│             │                     │             │
│  ┌──────────▼─────────────────────▼──────────┐  │
│  │  PhysicsWorld  (BepuPhysics v2)           │  │
│  │  — position, velocity, collision, gravity │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
      │
      │  OnDestinationReached   OnPathReplanned
      │  OnMovementStarted      OnMovementProgress
      ▼
Your Game Server (event handlers)
```

### What Spatial owns (authoritative)

| Data | Who owns it | How you read it |
|------|-------------|-----------------|
| Unit position | PhysicsWorld | `world.GetPosition(id)` |
| Unit velocity | PhysicsWorld | `world.GetVelocity(id)` |
| Ground/air state | MotorCharacterController | `world.GetState(id)` |
| Valid NavMesh surfaces | PathfindingService | `world.IsValidPosition(pos)` |

### What you own

- Health, mana, skills, cooldowns
- Session / room membership
- Inventory, quests
- When to issue Move commands

---

## 2. Creating a World

### Step 1 — Bake the NavMesh (once per map, at startup)

NavMesh baking is CPU-intensive (100–500 ms). Do it once when the server starts and reuse the result across every room that uses the same map.

```csharp
using Spatial.Integration;
using Spatial.Pathfinding;

// AgentConfig is the single source of truth for agent dimensions.
// The SAME instance must be used for baking, pathfinding, and movement.
var agentConfig = new AgentConfig
{
    Height   = 2.0f,   // capsule cylinder length (meters)
    Radius   = 0.4f,   // capsule hemisphere radius (meters)
    MaxSlope = 45.0f,  // steepest walkable slope (degrees)
    MaxClimb = 0.5f,   // max step height the agent can climb (meters)
};

// Resolve the path to the .obj file from the executable's directory.
// Place world files in:  Spatial.TestHarness/worlds/  (copied to output by .csproj)
string meshPath = Path.Combine(AppContext.BaseDirectory, "worlds", "dungeon.obj");

// BakeNavMesh loads the mesh, voxelises it, and produces a read-only NavMeshData.
// This can be shared safely across threads and World instances.
NavMeshData bakedNavMesh = World.BakeNavMesh(meshPath, agentConfig);
```

### Step 2 — Create a World instance (once per room)

```csharp
// Optional: tune pathfinding and avoidance behaviour
var pfConfig = new PathfindingConfiguration
{
    // Tier 2 fallback search radius when target is unreachable (see Section 4)
    FallbackTargetSearchRadius  = 5.0f,
    FallbackTargetSearchSamples = 8,

    // Local avoidance (agents steering around each other)
    EnableLocalAvoidance  = true,
    LocalAvoidanceRadius  = 5.0f,
    MaxAvoidanceNeighbors = 5,

    // Automatic replanning when path is blocked
    EnableAutomaticReplanning = true,
    ReplanCooldown            = 1.0f,  // seconds between replans for same entity
};

// World constructor is cheap — it creates a physics sim and wires subsystems,
// but does NOT re-process any geometry.
var world = new World(bakedNavMesh, agentConfig, pfConfig);

// Register event handlers immediately after construction (see Section 5)
world.OnDestinationReached += OnUnitArrived;
world.OnPathReplanned      += OnUnitReplanned;
world.OnMovementStarted    += OnUnitStartedMoving;
world.OnMovementProgress   += OnUnitMadeProgress;
```

### Single-instance shortcut

For simple servers with one room, you can bake + create in one call:

```csharp
using var world = World.CreateFromFile(meshPath, agentConfig, pfConfig);
```

### Static worlds vs. worlds with doors/destructibles/resource nodes

```csharp
// Default: monolithic NavMesh — best query performance, no runtime updates.
// SpawnObstacle() still adds a physics collider, but the pathfinder will NOT
// route around it — units are deflected by collision only.
NavMeshData baked = World.BakeNavMesh(meshPath, agentConfig);

// Tiled NavMesh: required for runtime tile rebuilds.
// Use this whenever your world has doors, collapsing bridges, or resource nodes
// (trees, rocks, ore veins) that should block pathfinding until destroyed.
var navConfig = new NavMeshConfiguration
{
    EnableTileUpdates = true,
    TileSize          = 32.0f,  // meters per tile
    MaxTiles          = 256,
    MaxPolysPerTile   = 2048,
};
NavMeshData tiledBaked = World.BakeNavMesh(meshPath, agentConfig, navConfig);
```

### Dispose when the room ends

```csharp
// World implements IDisposable — always call Dispose when the room closes.
// This releases BepuPhysics buffers and prevents memory leaks.
world.Dispose();

// Or use 'using':
using var world = new World(bakedNavMesh, agentConfig);
```

---

## 3. Spawning and Despawning Units

### Spawn

```csharp
// Spawn a player at an approximate world position.
// World will:
//   1. Snap the position to the nearest NavMesh surface (downward-priority).
//   2. Raycast down to find the exact physics surface below the snap point.
//   3. Create a capsule rigid body with feet on that surface.
// You do NOT need to compute Y offsets manually.
world.Spawn(entityId: playerId, position: spawnPoint, EntityType.Player);

// Spawn an NPC
world.Spawn(entityId: npcId, position: npcSpawnPoint, EntityType.NPC);

// Spawn a static obstacle (barrel, crate — blocks movement but does not pathfind)
world.Spawn(entityId: barrelId, position: barrelPos, EntityType.StaticObject);
```

**Entity ID rules**:
- IDs must be unique **within this World instance**.
- Two different `World` instances can each have an entity with `id = 1`.
- Use your existing session/entity ID system directly.

### Despawn

```csharp
// Stops active movement, then removes the physics body.
// Always despawn before the World is disposed.
world.Despawn(playerId);
```

**Internal despawn order** (important — do not reverse):
1. `StopMovement` — clears path state and removes from the character controller.
2. `UnregisterEntity` — removes the BepuPhysics rigid body.

Calling `UnregisterEntity` while movement is active leaves dangling references. The `World.Despawn` method handles this correctly.

### Teleport (instant position change)

```csharp
// Stops movement, snaps target to NavMesh, sets position immediately.
// Use for: respawns, GM warps, checkpoint placement, cutscene positioning.
world.Teleport(playerId, respawnPoint);

// If the unit should resume moving after the teleport:
world.Teleport(playerId, respawnPoint);
world.Move(playerId, questTarget);
```

### Resource Node Obstacles (trees, rocks, etc.)

`world.Spawn()` is for **units** (agents with pathfinding). For static world objects that should block both physics and pathfinding — resource nodes, destructible trees, ore veins — use `SpawnObstacle` / `DespawnObstacle` instead.

These two calls handle everything atomically: physics collider + NavMesh tile carve on spawn, and physics removal + NavMesh tile restore on despawn.

```csharp
// ── Prerequisite: bake with tiled NavMesh ────────────────────────────────────
// SpawnObstacle carves NavMesh tiles only when EnableTileUpdates = true.
// Without it the physics collider is still added but pathfinding routes through.
var navConfig = new NavMeshConfiguration { EnableTileUpdates = true };
NavMeshData baked = World.BakeNavMesh(meshPath, agentConfig, navConfig);
var world = new World(baked, agentConfig);

// ── Spawn a tree resource node ───────────────────────────────────────────────
// position = base of the tree (world-space, ground level)
// size     = bounding box (width, height, depth) — used for physics and NavMesh carving
world.SpawnObstacle(
    entityId: treeId,
    position: new Vector3(10f, 0f, 15f),
    size:     new Vector3(1f, 4f, 1f)   // 1m trunk, 4m tall
);
// Pathfinder now routes AROUND the tree. Units approaching it are deflected by physics.

// ── Player chops the tree ────────────────────────────────────────────────────
void OnTreeChopped(int treeId)
{
    world.DespawnObstacle(treeId);
    // Pathfinder immediately routes THROUGH the cleared area.
    // Any active paths that were detoured around the tree will replan on the
    // next validation tick automatically.
}
```

**How it works internally:**
- `SpawnObstacle` registers a static box collider in BepuPhysics, then calls `Pathfinding.RebuildNavMeshRegion` with empty geometry to erase walkability in the affected tiles (~1–5 ms).
- `DespawnObstacle` removes the collider, then calls `RebuildNavMeshRegion` with the original baked source geometry to restore those tiles.
- The source geometry is stored inside `NavMeshData` at bake time — the game server does not need to manage it.

**Cost:** ~1–5 ms per obstacle spawn/despawn (tile-cache partial rebuild). Full NavMesh rebake is never triggered.

**Monolithic NavMesh fallback:** if baked without `EnableTileUpdates`, the physics collider is still added (steering avoids the obstacle at close range), but the pathfinder has no global awareness — it will plan routes through the obstacle's footprint and rely on local avoidance to steer around it. This is acceptable for sparse, open areas but breaks badly in corridors or dense forests.

### Multi-size agents

When your game has units with different physical dimensions (goblins vs. trolls, infantry vs. mounted cavalry), bake one NavMesh per size using `MultiAgentNavMesh`.

```csharp
// Define one AgentConfig per agent class — keep these as static fields or singletons.
// World.Spawn() uses reference equality to look up the correct NavMesh,
// so each Spawn() call must receive the same instance that was passed to Add().
static readonly AgentConfig GoblinConfig = new() { Height = 1.4f, Radius = 0.3f, MaxClimb = 0.3f, MaxSlope = 45f };
static readonly AgentConfig HumanConfig  = new() { Height = 2.0f, Radius = 0.4f, MaxClimb = 0.5f, MaxSlope = 45f };
static readonly AgentConfig TrollConfig  = new() { Height = 3.0f, Radius = 0.8f, MaxClimb = 0.6f, MaxSlope = 40f };

// Bake one NavMesh per config — CPU-intensive, call once at startup.
// Each NavMesh respects its config's clearance: a troll cannot path through
// an archway that a goblin can, even on the same map.
var multiNavMesh = new MultiAgentNavMesh(meshPath)
    .Add(GoblinConfig)
    .Add(HumanConfig)
    .Add(TrollConfig)
    .Bake();

// Pass the multi-navmesh to the World constructor.
// The first config registered (GoblinConfig) becomes the default for
// World.Spawn() calls that don't specify a config.
using var world = new World(multiNavMesh);

// Spawn units — pass the same AgentConfig instance used in Add()
world.Spawn(goblinId, goblinSpawn, GoblinConfig);
world.Spawn(humanId,  humanSpawn,  HumanConfig);
world.Spawn(trollId,  trollSpawn,  TrollConfig);

// Move is unchanged — the entity's registered config is used automatically
world.Move(goblinId, target);
world.Move(trollId,  target);
```

**Rules:**
- Keep `AgentConfig` instances as `static readonly` fields — `Add()` and `Spawn()` use reference equality, not value equality.
- Baking N configs takes N times as long. For most games 2–3 sizes is sufficient.
- `world.Move()` routes each unit's path query to its own NavMesh automatically.

---

## 4. Requesting Unit Movement

### Basic move command

```csharp
MovementResponse response = world.Move(
    entityId: playerId,
    target:   clickedPosition,  // raw world-space position from player input
    speed:    5.0f              // meters per second
);
```

The system runs **Option D tiered fallback** automatically:

| Tier | What happens | `response.WasTargetAdjusted` |
|------|-------------|------------------------------|
| 1 — Full path | Direct path to the clicked target | `false` |
| 2 — Nearest reachable near target | Clicked position was invalid; system finds closest valid point within `FallbackTargetSearchRadius` | `true` |
| 3 — Furthest reachable toward target | Target is on a disconnected island; unit advances as far as possible in that direction | `true` |
| 4 — Hard cancel | Nothing reachable at all | `Success = false` |

### Reading the response

```csharp
if (response.Success)
{
    if (response.WasTargetAdjusted)
    {
        // Tell the client the unit is going to a slightly different spot
        // response.AdjustmentReason: "NearestReachableNearTarget" or "FurthestReachableTowardTarget"
        SendAdjustedDestinationMarker(playerId, response.ActualTargetPosition, response.AdjustmentReason);
    }
    else
    {
        SendDestinationMarker(playerId, response.ActualTargetPosition);
    }

    // Estimated arrival time — useful for client-side ability timers
    float eta = response.EstimatedTime;  // seconds at requested speed
}
else
{
    // response.FailureReason: EntityNotFound | AgentOffNavmesh | NoReachablePosition
    SendMovementFailedFeedback(playerId, response.FailureReason.ToString());
}
```

### Stop movement

```csharp
// Halts the unit at its current position. Can call Move() again after.
world.StopMove(playerId);
```

### Cancelling previous movement

Calling `Move()` on an entity that is already moving cancels the previous path and starts a new one immediately. You do not need to call `StopMove()` first.

### Pathfinding replans automatically

If a unit's path becomes blocked at runtime (e.g., another unit is standing in the way), the system replans automatically. **Your server does not call `Move()` again** — it just listens to `OnPathReplanned` if it needs to know.

---

## 5. Events and Callbacks

Register handlers immediately after creating the World. All events fire on the game server's update thread (the same thread that calls `world.Update()`).

```csharp
// ── Unit reached its destination ─────────────────────────────────────────────
// Trigger: idle AI, quest completion checks, reward delivery, NPC dialogue.
world.OnDestinationReached += (entityId, finalPosition) =>
{
    var player = playerManager.Get(entityId);
    player.State = PlayerState.Idle;
    questSystem.CheckArrivalTriggers(entityId, finalPosition);
};

// ── Unit started moving ───────────────────────────────────────────────────────
// Trigger: play movement animation, broadcast move command to clients.
// actualStart/actualTarget are NavMesh-snapped — may differ from the raw request.
world.OnMovementStarted += (entityId, actualStart, actualTarget) =>
{
    BroadcastToClients(new MoveStartPacket
    {
        EntityId = entityId,
        Target   = actualTarget,
    });
};

// ── Path was automatically replanned ─────────────────────────────────────────
// Trigger: log metrics, notify client that path changed.
// The unit continues moving — you do NOT need to call Move() again.
world.OnPathReplanned += (entityId) =>
{
    metrics.IncrementReplanCount(entityId);
    // Optionally tell client a new path is being followed
};

// ── Unit advanced to next waypoint ───────────────────────────────────────────
// Trigger: smooth client-side progress bars, animation sync.
// progressFraction: 0.0 (just started) → 1.0 (arrived)
world.OnMovementProgress += (entityId, progressFraction) =>
{
    clientSync.UpdateProgressBar(entityId, progressFraction);
};
```

### Collision events

Use `CollisionEventSystem` for physics collision callbacks (unit vs. unit, unit vs. obstacle):

```csharp
var collisionEvents = new CollisionEventSystem(world.Physics);

// Player hitting an enemy
collisionEvents.OnPlayerHitEnemy += (collision) =>
{
    combatSystem.ApplyContactDamage(collision.EntityA.EntityId, collision.EntityB.EntityId);
};

// Any unit hitting an obstacle
collisionEvents.OnUnitHitObstacle += (collision) =>
{
    audioSystem.PlayImpactSound(collision.Position);
};

// Custom type pair (e.g. projectile vs. player)
collisionEvents.RegisterHandler(EntityType.Projectile, EntityType.Player, (collision) =>
{
    projectileSystem.HitPlayer(collision.EntityA.EntityId, collision.EntityB.EntityId);
});
```

---

## 6. Reading Unit Positions and States

Call these queries on your update tick to build the packet you send to clients.

### Position and velocity

```csharp
// World-space position of the physics capsule CENTER.
// To get feet position: pos.Y - (agentConfig.Height / 2 + agentConfig.Radius)
Vector3 position = world.GetPosition(playerId);

// Velocity in meters per second — useful for client-side interpolation and
// lag compensation on ability hits.
Vector3 velocity = world.GetVelocity(playerId);
```

### Character state

```csharp
CharacterState state = world.GetState(playerId);

switch (state)
{
    case CharacterState.GROUNDED:
        // On the ground, pathfinding active.
        // Safe to issue Move() commands and cast grounded-only abilities.
        break;

    case CharacterState.AIRBORNE:
        // Falling or knocked back. Pathfinding is paused.
        // Block grounded-only abilities (e.g. "cannot cast while airborne").
        break;

    case CharacterState.RECOVERING:
        // Just landed. Stabilising — will return to GROUNDED in ~1 frame.
        // Trigger landing animation here.
        break;
}
```

### Building the per-tick sync packet

```csharp
// Call world.Update() first, then read positions. Never read before update.
void OnServerTick(float deltaTime)
{
    world.Update(deltaTime);

    // Build a snapshot of all active units for this tick
    var snapshot = new WorldSnapshot { Tick = currentTick };

    foreach (int id in activeEntityIds)
    {
        snapshot.Units.Add(new UnitState
        {
            EntityId  = id,
            Position  = world.GetPosition(id),
            Velocity  = world.GetVelocity(id),
            State     = world.GetState(id),
        });
    }

    BroadcastSnapshotToClients(snapshot);
}
```

### NavMesh queries (for validation, not for movement)

```csharp
// Check if a target position is on the walkable NavMesh before issuing Move().
// Move() handles invalid targets via Option D fallback, so this is optional —
// use it only if you want to reject clicks before they reach the movement system.
bool canReach = world.IsValidPosition(clickedPosition);

// Find the nearest walkable position (e.g. for spawn point validation).
Vector3? snapped = world.SnapToNavMesh(rawPosition);
if (snapped == null)
{
    // Position is too far from any walkable surface — pick a different spawn point.
}
```

---

## 7. Physics Interactions

### Knockback (ability hit, explosion radius)

```csharp
// Applies an impulse and forces AIRBORNE state — pathfinding pauses automatically.
// Pathfinding resumes when the unit lands (RECOVERING → GROUNDED).
var knockbackDir = Vector3.Normalize(targetPos - sourcePos);
// Include a small upward component for a natural arc:
knockbackDir = Vector3.Normalize(new Vector3(knockbackDir.X, 0.3f, knockbackDir.Z));

world.Knockback(targetId, knockbackDir, force: 8.0f);

// After knockback, poll state before issuing new abilities that require grounding:
if (world.GetState(targetId) == CharacterState.GROUNDED)
{
    // Safe to apply grounded effects
}
```

### Jump

```csharp
// Returns true if the unit was grounded and the jump was applied.
// Returns false if the unit was already airborne (prevents double-jump).
bool jumped = world.Jump(playerId, jumpForce: 5.0f);
```

### Soft push (non-combat, no state change)

```csharp
// Applies an impulse without forcing AIRBORNE — unit continues pathfinding.
// Use for: crowd flow, moving platform effects, gentle environmental forces.
world.Movement.Push(playerId, pushDirection, force: 3.0f, makePushable: false);
```

---

## 8. Runtime NavMesh Updates

Use this when world geometry changes at runtime — doors opening, bridges collapsing, terrain being destroyed. **Requires `EnableTileUpdates = true`** in `NavMeshConfiguration` (see Section 2).

> **Resource nodes (trees, rocks, ore):** use `world.SpawnObstacle()` / `world.DespawnObstacle()` instead of calling `RebuildNavMeshRegion` directly — those methods handle both the physics body and the NavMesh tile update in one call. See [Section 3 — Resource Node Obstacles](#resource-node-obstacles-trees-rocks-etc).

```csharp
// Example: a door opens — rebuild the NavMesh tile covering that doorway.
// newVertices / newIndices = the updated walkable geometry for that region.
int rebuiltTiles = world.Pathfinding.RebuildNavMeshRegion(
    center:      doorPosition,
    radius:      3.0f,          // meters — how much area to rebuild
    newVertices: openDoorVerts,
    newIndices:  openDoorIndices,
    navConfig:   navConfig       // same config used during BakeNavMesh
);

// Example: a bridge collapses — erase walkable area (pass empty geometry).
world.Pathfinding.RebuildNavMeshRegion(
    center:      bridgeMidpoint,
    radius:      8.0f,
    newVertices: Array.Empty<float>(),
    newIndices:  Array.Empty<int>(),
    navConfig:   navConfig
);
```

After a tile rebuild, any active path that crosses the rebuilt region is re-validated on the next periodic validation tick (`PathfindingConfiguration.PathValidationInterval`). No manual action needed.

---

## 9. The Game Loop

### Fixed timestep (required for determinism)

```csharp
const float FixedDeltaTime = 0.008f; // 125 Hz — recommended for production

var timer = Stopwatch.StartNew();
double accumulator = 0.0;

while (serverRunning)
{
    double elapsed = timer.Elapsed.TotalSeconds;
    timer.Restart();
    accumulator += elapsed;

    while (accumulator >= FixedDeltaTime)
    {
        // 1. Process input commands received since last tick
        ProcessQueuedCommands();

        // 2. Advance simulation (movement first, then physics — order matters)
        world.Update(FixedDeltaTime);

        // 3. Read positions and broadcast to clients
        BroadcastWorldSnapshot();

        accumulator -= FixedDeltaTime;
    }

    // Sleep until the next tick (reduces CPU spin)
    Thread.Sleep(1);
}
```

**Why movement before physics?**  
`world.Update()` calls `Movement.UpdateMovement()` first (sets velocity goals) then `Physics.Update()` (integrates velocities → new positions). Reversing this order produces one-frame lag.

**Why fixed timestep?**  
Physics simulation is not frame-rate independent. A fixed step keeps the simulation deterministic — critical for server-authoritative multiplayer where all clients must agree on positions. The recommended step is **0.008s (125 Hz)** for smooth movement; 0.016s (60 Hz) is acceptable for less demanding scenarios.

---

## 10. Multi-Room / Multi-Instance Servers

The NavMesh bake is the expensive step. Share one baked result across all rooms using the same map.

```csharp
// At server startup — bake once per map
var maps = new Dictionary<string, NavMeshData>();
maps["dungeon"]  = World.BakeNavMesh(Path.Combine(baseDir, "worlds/dungeon.obj"),  agentConfig);
maps["arena"]    = World.BakeNavMesh(Path.Combine(baseDir, "worlds/arena.obj"),    agentConfig);
maps["overworld"]= World.BakeNavMesh(Path.Combine(baseDir, "worlds/overworld.obj"),agentConfig);

// When a room opens — create a new World from the shared bake
Room OpenRoom(string mapName)
{
    var baked = maps[mapName];
    var world = new World(baked, agentConfig, pfConfig);
    world.OnDestinationReached += OnUnitArrived;
    // ... register other events ...
    return new Room(world);
}

// When a room closes
void CloseRoom(Room room)
{
    foreach (int id in room.EntityIds)
        room.World.Despawn(id);

    room.World.Dispose();  // frees BepuPhysics memory
}
```

Each `World` instance has a **fully isolated** `PhysicsWorld` — entities in room A cannot interact with entities in room B.

---

## 11. Configuration Reference

### AgentConfig (single source of truth — never create per-subsystem)

```csharp
var agentConfig = new AgentConfig
{
    Height   = 2.0f,   // capsule cylinder length
    Radius   = 0.4f,   // capsule hemisphere radius
    MaxSlope = 45.0f,  // max walkable slope (degrees)
    MaxClimb = 0.5f,   // max step height (meters) — must match NavMesh generation
};
```

### PathfindingConfiguration (tuning knobs)

```csharp
var pfConfig = new PathfindingConfiguration
{
    // ── Option D fallback (invalid target handling) ──────────────────────
    FallbackTargetSearchRadius  = 5.0f,  // Tier 2 ring search around target (meters)
    FallbackTargetSearchSamples = 8,     // angles per ring (8 × 2 rings = 16 candidates)

    // ── Path validation ──────────────────────────────────────────────────
    EnablePathValidation  = true,   // validate MaxClimb/MaxSlope per segment
    EnablePathAutoFix     = true,   // insert intermediate waypoints for steep segments

    // ── Automatic replanning ─────────────────────────────────────────────
    EnableAutomaticReplanning     = true,
    ReplanCooldown                = 1.0f,   // min seconds between replans per entity
    PathValidationInterval        = 0.5f,   // how often to check path validity (seconds)
    PathValidationLookaheadWaypoints = 3,   // waypoints ahead to validate each check
    StuckDetectionThreshold       = 0.3f,   // meters; less than this = possibly stuck
    StuckDetectionCount           = 2,      // consecutive stuck ticks before replan

    // ── Local avoidance ──────────────────────────────────────────────────
    EnableLocalAvoidance  = true,
    LocalAvoidanceRadius  = 5.0f,
    MaxAvoidanceNeighbors = 5,
    AvoidanceStrength     = 2.0f,
    SeparationRadius      = 2.0f,

    // ── Arrival thresholds ───────────────────────────────────────────────
    WaypointReachedThreshold     = 0.5f,  // XZ distance to advance past waypoint
    DestinationReachedThreshold  = 0.3f,  // XZ distance to fire OnDestinationReached

    // ── NavMesh snapping ─────────────────────────────────────────────────
    HorizontalSearchExtent           = 2.0f,
    VerticalSearchExtent             = 5.0f,
    PathfindingSearchExtentsHorizontal = 5.0f,
    PathfindingSearchExtentsVertical   = 10.0f,

    // ── Edge / cliff detection ───────────────────────────────────────────
    EdgeCheckDistanceMultiplier = 2.5f,  // multiples of agent radius to look ahead
    MaxSafeDropDistance         = 2.0f,  // meters — larger drops are treated as edges
};
```

### PhysicsConfiguration

```csharp
var physicsConfig = new PhysicsConfiguration
{
    Gravity   = new Vector3(0, -9.81f, 0),
    Timestep  = 0.008f,  // 125 Hz
};

var world = new World(baked, agentConfig, pfConfig, physicsConfig);
```

---

## 12. Production Checklist

### Startup

- [ ] `AgentConfig` created **once**, passed to `BakeNavMesh`, `World`, and nothing else.
- [ ] `World.BakeNavMesh()` called once per map at process startup (not per room).
- [ ] `NavMeshData` stored and reused across all `World` instances for the same map.
- [ ] Event handlers registered on `world` immediately after construction.

### Per-room lifecycle

- [ ] `new World(baked, agentConfig)` when a room opens.
- [ ] `world.Dispose()` (or `using`) when a room closes.
- [ ] All entities despawned **before** `Dispose()`.

### Game loop

- [ ] `world.Update(fixedDeltaTime)` called on every tick with a **fixed** delta.
- [ ] Positions read **after** `world.Update()`, not before.
- [ ] Update rate is 60–125 Hz (0.016–0.008s). Never use variable delta time.

### Movement

- [ ] Always check `response.Success` after `world.Move()`.
- [ ] Use `response.WasTargetAdjusted` to show the correct client-side marker.
- [ ] Do **not** call `world.Move()` again after `OnPathReplanned` — the system handles it.
- [ ] Do **not** call `world.StopMove()` before `world.Move()` — `Move()` cancels the previous path automatically.

### Despawn safety

- [ ] Call `world.Despawn(id)` (not `Physics.UnregisterEntity` directly) — despawn handles movement cleanup first.
- [ ] Never access `world.GetPosition(id)` or `world.GetState(id)` after despawn.

### NavMesh tile updates

- [ ] Only possible when baked with `EnableTileUpdates = true`.
- [ ] After `RebuildNavMeshRegion`, active paths self-revalidate — no manual replan needed.

### Resource node obstacles

- [ ] Bake with `EnableTileUpdates = true` — `SpawnObstacle` silently skips tile carving for monolithic NavMeshes.
- [ ] Use `world.SpawnObstacle(id, position, size)` — not `world.Spawn()` — for trees, rocks, and other destructible blockers.
- [ ] Use `world.DespawnObstacle(id)` when the node is harvested/destroyed — not `world.Despawn()`.
- [ ] Do **not** call `RebuildNavMeshRegion` manually for resource nodes — `SpawnObstacle`/`DespawnObstacle` handle it.

---

## Advanced: Direct Subsystem Access

`World` exposes the underlying subsystems for operations not covered by the facade:

```csharp
// Direct NavMesh query (e.g. compute a path without moving a unit)
var path = world.Pathfinding.FindPath(start, end);

// Apply a custom physics impulse
world.Physics.ApplyLinearImpulse(world.Physics.EntityRegistry.GetEntityById(id), impulse);

// Read character controller state directly
var entity = world.Physics.EntityRegistry.GetEntityById(id);
var state  = world.Movement.GetCharacterState(entity);

// Get active path waypoints for a unit (e.g. to send to Unity visualiser)
var waypoints = world.Movement.GetWaypoints(id);
```

Use these escape hatches sparingly — the facade API covers the vast majority of production use cases.
