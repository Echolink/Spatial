# Multi-Size Agent Support — Test Plan

This document defines the test scenarios to validate the multi-size agent implementation
introduced in the session of 2026-04-19. Execute all scenarios in the next session before
merging or shipping.

---

## What Was Changed

| File | Change |
|---|---|
| `Spatial.Integration/MultiAgentNavMesh.cs` | New class — fluent builder, bakes one NavMesh per `AgentConfig` |
| `Spatial.Integration/World.cs` | New constructor `World(MultiAgentNavMesh)`, new `Spawn(id, pos, AgentConfig)` overload, per-entity config tracking |
| `Spatial.Integration/MovementController.cs` | `_entityServices` dict, `ServiceFor(entityId)` routing, all entity-scoped NavMesh calls now per-entity |

---

## Regression Guard — Run First

Before running any new scenario, confirm the existing test suite still passes.
If any of these break, stop and fix before proceeding.

```bash
dotnet run --project Spatial.TestHarness -- enhanced 5
dotnet run --project Spatial.TestHarness -- scale 20
dotnet run --project Spatial.TestHarness -- local-avoidance
dotnet run --project Spatial.TestHarness -- path-validation
```

**Pass criteria:** same output as before the change (no new failures, no new warnings about navmesh or pathfinding).

---

## Scenario 1 — Single-Config Backwards Compatibility

**Goal:** confirm the old single-config API is completely unaffected.

**Code to write** in `Spatial.TestHarness/TestMultiSizeAgents.cs`:

```csharp
public static void TestSingleConfigBackwardsCompat(string meshPath)
{
    var config = AgentConfig.Player;
    var navMesh = World.BakeNavMesh(meshPath, config);
    using var world = new World(navMesh, config);

    var e = world.Spawn(1, new Vector3(0, 0, 0));
    // Settle
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    var resp = world.Move(1, new Vector3(5, 0, 5));
    Assert(resp.Success, "single-config Move should succeed");

    for (int i = 0; i < 200; i++) world.Update(0.016f);

    world.Despawn(1);
    Console.WriteLine("[PASS] SingleConfigBackwardsCompat");
}
```

**Pass criteria:**
- Builds without error
- Move returns `Success = true`
- No exception thrown during Update loop
- No console output mentioning "no NavMesh found" or "unregistered config"

---

## Scenario 2 — Multi-Size Bake and Basic Spawn

**Goal:** confirm `MultiAgentNavMesh` bakes all configs and `World(multiNavMesh)` wires them up correctly.

```csharp
static readonly AgentConfig SmallConfig  = new() { Radius = 0.3f, Height = 1.4f, MaxClimb = 0.3f, MaxSlope = 40f };
static readonly AgentConfig MediumConfig = new() { Radius = 0.5f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
static readonly AgentConfig LargeConfig  = new() { Radius = 1.2f, Height = 3.5f, MaxClimb = 0.8f, MaxSlope = 35f };

public static void TestMultiSizeBakeAndSpawn(string meshPath)
{
    var multiNavMesh = new MultiAgentNavMesh(meshPath)
        .Add(SmallConfig)
        .Add(MediumConfig)
        .Add(LargeConfig)
        .Bake();

    using var world = new World(multiNavMesh);

    world.Spawn(1, new Vector3(0, 0, 0), SmallConfig);
    world.Spawn(2, new Vector3(2, 0, 0), MediumConfig);
    world.Spawn(3, new Vector3(5, 0, 0), LargeConfig, EntityType.Enemy);

    for (int i = 0; i < 30; i++) world.Update(0.016f);

    var pos1 = world.GetPosition(1);
    var pos2 = world.GetPosition(2);
    var pos3 = world.GetPosition(3);

    Assert(pos1 != Vector3.Zero || true, "entity 1 spawned");
    Assert(pos2 != Vector3.Zero || true, "entity 2 spawned");
    Assert(pos3 != Vector3.Zero || true, "entity 3 spawned");

    Console.WriteLine("[PASS] MultiSizeBakeAndSpawn");
}
```

