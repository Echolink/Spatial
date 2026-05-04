# Spatial

Server-authoritative physics and pathfinding framework for multiplayer game servers (.NET 8.0).

## What It Does

Spatial provides a trusted, cheat-proof movement backbone for multiplayer games. The server owns all positions and paths — clients never fake movement.

- **NavMesh generation** — converts a 3D level mesh (`.obj`) into a walkable polygon network
- **Pathfinding** — A* via DotRecast, with per-segment validation (MaxClimb/MaxSlope) and automatic waypoint insertion
- **Physics simulation** — BepuPhysics v2 capsule bodies with gravity, collision, and constraint-based movement at 125 Hz
- **Movement control** — path-following, local avoidance, automatic replanning, knockback, jump
- **Multi-size agents** — separate NavMesh baked per `AgentConfig`; each unit routes on its own mesh
- **Runtime NavMesh updates** — tile-based partial rebuilds for doors, destructibles, and resource nodes

The `Unity/` client is a debug visualization tool only — not a game client.

## Architecture

```
Game Server Code
    └── World  (Spatial.Integration) — single entry point
        ├── MovementController        — path-following, local avoidance, replanning
        │   ├── PathfindingService    — DotRecast A* + path validation + PathAutoFix
        │   │   └── Pathfinder        — raw NavMesh polygon queries
        │   └── PathSegmentValidator  — MaxClimb/MaxSlope checks
        └── MotorCharacterController  — BepuPhysics constraint-based movement (production standard)
            └── PhysicsWorld          — BepuPhysics v2 wrapper (125 Hz)
```

## Quick Start

```bash
dotnet build Spatial.sln

dotnet run --project Spatial.TestHarness -- enhanced          # 5-agent showcase
dotnet run --project Spatial.TestHarness -- enhanced 10       # 10-agent stress test
dotnet run --project Spatial.TestHarness -- scale 50          # 50-agent scale test
dotnet run --project Spatial.TestHarness -- motor-vs-velocity # controller comparison
dotnet run --project Spatial.TestHarness -- obstacle-rebake-visual  # tiled NavMesh rebake demo
dotnet run --project Spatial.TestHarness -- agent-collision
dotnet run --project Spatial.TestHarness -- local-avoidance
dotnet run --project Spatial.TestHarness -- path-validation
```

Each mode starts a WebSocket server on port 8181. Connect the Unity client for real-time 3D visualization — see [Unity/QUICK_START.md](Unity/QUICK_START.md).

## Integrating into Your Game Server

The `World` façade is the single entry point:

```csharp
var agentConfig = new AgentConfig { Height = 2.0f, Radius = 0.4f, MaxSlope = 45f, MaxClimb = 0.5f };

// Bake once at startup (100–500 ms), share across all rooms on this map
NavMeshData baked = World.BakeNavMesh("worlds/arena.obj", agentConfig);

// One World per room — cheap to create, fully isolated physics
using var world = new World(baked, agentConfig);
world.OnDestinationReached += (id, pos) => { /* trigger idle AI, rewards, etc. */ };

// Per-tick game loop
world.Spawn(playerId, spawnPosition);
world.Move(playerId, clickedPosition);
world.Update(0.008f);  // fixed 125 Hz timestep
Vector3 pos = world.GetPosition(playerId);
```

See [docs/GAME_SERVER_INTEGRATION.md](docs/GAME_SERVER_INTEGRATION.md) for the full guide.

## Project Structure

| Module | Purpose |
|--------|---------|
| `Spatial.Physics` | BepuPhysics v2.4.0 wrapper — gravity, collision, deterministic 125 Hz simulation |
| `Spatial.Pathfinding` | DotRecast 2026.1.1 — NavMesh generation and A* queries |
| `Spatial.Integration` | `World` façade, movement orchestration, local avoidance, NavMesh builder |
| `Spatial.Server` | Fleck WebSocket server — broadcasts simulation state to Unity (port 8181) |
| `Spatial.MeshLoading` | OBJ mesh loader with group metadata and off-mesh link detection |
| `Spatial.TestHarness` | All test/demo entry points; `Program.cs` is the CLI router |
| `Unity/` | Real-time 3D visualization client |

## Features

### Movement
- `MotorCharacterController` — constraint-based movement via BepuPhysics motor (51.5% less path deviation, 32% faster, zero replanning vs velocity-based)
- `PathAutoFix` — automatically inserts intermediate waypoints for segments that exceed MaxClimb/MaxSlope
- Option D tiered fallback for unreachable targets: nearest reachable → furthest reachable toward target → hard cancel
- Local avoidance — agents steer around each other without triggering replanning
- Knockback and jump with automatic pathfinding pause on launch and resume on landing

### NavMesh
- Multi-size agents — `MultiAgentNavMesh` bakes one NavMesh per `AgentConfig`; each spawned unit routes on its own mesh
- Runtime tile updates — `SpawnObstacle`/`DespawnObstacle` atomically update physics collider and NavMesh tiles (~1–5 ms per operation)
- Tiled NavMesh via `NavMeshConfiguration { EnableTileUpdates = true }` — required for all runtime updates

### Game Server API
- `World` façade — create, spawn, move, update, despawn in ~10 lines
- `NavMeshData` is read-only and safe to share across rooms using the same map
- Events: `OnDestinationReached`, `OnMovementStarted`, `OnPathReplanned`, `OnMovementProgress`
- Queries: `GetPosition`, `GetVelocity`, `GetState` (`GROUNDED` / `AIRBORNE` / `RECOVERING`)

## Documentation

| Doc | Contents |
|-----|----------|
| [docs/GAME_SERVER_INTEGRATION.md](docs/GAME_SERVER_INTEGRATION.md) | Full integration guide — World API, events, game loop, multi-size agents, configuration reference |
| [docs/PRODUCTION_ARCHITECTURE.md](docs/PRODUCTION_ARCHITECTURE.md) | Architecture decisions, controller comparison, dynamic NavMesh, migration guide |
| [docs/GAME_SERVER_FAQ.md](docs/GAME_SERVER_FAQ.md) | Common integration questions |
| [docs/PROJECT_SUMMARY.md](docs/PROJECT_SUMMARY.md) | High-level overview for new contributors |
| [Unity/QUICK_START.md](Unity/QUICK_START.md) | 5-minute Unity visualization setup |
| [Unity/UNITY_SETUP_GUIDE.md](Unity/UNITY_SETUP_GUIDE.md) | Detailed Unity setup and troubleshooting |

## Requirements

- .NET 8.0 SDK
- (Optional) Unity 2021.3+ for visualization

## Dependencies

- [BepuPhysics v2](https://github.com/bepu/bepuphysics2) — high-performance physics engine
- [DotRecast](https://github.com/ikpil/DotRecast) — C# port of Recast/Detour navigation
- [Fleck](https://github.com/statianzo/Fleck) — WebSocket server
- [Newtonsoft.Json](https://www.newtonsoft.com/json) — JSON serialization
