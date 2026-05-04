# Spatial — Project Summary

## What It Is

**Spatial** is a server-authoritative physics and pathfinding framework for multiplayer game servers. It provides a trusted, cheat-proof physics backbone and NPC navigation system that runs entirely on the server. The Unity client is for development visualization only — not a game client.

**Status:** Production-ready  
**Tech stack:** .NET 8.0, C#, BepuPhysics v2.4.0, DotRecast 2026.1.1, Fleck WebSocket, Unity 2021.3+ (visualization)

---

## Purpose

- **Server-authoritative physics** — positions and collisions are computed server-side; clients cannot fake movement
- **NPC pathfinding** — NavMesh-based navigation for AI agents over complex multi-level terrain
- **Development visualization** — real-time 3D view of the simulation via WebSocket → Unity

The system handles: where entities are, how they move, and how NPCs find paths. Game logic (combat, inventory, quests) is left to the consuming game server.

---

## Modules

| Module | Purpose | Key Files |
|--------|---------|-----------|
| `Spatial.Physics` | BepuPhysics v2 wrapper | `PhysicsWorld.cs`, `PhysicsEntity.cs`, `CollisionHandler.cs` |
| `Spatial.Pathfinding` | DotRecast NavMesh generation + A* queries | `NavMeshGenerator.cs`, `Pathfinder.cs`, `NavMeshData.cs` |
| `Spatial.Integration` | `World` façade, movement orchestration, local avoidance | `World.cs`, `MovementController.cs`, `MotorCharacterController.cs`, `PathfindingService.cs`, `NavMeshBuilder.cs`, `MultiAgentNavMesh.cs` |
| `Spatial.Server` | WebSocket visualization server (port 8181) | `VisualizationServer.cs`, `SimulationStateBuilder.cs` |
| `Spatial.TestHarness` | All test/demo entry points | `Program.cs`, `TestEnhancedShowcase.cs`, `TestMotorVsVelocity.cs` |
| `Spatial.MeshLoading` | OBJ mesh loader with group metadata | `ObjMeshLoader.cs` |
| `Unity/` | Real-time 3D visualization client | `SimulationClient.cs`, `EntityVisualizer.cs` |

---

## Architecture

```
Game Server (your code)
    └── World  (Spatial.Integration) — single entry point
            ├── MovementController
            │       ├── PathfindingService  →  DotRecast A* + validation
            │       ├── MotorCharacterController  →  smooth BepuPhysics forces
            │       └── LocalAvoidance  →  steering around nearby agents
            └── PhysicsWorld (BepuPhysics, 125 Hz)
                        └── WebSocket → Unity (debug only)
```

### Critical Design Rule: AgentConfig is Single Source of Truth

`AgentConfig` (Height, Radius, MaxSlope, MaxClimb) must be the same instance passed to NavMesh generation, PathfindingService, and MovementController. Misalignment causes agents to fall through geometry or get stuck.

### Production Standard: MotorCharacterController

The motor-based controller is the production standard. It uses smooth acceleration via BepuPhysics constraint forces instead of direct velocity setting:
- 51.5% less path deviation vs velocity-based
- 32% faster movement completion
- Zero replanning on the Agent-3 10m vertical climb scenario

The old `CharacterController` (velocity-based) is kept for reference only — do not use for new work.

---

## How to Run

```bash
dotnet run --project Spatial.TestHarness -- enhanced               # 5-agent showcase
dotnet run --project Spatial.TestHarness -- enhanced 10            # 10-agent stress test
dotnet run --project Spatial.TestHarness -- scale 50               # 50-agent scale test
dotnet run --project Spatial.TestHarness -- motor-vs-velocity      # controller comparison
dotnet run --project Spatial.TestHarness -- obstacle-rebake-visual # tiled NavMesh rebake demo
dotnet run --project Spatial.TestHarness -- agent-collision
dotnet run --project Spatial.TestHarness -- local-avoidance
dotnet run --project Spatial.TestHarness -- path-validation
```

Connect Unity client to `ws://localhost:8181` for real-time 3D visualization.

---

## Game Server Integration (Minimal)

```csharp
var agentConfig = new AgentConfig { Height = 2.0f, Radius = 0.4f, MaxSlope = 45f, MaxClimb = 0.5f };

// Bake once at startup, share across rooms
NavMeshData baked = World.BakeNavMesh("worlds/arena.obj", agentConfig);

using var world = new World(baked, agentConfig);
world.OnDestinationReached += (id, pos) => { /* arrived */ };

world.Spawn(playerId, spawnPosition);
world.Move(playerId, targetPosition);

while (running)
    world.Update(0.008f);  // 125 Hz fixed timestep
```

See [GAME_SERVER_INTEGRATION.md](GAME_SERVER_INTEGRATION.md) for the full guide.

---

## Current Status

| System | Status |
|--------|--------|
| Physics (BepuPhysics v2) | ✅ Complete |
| NavMesh + Pathfinding (DotRecast) | ✅ Complete |
| Tiled NavMesh + runtime tile updates | ✅ Complete |
| Motor-based movement | ✅ Complete (production standard) |
| Local avoidance | ✅ Complete |
| Agent collision / push / knockback | ✅ Complete |
| Dynamic obstacle spawn/despawn + NavMesh rebake | ✅ Complete |
| Multi-size agents (per-config NavMesh) | ✅ Complete |
| Option D tiered target fallback | ✅ Complete |
| WebSocket visualization | ✅ Complete |
| Scale validation (50 agents) | ✅ Complete |

---

## Known Limitations

| Issue | Priority |
|-------|----------|
| `CharacterController` (velocity-based) not formally `[Obsolete]` | Low |
| `ValidateAndReplanIfNeeded()` is a placeholder | Medium |
| Small agents (halfHeight ≤ 1.0) may bounce on flat planes — raycast self-hit bug | Medium |
| `World.GetPosition` returns capsule center Y, not foot Y | Low |