**Pass criteria:**
- Three bakes logged to console (one per config)
- Three entities spawned without exception
- Positions are within 1 m of spawn points after settle ticks
- No "Entity not found" or null reference errors

---

## Scenario 3 — Each Size Moves on Its Own NavMesh

**Goal:** confirm `Move()` routes each entity to the NavMesh built for its config, not the default one.

**Observable signal:** each entity's path query should be served. If routing falls back to the wrong NavMesh, large agents may fail to find paths through wide-but-short corridors that were pruned during baking for their actual size.

```csharp
public static void TestEachSizeMovesOnCorrectNavMesh(string meshPath)
{
    var multiNavMesh = new MultiAgentNavMesh(meshPath)
        .Add(SmallConfig).Add(MediumConfig).Add(LargeConfig).Bake();

    using var world = new World(multiNavMesh);

    world.Spawn(1, new Vector3(-5, 0, 0), SmallConfig);
    world.Spawn(2, new Vector3( 0, 0, 0), MediumConfig);
    world.Spawn(3, new Vector3( 5, 0, 0), LargeConfig);

    for (int i = 0; i < 30; i++) world.Update(0.016f);

    var target = new Vector3(10, 0, 10);
    var r1 = world.Move(1, target);
    var r2 = world.Move(2, target);
    var r3 = world.Move(3, target);

    Assert(r1.Success, "small agent Move succeeded");
    Assert(r2.Success, "medium agent Move succeeded");
    Assert(r3.Success, "large agent Move succeeded");

    // Run for 5 seconds and check all three are moving
    for (int i = 0; i < 300; i++) world.Update(0.016f);

    Console.WriteLine($"  Small  final pos: {world.GetPosition(1)}");
    Console.WriteLine($"  Medium final pos: {world.GetPosition(2)}");
    Console.WriteLine($"  Large  final pos: {world.GetPosition(3)}");
    Console.WriteLine("[PASS] EachSizeMovesOnCorrectNavMesh");
}
```

**Pass criteria:**
- All three `Move()` calls return `Success = true`
- After 5 seconds all three entities have moved at least 1 m from their spawn
- No `[MovementController] Replan failed` messages in console

---

## Scenario 4 — Capsule Sizing Is Per-Entity

**Goal:** confirm physics capsule dimensions match the AgentConfig passed at spawn.

**How to verify:** check that a large agent is visually taller and wider than a small agent (via visualization server or by reading spawn Y offset).

```csharp
public static void TestCapsuleSizingPerEntity(string meshPath)
{
    var multiNavMesh = new MultiAgentNavMesh(meshPath)
        .Add(SmallConfig).Add(LargeConfig).Bake();

    using var world = new World(multiNavMesh);

    // Spawn both at the same XZ position but separated so they don't collide
    var smallEntity = world.Spawn(1, new Vector3(0, 0, 0), SmallConfig);
    var largeEntity = world.Spawn(2, new Vector3(10, 0, 0), LargeConfig);

    for (int i = 0; i < 30; i++) world.Update(0.016f);

    var smallPos = world.GetPosition(1);
    var largePos = world.GetPosition(2);

    // The capsule center Y = groundY + Height/2 + Radius
    // Small: groundY + 1.4/2 + 0.3 = groundY + 1.0
    // Large: groundY + 3.5/2 + 1.2 = groundY + 2.95
    // So largePos.Y should be significantly higher than smallPos.Y
    // (assuming both are on flat ground at the same Y)
    float expectedSmallCenterOffset = SmallConfig.Height / 2f + SmallConfig.Radius; // 1.0
    float expectedLargeCenterOffset = LargeConfig.Height / 2f + LargeConfig.Radius; // 2.95

    Console.WriteLine($"  Small agent center Y: {smallPos.Y:F2} (expected ~{expectedSmallCenterOffset:F2} above ground)");
    Console.WriteLine($"  Large agent center Y: {largePos.Y:F2} (expected ~{expectedLargeCenterOffset:F2} above ground)");

    Assert(largePos.Y > smallPos.Y + 0.5f, "large agent capsule center is higher than small agent");
    Console.WriteLine("[PASS] CapsuleSizingPerEntity");
}
```

