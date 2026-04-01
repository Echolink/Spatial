# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build solution
dotnet build Spatial.sln

# Run tests (common modes)
dotnet run --project Spatial.TestHarness -- enhanced          # Multi-agent showcase (5 agents)
dotnet run --project Spatial.TestHarness -- enhanced 10       # Custom agent count
dotnet run --project Spatial.TestHarness -- scale 50          # Scale test (50+ agents)
dotnet run --project Spatial.TestHarness -- motor-vs-velocity # Controller comparison
dotnet run --project Spatial.TestHarness -- agent-collision
dotnet run --project Spatial.TestHarness -- local-avoidance
dotnet run --project Spatial.TestHarness -- path-validation
```

The test harness also starts a WebSocket visualization server on port 8181. Connect a Unity client (`Unity/QUICK_START.md`) for real-time 3D rendering.

## Architecture

Spatial is a server-authoritative physics + pathfinding framework for multiplayer game servers (.NET 8.0).

**Layer stack (top → bottom):**
```
Game Server Code
    └── MovementController          (Spatial.Integration) — orchestrates everything
        ├── PathfindingService      (Spatial.Integration) — path planning + validation
        │   ├── Pathfinder          (Spatial.Pathfinding) — DotRecast NavMesh queries
        │   └── PathSegmentValidator — MaxClimb/MaxSlope checks with PathAutoFix
        └── MotorCharacterController (Spatial.Integration) — PRODUCTION standard
            └── PhysicsWorld        (Spatial.Physics) — BepuPhysics v2 wrapper
```

**Modules:**
- `Spatial.Physics` — BepuPhysics v2.4.0 wrapper (gravity, collisions, deterministic sim at 125 FPS / 0.008s timestep)
- `Spatial.Pathfinding` — DotRecast 2026.1.1 NavMesh generation and path queries
- `Spatial.Integration` — Glues physics + pathfinding; contains the movement controllers, local avoidance, and NavMesh builder
- `Spatial.Server` — Fleck WebSocket server broadcasting simulation state to Unity
- `Spatial.MeshLoading` — OBJ mesh loader for world geometry
- `Spatial.TestHarness` — All test/demo entry points; `Program.cs` is the CLI router

## Critical Design Constraints

**AgentConfig is the single source of truth.** The same `AgentConfig` instance (Height, Radius, MaxSlope, MaxClimb) must be passed to NavMesh generation, `PathfindingService`, and `MovementController`. Mismatches cause agents to fall through geometry or get stuck.

**Use `MotorCharacterController`, not `CharacterController`.** The motor-based controller uses BepuPhysics constraint solver for smooth movement. It was adopted as the production standard after Phase 4 testing (51.5% less distance traveled, 32% faster, zero replanning). `CharacterController` is kept for reference only.

**BepuPhysics v2 specifics:**
- Static bodies use `simulation.Statics`; dynamic bodies must be explicitly awakened when velocity is set
- Use `SpringSettings(30f, 1f)` for stable ground contacts
- Use low friction (0.1) for character bodies
- `BufferPool` must be properly disposed

**NavMesh generation:**
- Cell size 0.3f recommended for quality
- `FilterOccludedWalkableAreas()` prevents navmesh from generating under obstacles
- `RcAreaModification(0x3f)` for walkable areas
- `PathAutoFix = true` (default) inserts intermediate waypoints for steep segments

## Git Conventions

Conventional commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `perf:`, `style:`. First line under 72 characters. Focus on "why" not "what". Do not push unless explicitly asked.
