# Off-Mesh Links Implementation

Agents can now traverse gaps, jump between platforms, and use ladders. This document covers the complete pipeline from Blender to physics simulation.

---

## How It Works (End-to-End)

```
Blender OBJ export
    └── ObjMeshLoader          detects offmesh_* groups → OffMeshLinkDef objects in WorldData
        └── NavMeshBuilder     passes OffMeshLinkDef list to NavMeshGenerator
            └── NavMeshGenerator   registers RcOffMeshConnection on SimpleInputGeomProvider
                                   → Detour bakes links into DtNavMesh as off-mesh polygons
                └── Pathfinder     FindPath annotates link-entry waypoints with OffMeshLinkType
                    └── MovementController   detects link-entry waypoint, calls BeginLinkTraversal
                        └── MotorCharacterController   drives kinematic arc in UpdateGroundedState
                            └── SimulationStateBuilder  broadcasts TraversalType + TraversalT
```

---

## Blender Workflow (Designer)

Name your marker objects following this exact convention:

```
offmesh_jump_01_start       offmesh_jump_01_end
offmesh_teleport_02_start   offmesh_teleport_02_end
offmesh_climb_03_start      offmesh_climb_03_end
```

Rules:
- The marker can be any mesh (a small cube or plane works fine). Only its centroid is used.
- The OBJ exporter handles the Y/Z swap automatically — no manual coordinate conversion needed.
- Marker meshes are **not** added to the physics world or navmesh surface. They are invisible to agents.
- Unpaired markers (a `_start` with no matching `_end`, or vice versa) emit a warning and are skipped.

Supported link types:

| Type | Arc shape | Speed | Use case |
|---|---|---|---|
| `jump` | Parabola | 5 m/s | Platform gaps, pits |
| `climb` | Linear Y | 2 m/s | Ladders, ropes |
| `teleport` | Instant | — | Portals, spawn pads |

---

## Wiring in Code

```csharp
// 1. Load world (ObjMeshLoader extracts OffMeshLinkDef objects automatically)
var worldData = worldBuilder.LoadAndBuildWorld(meshFilePath);

// 2. Bake NavMesh with off-mesh links
var navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig, worldData.OffMeshLinks);

// 3. Create Pathfinder with link defs so waypoints are annotated
var pathfinder = new Pathfinder(navMeshData, worldData.OffMeshLinks);

// 4. Create PathfindingService and MovementController as normal
var pathfindingService = new PathfindingService(pathfinder, agentConfig, config);
var motorController    = new MotorCharacterController(physicsWorld);
var movementController = new MovementController(physicsWorld, pathfindingService, agentConfig, config, motorController);

// 5. (Optional) Forward traversal state to Unity via SimulationStateBuilder
var state = SimulationStateBuilder.BuildFromPhysicsWorld(
    physicsWorld, navMeshData,
    getTraversalInfo: id =>
    {
        var info = motorController.GetTraversalInfo(id);
        return info.HasValue ? (info.Value.Type.ToString(), info.Value.T) : null;
    });
```

---

## Arc Behaviour Per Link Type

Traversal is driven kinematically inside `MotorCharacterController.UpdateGroundedState`. Physics forces are suppressed during the arc. Ground contact callbacks cannot interrupt `LINK_TRAVERSAL`.

**Jump** — parabolic arc using `sin(π·t)` as the height curve:
```
Y(t) = Entry.Y + sin(π·t) × arcHeight
arcHeight = horizontalDistance × 0.3
```

**Climb** — straight linear interpolation in Y (models a ladder):
```
Y(t) = Entry.Y + (Exit.Y - Entry.Y) × t
```

**Teleport** — position is set to the exit point immediately; no arc ticks are registered.

After arc completion (`t ≥ 1.0`) the entity enters `RECOVERING` state, then back to `GROUNDED` after `StabilityThreshold` seconds, at which point pathfinding resumes normally.

---

## Tuning Arc Parameters

Default config lives in `Spatial.Integration/LinkTraversalConfig.cs`:

```csharp
[OffMeshLinkType.Jump]     = new(Speed: 5f, MinDuration: 0.5f, ArcHeightScale: 0.3f, ArcShape: Parabola)
[OffMeshLinkType.Climb]    = new(Speed: 2f, MinDuration: 1.0f, ArcHeightScale: 0f,   ArcShape: Linear)
[OffMeshLinkType.Teleport] = new(Speed: 0f, MinDuration: 0f,   ArcHeightScale: 0f,   ArcShape: None)
```