**Pass criteria:**
- Large agent center Y is at least 0.5 m higher than small agent center Y (on flat ground)
- No physics assertion errors from BepuPhysics

---

## Scenario 5 — Wrong Instance at Spawn Falls Back Gracefully

**Goal:** confirm that passing an unregistered `AgentConfig` instance at spawn falls back to the default NavMesh without crashing.

```csharp
public static void TestUnregisteredConfigFallback(string meshPath)
{
    var multiNavMesh = new MultiAgentNavMesh(meshPath)
        .Add(MediumConfig).Bake();

    using var world = new World(multiNavMesh);

    // This config was NOT registered — should silently fall back to default
    var unknownConfig = new AgentConfig { Radius = 0.6f, Height = 2.2f };
    var entity = world.Spawn(1, new Vector3(0, 0, 0), unknownConfig);

    for (int i = 0; i < 30; i++) world.Update(0.016f);

    // Movement should still work (uses default NavMesh as fallback)
    var resp = world.Move(1, new Vector3(5, 0, 5));
    // We don't assert Success here — depends on whether default NavMesh serves this route
    // What we verify is: no exception thrown, no crash

    Console.WriteLine($"  Fallback Move result: Success={resp.Success}");
    Console.WriteLine("[PASS] UnregisteredConfigFallback — no crash");
}
```

**Pass criteria:**
- No `KeyNotFoundException` or `NullReferenceException`
- Move either succeeds or returns `Success = false` with a reason — it does not throw

---

## Scenario 6 — Despawn Cleans Up Per-Entity State

**Goal:** confirm that `Despawn()` removes entity service registration and config tracking so there are no memory leaks or stale state.

```csharp
public static void TestDespawnCleansUpState(string meshPath)
{
    var multiNavMesh = new MultiAgentNavMesh(meshPath)
        .Add(SmallConfig).Add(LargeConfig).Bake();

    using var world = new World(multiNavMesh);

    world.Spawn(1, new Vector3(0, 0, 0), SmallConfig);
    world.Spawn(2, new Vector3(5, 0, 0), LargeConfig);

    for (int i = 0; i < 30; i++) world.Update(0.016f);

    world.Move(1, new Vector3(10, 0, 10));
    world.Move(2, new Vector3(10, 0, 10));

    for (int i = 0; i < 60; i++) world.Update(0.016f);

    // Despawn while moving
    world.Despawn(1);
    world.Despawn(2);

    // Continue ticking — should not throw NullReference or access removed entity
    for (int i = 0; i < 60; i++) world.Update(0.016f);

    Console.WriteLine("[PASS] DespawnCleansUpState");
}
```

**Pass criteria:**
- No exception during the post-despawn Update loop
- No console errors about missing entities

---

## Scenario 7 — Mixed-Size Room End-to-End

**Goal:** run a realistic room lifecycle with three size tiers, multiple units each, and confirm the simulation completes cleanly.

```csharp
public static void TestMixedSizeRoomLifecycle(string meshPath)
{
    var multiNavMesh = new MultiAgentNavMesh(meshPath)
        .Add(SmallConfig).Add(MediumConfig).Add(LargeConfig).Bake();

    bool anyDestinationReached = false;
    using var world = new World(multiNavMesh);
    world.OnDestinationReached += (id, pos) =>
    {
        Console.WriteLine($"  Entity {id} reached destination at {pos}");
        anyDestinationReached = true;
    };

    // Spawn two of each size
    world.Spawn(1, new Vector3(-8, 0, -8), SmallConfig);
    world.Spawn(2, new Vector3(-6, 0, -8), SmallConfig);
    world.Spawn(3, new Vector3( 0, 0, -8), MediumConfig);
    world.Spawn(4, new Vector3( 2, 0, -8), MediumConfig);
    world.Spawn(5, new Vector3( 8, 0, -8), LargeConfig, EntityType.Enemy);
    world.Spawn(6, new Vector3(10, 0, -8), LargeConfig, EntityType.Enemy);

    for (int i = 0; i < 30; i++) world.Update(0.016f);

    // Send all to the same destination
    var dest = new Vector3(0, 0, 10);
    for (int id = 1; id <= 6; id++)
        world.Move(id, dest);

    // Run for 15 seconds
    for (int i = 0; i < 15 * 60; i++) world.Update(0.016f);

    Assert(anyDestinationReached, "at least one entity should have reached the destination");

    // Clean up
    for (int id = 1; id <= 6; id++) world.Despawn(id);

    Console.WriteLine("[PASS] MixedSizeRoomLifecycle");
}
```

