# Off-Mesh Link Design Review

## Current State

### What Already Exists

`SimpleInputGeomProvider` in `Spatial.Pathfinding/NavMeshGenerator.cs` already has the infrastructure:

- `AddOffMeshConnection(start, end, radius, bidir, area, flags)` — never called
- `GetOffMeshConnections()` — returns stored connections
- `RemoveOffMeshConnections(predicate)` — removal support

The data structures are prepared but nothing populates them.

---

## Pathfinding Integration

### Does the pathfinder automatically use off-mesh links?

**Yes.** Once connections are baked into the NavMesh, `DtNavMeshQuery.FindPath()` treats off-mesh polygons as regular graph edges and routes through them when they reduce total path cost. No changes needed in the routing logic itself.

### What does need adding

`DtNavMeshQuery.FindStraightPath()` returns `DtStraightPath[]` where each entry has a `flags` field. When `flags & DT_STRAIGHTPATH_OFFMESH_CONNECTION != 0`, that waypoint pair is a link traversal. Currently `PathfindingService` ignores flags entirely and only extracts the `Vector3`.

**Required change:** Read those flags and annotate `PathResult` so `MovementController` knows which segments need special traversal handling. Options:
- Parallel `WaypointFlags[]` array on `PathResult`
- Richer `PathSegment` type with `IsOffMeshLink` and `LinkType` fields

---

## Link Types and Physics Handling

| Link Type | Physics Approach |
|-----------|-----------------|
| **Jump / gap** | Kinematic arc traversal (see below) |
| **Teleport** | `PhysicsWorld.TeleportEntity()` directly to exit, no arc |
| **Climb / ladder** | New `CLIMBING` state — disable gravity, constrain to line segment, apply upward velocity |

### Why not use the existing `Jump(entityId, force)`?

`Jump()` applies a physics impulse and transitions the agent to `AIRBORNE`. Any collision or knockback during flight sends the agent wherever physics takes it — it can land in a hole, fall off the map, or be deflected by another agent.

For NPC link traversal, **arrival must be guaranteed.**

### Kinematic Arc Traversal (recommended for jump links)

Add a new `LINK_TRAVERSAL` state to `MotorCharacterController`. While in this state, the agent is driven by position override rather than physics forces:

```
Each physics tick while LINK_TRAVERSAL:
  t = LinkElapsed / LinkDuration          // 0 → 1
  horizontal = Lerp(LinkEntry, LinkExit, t)
  arc        = Sin(π × t) × JumpArcHeight // peaks at midpoint
  targetPos  = horizontal + Up × arc

  PhysicsWorld.TeleportEntity(entityId, targetPos)
  PhysicsWorld.SetEntityVelocity(entityId, Zero)

  LinkElapsed += deltaTime
  if t >= 1.0 → arrive at LinkExit exactly → transition to RECOVERING
```

**Benefits:**
- Agent visually arcs through the air (looks like a jump)
- Landing position is mathematically guaranteed to be the link exit point
- Knockback attempts during traversal can be discarded (same as during a cutscene)
- `JumpArcHeight` and `LinkDuration` can be configured per link or derived from horizontal distance

---

## Coordinate System Pipeline

Understanding this is critical before choosing an authoring approach.

### Full pipeline transform

| Stage | File | X | Y | Z |
|-------|------|---|---|---|
| OBJ Load | `ObjMeshLoader.cs` | raw | raw | raw |
| NavMesh Gen | `NavMeshGenerator.cs` | same | same | same |
| NavMesh Build | `NavMeshBuilder.cs` | same | same | same |
| WorldBuilder | `WorldBuilder.cs` | scale/rot/translate | same | same |
| Serialize to Unity | `SimulationStateBuilder.cs` | same | same | same |
| **Unity client** | `EntityVisualizer.cs` | **-X** | same | same |

**Key facts:**
- Blender uses **Z-up right-handed**
- OBJ export from Blender applies a **Y/Z swap** (standard OBJ format is Y-up)
- Server uses those OBJ coordinates unchanged (Y-up right-handed)
- Unity applies an **X-axis negation** for left-hand/right-hand conversion

A point at `(10, 5, 3)` in Blender's viewport becomes `(10, 3, 5)` in the OBJ/server, and `(-10, 3, 5)` in Unity.

**Consequence:** Any hand-written coordinate file is unusable by a designer — they would need to mentally apply both the Y/Z swap and the X negation. This rules out manually authored JSON.

---

## Authoring Approach Options

### Option 1 — Named marker objects in Blender + Python export script

Designer places empty axes or small cubes in Blender named with a convention:

```
offmesh_jump_01_start
offmesh_jump_01_end
offmesh_teleport_02_start
offmesh_teleport_02_end
```

A Blender Python export script (~30 lines) reads all `offmesh_*` objects, applies the same Y/Z swap that OBJ export uses, and writes a sidecar JSON file automatically. The designer places objects visually — never touches coordinates.

**Pros:** Designer-friendly, separate from physics mesh, easy to extend  
**Cons:** Requires a Blender script and a sidecar file loader in the game

---

### Option 2 — Named mesh groups inside the OBJ (recommended for now)

Blender lets you name mesh objects. The OBJ exporter writes `o objectname` headers. The OBJ loader already iterates these groups — add a filter pass that:

1. Detects groups whose name starts with `offmesh_`
2. Extracts the centroid of that group's vertices as the link point
3. Pairs `_start` / `_end` groups by shared ID (e.g., `offmesh_jump_01`)
4. Registers the pair via `AddOffMeshConnection()` in the NavMesh builder
5. Skips those groups when building the physics world

**In Blender:** Designer places two small cube objects named `offmesh_jump_01_start` and `offmesh_jump_01_end` at the edges of the gap. Normal OBJ export. No extra files, no extra steps.

**Pros:** Zero extra tooling, zero extra files, works with existing OBJ pipeline, coordinate conversion is automatic (OBJ exporter handles it)  
**Cons:** Marker geometry must be filtered out from physics/NavMesh; naming convention must be documented for designers

---

### Option 3 — Blender addon with UI panel (future)

A `Spatial` Blender addon adds a panel with "Add Jump Link", "Add Teleport Link" buttons. Designer clicks two points, selects type. Addon stores data as Blender custom properties and exports correctly on OBJ export. Most professional authoring experience; highest implementation cost.

---

## Recommended Implementation Order

1. **OBJ loader:** Detect and extract `offmesh_*` named groups, output link definitions
2. **NavMesh builder:** Accept link definitions, call `AddOffMeshConnection()` at build time, exclude marker geometry from physics
3. **PathfindingService:** Read `DtStraightPath` flags, annotate `PathResult` with link segment info
4. **MovementController:** Detect link entry waypoints, dispatch to traversal handler
5. **MotorCharacterController:** Implement `LINK_TRAVERSAL` state with kinematic arc

Each step is independently testable. Steps 1–2 can be verified by checking the NavMesh visually in Unity (links appear as colored arcs in DotRecast debug output). Steps 3–5 can be tested with a simple two-platform scene.