- **Speed** — horizontal traversal speed (m/s). Arc duration = `max(distance / speed, minDuration)`.
- **MinDuration** — floor on traversal time in seconds (prevents zero-duration arcs on very short links).
- **ArcHeightScale** — arc height as a fraction of horizontal distance. 0 = flat.
- **ArcShape** — `Parabola`, `Linear`, or `None` (teleport).

---

## Changed Files

| File | What changed |
|---|---|
| `Spatial.Pathfinding/OffMeshLinkDef.cs` *(new)* | `OffMeshLinkType` enum + `OffMeshLinkDef` class |
| `Spatial.MeshLoading/Spatial.MeshLoading.csproj` | Added ProjectReference → Spatial.Pathfinding |
| `Spatial.MeshLoading/Data/WorldData.cs` | `List<OffMeshLinkDef> OffMeshLinks` |
| `Spatial.MeshLoading/Loaders/ObjMeshLoader.cs` | Regex-based detection of `offmesh_*` groups; centroid extraction; pairing of start+end halves |
| `Spatial.Pathfinding/NavMeshGenerator.cs` | `GenerateNavMeshDirect` accepts `offMeshLinks`; `CreateDetourNavMesh` populates `DtNavMeshCreateParams.offMeshCon*` |
| `Spatial.Pathfinding/PathResult.cs` | `IReadOnlyList<OffMeshLinkType?> OffMeshLinkTypes` (parallel to Waypoints) |
| `Spatial.Pathfinding/Pathfinder.cs` | Stores link defs; annotates waypoints via `DT_STRAIGHTPATH_OFFMESH_CONNECTION` flag |
| `Spatial.Integration/CharacterState.cs` | Added `LINK_TRAVERSAL` |
| `Spatial.Integration/LinkTraversalConfig.cs` *(new)* | `LinkArcShape`, `LinkTraversalConfig`, `LinkTraversalDefaults` |
| `Spatial.Integration/ICharacterController.cs` | `BeginLinkTraversal`, `GetTraversalInfo` |
| `Spatial.Integration/CharacterController.cs` | No-op stubs (legacy controller, not used in production) |
| `Spatial.Integration/MotorCharacterController.cs` | `LinkTraversalData`, `BeginLinkTraversal`, `GetTraversalInfo`, LINK_TRAVERSAL case in `UpdateGroundedState` |
| `Spatial.Integration/MovementController.cs` | `MovementState.OffMeshLinkTypes`; link-entry detection at waypoint-reached; LINK_TRAVERSAL early-return guard |
| `Spatial.Integration/NavMeshBuilder.cs` | `BuildNavMeshDirect` optional `offMeshLinks` parameter |
| `Spatial.Server/SimulationState.cs` | `TraversalType` + `TraversalT` on `EntityState` |
| `Spatial.Server/SimulationStateBuilder.cs` | Optional `getTraversalInfo` delegate for per-entity traversal broadcast |

---

## Unity Client Integration

Each entity snapshot now includes:

```json
{
  "traversalType": "Jump",
  "traversalT": 0.42
}
```

- `traversalType`: `"none"` when not traversing, otherwise `"Jump"`, `"Climb"`, or `"Teleport"`.
- `traversalT`: normalized arc progress `[0, 1]`. Drive animation clips at this normalized time for frame-accurate server sync.

Suggested Unity usage:
```csharp
if (entity.traversalType != "none")
{
    animator.Play(entity.traversalType, 0, entity.traversalT);
    animator.speed = 0; // server drives playback position
}
```

---

## Verification Checklist

1. **NavMesh bake** — Add a test OBJ with `offmesh_jump_01_start` / `offmesh_jump_01_end` markers. Connect the Unity client and confirm the off-mesh arc appears in DotRecast's NavMesh debug overlay.

2. **Path annotation** — Log `pathResult.OffMeshLinkTypes` for a two-platform path. Entry waypoints must have a non-null type; regular waypoints must be `null`.

3. **Arc traversal** — Run `enhanced` and observe an agent cross a gap. It should arc through the air and land at the link exit without replanning during the arc.

4. **Ground contact guard** — Confirm `CharacterState` stays `LINK_TRAVERSAL` throughout the arc (no premature AIRBORNE→RECOVERING transition during the parabola).

5. **Regression** — Run `motor-vs-velocity`, `agent-collision`, and `local-avoidance` to confirm normal movement is unaffected.