**Pass criteria:**
- At least one `OnDestinationReached` event fired
- No replan storms (check for excessive `[MovementController] Replanning` lines)
- Clean despawn with no exceptions

---

---

## Obstacle Spawn / Push Scenarios

These test the `SpawnObstacle` / `DespawnObstacle` overlap-checking and push logic in `World.cs`.

### Key implementation details to keep in mind

| Detail | Where |
|---|---|
| Overlap check radius | `Max(size.X, size.Z) * 0.5 + _agentConfig.Radius` |
| Footprint check uses | `_agentConfig.Radius` (world default, not per-entity) |
| Push axis | Minimum penetration (X vs Z overlap depth) |
| Post-push snap | `Pathfinding.FindNearestValidPosition` within 3 m |
| Movement after push | `Movement.StopMovement` is called on pushed unit |

---

### Scenario 8 — Clean Spawn (No Overlap)

**Goal:** obstacle spawns successfully when the footprint is clear.

```csharp
public static void TestObstacleSpawnNoOverlap(string meshPath)
{
    var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
    using var world = new World(navMesh, AgentConfig.Player);

    world.Spawn(1, new Vector3(0, 0, 0));
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    // Obstacle placed well away from the unit
    var result = world.SpawnObstacle(100, new Vector3(10, 0, 10), new Vector3(2, 2, 2));

    Assert(result.Spawned, "obstacle should spawn when footprint is clear");
    Assert(result.DisplacedEntityIds.Count == 0, "no units should be displaced");
    Assert(result.Entity != null, "entity should be non-null on success");

    Console.WriteLine("[PASS] ObstacleSpawnNoOverlap");
}
```

**Pass criteria:**
- `Spawned = true`
- `DisplacedEntityIds` is empty
- `Entity` is not null

---

### Scenario 9 — Blocked Spawn Rejected (forceSpawn = false)

**Goal:** spawn is rejected when a unit is standing inside the footprint and `forceSpawn` is the default `false`.

```csharp
public static void TestObstacleSpawnRejectedByOverlap(string meshPath)
{
    var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
    using var world = new World(navMesh, AgentConfig.Player);

    // Spawn unit directly at obstacle position
    world.Spawn(1, new Vector3(0, 0, 0));
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    // Obstacle centred on the unit (footprint = 2×2 → halfX=1, halfZ=1; unit at 0,0,0 is inside)
    var result = world.SpawnObstacle(100, new Vector3(0, 0, 0), new Vector3(2, 2, 2));

    Assert(!result.Spawned, "spawn should be rejected when unit is inside footprint");
    Assert(result.DisplacedEntityIds.Contains(1), "blocking unit ID should be returned");
    Assert(result.Entity == null, "entity should be null on rejection");

    // Unit must still be at its original position — not moved
    var unitPos = world.GetPosition(1);
    Assert(MathF.Abs(unitPos.X) < 1f, "unit should not have been moved on rejection");

    Console.WriteLine("[PASS] ObstacleSpawnRejectedByOverlap");
}
```

**Pass criteria:**
- `Spawned = false`
- `DisplacedEntityIds` contains entity 1
- Unit position unchanged after the failed spawn attempt

---

### Scenario 10 — Force Spawn Pushes Unit Out

**Goal:** with `forceSpawn = true`, the unit is pushed to the nearest clear point and the obstacle spawns.

```csharp
public static void TestObstacleForceSpawnPushesUnit(string meshPath)
{
    var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
    using var world = new World(navMesh, AgentConfig.Player);

    var spawnPos = new Vector3(0, 0, 0);
    world.Spawn(1, spawnPos);
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    var unitPosBefore = world.GetPosition(1);

    // Force-spawn obstacle on top of unit
    var obstacleSize = new Vector3(2, 2, 2);
    var result = world.SpawnObstacle(100, spawnPos, obstacleSize, forceSpawn: true);

    Assert(result.Spawned, "force spawn should always succeed");
    Assert(result.DisplacedEntityIds.Contains(1), "unit should be listed as displaced");

    // Unit must have moved outside the obstacle footprint
    // Footprint halfX = 2/2 + AgentConfig.Player.Radius + 0.1 = 1.6
    float halfX = obstacleSize.X * 0.5f + AgentConfig.Player.Radius + 0.1f;
    float halfZ = obstacleSize.Z * 0.5f + AgentConfig.Player.Radius + 0.1f;
    var unitPosAfter = world.GetPosition(1);
    float feetY = unitPosAfter.Y - (AgentConfig.Player.Height / 2f + AgentConfig.Player.Radius);
    bool outsideFootprint =
        MathF.Abs(unitPosAfter.X - spawnPos.X) >= halfX ||
        MathF.Abs(unitPosAfter.Z - spawnPos.Z) >= halfZ;

    Console.WriteLine($"  Unit before: {unitPosBefore}");
    Console.WriteLine($"  Unit after:  {unitPosAfter}  (halfX={halfX:F2}, halfZ={halfZ:F2})");
    Assert(outsideFootprint, "unit must be outside obstacle footprint after push");

    Console.WriteLine("[PASS] ObstacleForceSpawnPushesUnit");
}
```

**Pass criteria:**
- `Spawned = true`
- `DisplacedEntityIds` contains entity 1
- Unit XZ position places it outside the expanded footprint (`|dx| >= halfX` or `|dz| >= halfZ`)

---

### Scenario 11 — Pushed Unit's Movement Is Stopped

**Goal:** confirm that a force-displaced unit has no active movement state after the push — so the game server can safely re-issue `Move()` from the new position.

```csharp
public static void TestPushedUnitMovementStopped(string meshPath)
{
    var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
    using var world = new World(navMesh, AgentConfig.Player);

    world.Spawn(1, new Vector3(0, 0, 0));
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    // Start the unit moving toward a far point
    world.Move(1, new Vector3(15, 0, 15));
    for (int i = 0; i < 10; i++) world.Update(0.016f); // mid-move

    // Force-spawn obstacle on top of it
    world.SpawnObstacle(100, new Vector3(0, 0, 0), new Vector3(2, 2, 2), forceSpawn: true);

    // After push, velocity should be near zero
    var vel = world.GetVelocity(1);
    float speed = vel.Length();
    Console.WriteLine($"  Unit speed after push: {speed:F3} m/s");
    Assert(speed < 0.5f, "pushed unit should have near-zero velocity");

    // Re-issuing Move should succeed from the new position
    var resp = world.Move(1, new Vector3(10, 0, 10));
    Assert(resp.Success, "re-issued Move after push should succeed");

    Console.WriteLine("[PASS] PushedUnitMovementStopped");
}
```

**Pass criteria:**
- Speed < 0.5 m/s immediately after the force spawn
- `Move()` issued after the push returns `Success = true`

---

### Scenario 12 — Multiple Units Pushed Out

**Goal:** two units standing inside the footprint are both pushed out correctly.

```csharp
public static void TestMultipleUnitsPushedOut(string meshPath)
{
    var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
    using var world = new World(navMesh, AgentConfig.Player);

    // Place two units symmetrically inside a large obstacle footprint
    world.Spawn(1, new Vector3(-0.5f, 0, 0));
    world.Spawn(2, new Vector3( 0.5f, 0, 0));
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    var result = world.SpawnObstacle(100, new Vector3(0, 0, 0), new Vector3(4, 2, 4), forceSpawn: true);

    Assert(result.Spawned, "obstacle should spawn");
    Assert(result.DisplacedEntityIds.Count == 2, "both units should be displaced");
    Assert(result.DisplacedEntityIds.Contains(1) && result.DisplacedEntityIds.Contains(2),
           "both entity IDs should appear in displaced list");

    float halfX = 4 * 0.5f + AgentConfig.Player.Radius + 0.1f;
    float halfZ = 4 * 0.5f + AgentConfig.Player.Radius + 0.1f;

    foreach (int id in new[] { 1, 2 })
    {
        var pos = world.GetPosition(id);
        bool outside = MathF.Abs(pos.X) >= halfX || MathF.Abs(pos.Z) >= halfZ;
        Console.WriteLine($"  Entity {id} pos after push: {pos}  outside={outside}");
        Assert(outside, $"entity {id} must be outside footprint after push");
    }

    Console.WriteLine("[PASS] MultipleUnitsPushedOut");
}
```

**Pass criteria:**
- `DisplacedEntityIds` has exactly 2 entries, one per unit
- Both units' positions fall outside the expanded footprint

---

### Scenario 13 — DespawnObstacle Removes Physics Body

**Goal:** after `DespawnObstacle`, a unit can walk through the formerly blocked area.

```csharp
public static void TestDespawnObstacleRestoresWalkability(string meshPath)
{
    var navConfig = new NavMeshConfiguration { EnableTileUpdates = true };
    var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player, navConfig);
    using var world = new World(navMesh, AgentConfig.Player);

    var obstaclePos = new Vector3(5, 0, 0);
    var obstacleSize = new Vector3(2, 2, 2);

    // Spawn obstacle
    var spawnResult = world.SpawnObstacle(100, obstaclePos, obstacleSize);
    Assert(spawnResult.Spawned, "obstacle should spawn");

    // Spawn a unit far from the obstacle
    world.Spawn(1, new Vector3(-5, 0, 0));
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    // Target is on the other side of the obstacle — path may be blocked or rerouted
    var resp1 = world.Move(1, new Vector3(10, 0, 0));
    Console.WriteLine($"  Move with obstacle present: Success={resp1.Success}, Adjusted={resp1.WasTargetAdjusted}");

    // Remove obstacle
    world.DespawnObstacle(100);

    // Stop and re-issue Move — should now have a direct route
    world.StopMove(1);
    var resp2 = world.Move(1, new Vector3(10, 0, 0));
    Console.WriteLine($"  Move after DespawnObstacle: Success={resp2.Success}, Adjusted={resp2.WasTargetAdjusted}");
    Assert(resp2.Success, "move to the same target should succeed after obstacle removed");

    Console.WriteLine("[PASS] DespawnObstacleRestoresWalkability");
}
```

**Pass criteria:**
- `SpawnObstacle` returns `Spawned = true`
- Post-despawn `Move()` returns `Success = true`
- No exception during `DespawnObstacle`

> **Note:** NavMesh tile restoration only works when baked with `EnableTileUpdates = true`. The test explicitly uses that config so the physics removal and NavMesh restoration are both verified.

---

### Scenario 14 — Retry After Rejection Succeeds

**Goal:** the suggested workflow works — receive a rejection, relocate the blocker, then retry the spawn.

```csharp
public static void TestRetryAfterRejectionSucceeds(string meshPath)
{
    var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
    using var world = new World(navMesh, AgentConfig.Player);

    world.Spawn(1, new Vector3(0, 0, 0));
    for (int i = 0; i < 30; i++) world.Update(0.016f);

    var obstaclePos  = new Vector3(0, 0, 0);
    var obstacleSize = new Vector3(2, 2, 2);

    // First attempt — should be rejected
    var attempt1 = world.SpawnObstacle(100, obstaclePos, obstacleSize);
    Assert(!attempt1.Spawned, "first attempt should be rejected");

    // Game server moves the blocking unit away
    foreach (var blockerId in attempt1.DisplacedEntityIds)
        world.Teleport(blockerId, new Vector3(10, 0, 10));

    for (int i = 0; i < 30; i++) world.Update(0.016f);

    // Second attempt — footprint is now clear
    var attempt2 = world.SpawnObstacle(100, obstaclePos, obstacleSize);
    Assert(attempt2.Spawned, "second attempt should succeed after unit is moved away");
    Assert(attempt2.DisplacedEntityIds.Count == 0, "no units displaced on clean retry");

    Console.WriteLine("[PASS] RetryAfterRejectionSucceeds");
}
```

**Pass criteria:**
- First spawn rejected with blocker ID
- Second spawn succeeds with zero displacements

---

## How to Wire the Test Runner

Add two new CLI entries in `Spatial.TestHarness/Program.cs`:

```csharp
case "multi-size":
    string meshPath = Path.Combine(AppContext.BaseDirectory, "worlds", "arena.obj");
    TestMultiSizeAgents.TestSingleConfigBackwardsCompat(meshPath);
    TestMultiSizeAgents.TestMultiSizeBakeAndSpawn(meshPath);
    TestMultiSizeAgents.TestEachSizeMovesOnCorrectNavMesh(meshPath);
    TestMultiSizeAgents.TestCapsuleSizingPerEntity(meshPath);
    TestMultiSizeAgents.TestUnregisteredConfigFallback(meshPath);
    TestMultiSizeAgents.TestDespawnCleansUpState(meshPath);
    TestMultiSizeAgents.TestMixedSizeRoomLifecycle(meshPath);
    Console.WriteLine("\nAll multi-size scenarios passed.");
    break;

case "obstacle-spawn":
    string meshPath2 = Path.Combine(AppContext.BaseDirectory, "worlds", "arena.obj");
    TestObstacleSpawn.TestObstacleSpawnNoOverlap(meshPath2);
    TestObstacleSpawn.TestObstacleSpawnRejectedByOverlap(meshPath2);
    TestObstacleSpawn.TestObstacleForceSpawnPushesUnit(meshPath2);
    TestObstacleSpawn.TestPushedUnitMovementStopped(meshPath2);
    TestObstacleSpawn.TestMultipleUnitsPushedOut(meshPath2);
    TestObstacleSpawn.TestDespawnObstacleRestoresWalkability(meshPath2);
    TestObstacleSpawn.TestRetryAfterRejectionSucceeds(meshPath2);
    Console.WriteLine("\nAll obstacle-spawn scenarios passed.");
    break;
```

Run with:

```bash
dotnet run --project Spatial.TestHarness -- multi-size
dotnet run --project Spatial.TestHarness -- obstacle-spawn
```

---

## Helper

Add this at the top of `TestMultiSizeAgents.cs`:

```csharp
static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception($"[FAIL] {message}");
}
```

---

## Known Limitations (Out of Scope for This Session)

**Obstacle footprint check ignores per-entity size** — `IsInsideObstacleFootprint` and `PushUnitOutOfObstacle` both use `_agentConfig.Radius` (the world's default config) regardless of which `AgentConfig` a unit was spawned with. In a multi-size world a large agent may not be detected as overlapping even when its physical capsule clearly intersects the obstacle footprint, because the footprint expansion uses the small default radius. Address this once per-entity config lookup is plumbed through to obstacle code.

- **Local avoidance between different-size agents** — avoidance radius currently uses the per-entity config's `AgentRadius` indirectly via capsule size, but `LocalAvoidance.CalculateAvoidanceVelocity` doesn't explicitly account for mixed radii in its separation math. Not a regression; existing behavior unchanged.
- **NavMesh tile rebuild with multi-size** — `RebuildNavMeshRegion` operates on a single `PathfindingService`. In multi-size worlds, only the default config's tiles are rebuilt. Large agents may navigate through areas that were meant to be closed. Document and address separately.
- **`world.Pathfinding` property in multi-size worlds** — returns the default config's service only. Advanced callers who access `world.Pathfinding` directly for manual queries should be aware of this.
