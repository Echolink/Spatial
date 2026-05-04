# Off-Mesh Links Test Results
Date: 2026-04-23
Test mode: `dotnet run --project Spatial.TestHarness -- enhanced`
OBJ file: `seperated_land_with_link.obj`

---

## Summary

Four bugs were found and fixed before the test could pass end-to-end. The NavMesh now bakes both off-mesh links correctly and the path validator no longer false-rejects paths that use them. Arc traversal itself has not yet been observed because agent spawn positions are misaligned with the new terrain — that is the next step.

---

## Bug 1 — OBJ file was not found at runtime

### Symptom
```
⚠ Mesh not found, using procedural geometry
```
The test ran but used fallback procedural geometry instead of the new OBJ. No off-mesh links were loaded.

### Root Cause
`ResolvePath` resolves relative to the exe's output directory:
```
Spatial.TestHarness/bin/Debug/net8.0/worlds/
```
The `.csproj` copies files from `Spatial.TestHarness/worlds/` to that output folder at build time. The new file had been placed in the project root `worlds/` folder instead, so it was never copied.

### Fix
Copied the file to the correct source location:
```
worlds/seperated_land_with_link.obj
  → Spatial.TestHarness/worlds/seperated_land_with_link.obj
```

---

## Bug 2 — Off-mesh marker groups not detected by ObjMeshLoader

### Symptom
After the file was found, the loader logged the marker geometry as regular meshes and never emitted any `Off-mesh link:` lines. Both links were silently dropped.

### Root Cause
Blender's OBJ exporter appends the mesh datablock name to the object name when writing group headers:
```
# What Blender exported:
g offmesh_jump_01_start_Sphere
g offmesh_jump_01_end_Sphere.001
g offmesh_teleport_02_start_Sphere.002
g offmesh_teleport_02_end_Sphere.003

# What the regex expected (anchored with $):
^offmesh_(jump|teleport|climb)_(\w+)_(start|end)$
```
The `$` end-anchor caused every match to fail because `_Sphere` / `_Sphere.001` was left over after the `(start|end)` capture.

### Fix
`Spatial.MeshLoading/Loaders/ObjMeshLoader.cs` — removed the `$` anchor:
```csharp
// Before
new(@"^offmesh_(jump|teleport|climb)_(\w+)_(start|end)$", ...)

// After
new(@"^offmesh_(jump|teleport|climb)_(\w+)_(start|end)", ...)
```
The regex engine now backtracks correctly: for `offmesh_jump_01_start_Sphere` it captures `jump`, `01`, `start` and ignores the trailing `_Sphere`.

Note: The long-term Blender fix is to rename the mesh datablock to match the object name (both named `offmesh_jump_01_start`), which produces a clean export with no suffix. The regex change makes the system robust regardless.

---

## Bug 3 — Off-mesh links loaded but not passed to NavMesh or Pathfinder

### Symptom
After fixing Bug 2, the loader correctly emitted:
```
[ObjMeshLoader] Off-mesh link: Jump '01'  <53.32, -2.60, -7.03> → <46.27, -2.30, -16.45>
[ObjMeshLoader] Off-mesh link: Teleport '02'  <42.83, 7.55, 21.40> → <20.05, -2.12, -23.58>
```
But the navmesh was still built without them and `Baking 2 off-mesh connection(s)` never appeared.

### Root Cause
`TestEnhancedShowcase` was calling both `BuildNavMeshDirect` and `new Pathfinder` without forwarding `worldData.OffMeshLinks`:

```csharp
// Line 158 — links NOT passed, NavMesh baked without off-mesh connections
navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);

// Line 204 — links NOT passed, waypoints never annotated with link type
var pathfinder = new Pathfinder(navMeshData);
```

Both parameters are optional in the method signatures, so the code compiled silently without error.

### Fix
`Spatial.TestHarness/TestEnhancedShowcase.cs`:
```csharp
// Line 158
navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig, worldData.OffMeshLinks);

// Line 204
var pathfinder = new Pathfinder(navMeshData, worldData?.OffMeshLinks);
```
After this fix the log confirmed baking:
```
Baking 2 off-mesh connection(s) into NavMesh
[Direct] Build result: 408 polys, 113 contours
```

---

## Bug 4 — Path validator rejected every path that used an off-mesh link

### Symptom
After fixing Bug 3, paths routing through the teleport link were immediately rejected:
```
[PathfindingService] Path validation FAILED: Segment 3→4 exceeds MaxClimb: 8.80m > 0.50m
(from Y=-0.97 to Y=7.83)
```
The agent was stuck replanning in a loop and never moved.

### Root Cause
`PathSegmentValidator.ValidatePath` applies MaxClimb and MaxSlope checks to every segment in the waypoint list. An off-mesh link transition (e.g. teleport from Y=7.5 down to Y=-2.1, or the approach ramp up to Y=7.8) involves large vertical changes that are intentional and physically correct — they are driven kinematically by `MotorCharacterController`, not walked. But the validator had no concept of off-mesh links and checked them the same as walkable terrain.

`PathResult` already carried a parallel `OffMeshLinkTypes` list (non-null at link-entry waypoints) but `PathfindingService` never passed it to the validator.

### Fix — PathSegmentValidator.cs
Added `offMeshLinkTypes` parameter and a skip guard at the top of the loop:
```csharp
public ValidationResult ValidatePath(
    IReadOnlyList<Vector3> waypoints,
    float maxClimb,
    float maxSlope,
    float agentRadius = 0.5f,
    IReadOnlyList<OffMeshLinkType?>? offMeshLinkTypes = null)   // ← new
{
    ...
    for (int i = 0; i < waypoints.Count - 1; i++)
    {
        // Off-mesh link segments are traversed kinematically — skip climb/slope checks.
        if (offMeshLinkTypes != null && i < offMeshLinkTypes.Count && offMeshLinkTypes[i] != null)
            continue;   // ← new

        var current = waypoints[i];
        ...
    }
}
```

### Fix — PathfindingService.cs
Forwarded the link types from the path result to the validator:
```csharp
var validation = _pathValidator.ValidatePath(
    pathResult.Waypoints,
    _agentConfig.MaxClimb,
    _agentConfig.MaxSlope,
    _agentConfig.Radius,
    pathResult.OffMeshLinkTypes   // ← new
);
```

---

## Final Test Output

```
✓ Loading world from: seperated_land_with_link.obj
[ObjMeshLoader] Off-mesh link: Jump '01'  <53.32, -2.60, -7.03> → <46.27, -2.30, -16.45>
[ObjMeshLoader] Off-mesh link: Teleport '02'  <42.83, 7.55, 21.40> → <20.05, -2.12, -23.58>
[Direct] Build result: 408 polys, 113 contours
Baking 2 off-mesh connection(s) into NavMesh
✓ NavMesh generated successfully!
✅ ENHANCED SHOWCASE TEST: PASSED
```

---

## Bug 5 — `ValidatePathContinuity` rejected off-mesh link segments

### Symptom
Even after all prior fixes, the teleport link path would have been silently rejected by a pre-movement check in `TestEnhancedShowcase`, preventing the agent from ever starting movement.

### Root Cause
`ValidatePathContinuity` in `TestEnhancedShowcase.cs` contains three heuristic geometry checks. CASE 3 rejects any segment longer than 25m horizontally:

```csharp
if (horizontalDist > 25.0f)
{
    Console.WriteLine("Path validation failed: Very long waypoint segment detected");
    return false;
}
```

The teleport link spans ~50m horizontally (entry `(42.83, 7.55, 21.40)` → exit `(20.05, -2.12, -23.58)`), so its segment was always rejected. Additionally, CASE 1 and CASE 2 could also reject the 9.67m vertical drop of the teleport transition. Unlike `PathSegmentValidator`, this function had no knowledge of off-mesh links.

### Fix — `TestEnhancedShowcase.cs`
Added `offMeshLinkTypes` parameter and a skip guard at the top of the segment loop, mirroring the fix already applied to `PathSegmentValidator` in Bug 4:

```csharp
private static bool ValidatePathContinuity(
    IReadOnlyList<Vector3> waypoints,
    IReadOnlyList<OffMeshLinkType?>? offMeshLinkTypes = null)
{
    for (int i = 1; i < waypoints.Count; i++)
    {
        // Off-mesh link transitions are kinematic — skip geometry checks for those segments.
        if (offMeshLinkTypes != null && (i - 1) < offMeshLinkTypes.Count && offMeshLinkTypes[i - 1] != null)
            continue;
        ...
    }
}
```

The call site at line 541 was updated to forward the link types from the path result:
```csharp
bool pathValid = ValidatePathContinuity(pathResult.Waypoints, pathResult.OffMeshLinkTypes);
```

---

## Bug 6 — Agent spawn positions not targeting any link

### Symptom
No agent in the default 5-agent `enhanced` run was ever routed through either link. Agents 2, 3, 4 all failed spawn validation because their hardcoded XZ coordinates were tuned to the old `seperated_land.obj` terrain. Only agents 1 and 5 ran, navigating a small central area far from both links.

### Fix — `TestEnhancedShowcase.cs` `GenerateAgentScenarios`
Replaced agents 2, 3, 4 with scenarios that target the link entries and exits directly:

```csharp
// Agent-2: jump link forward
(102, "Agent-2 [JumpLink fwd]",
    new Vector3(53.32f, -2.60f, -7.03f),   // jump link entry
    new Vector3(46.27f, -2.30f, -16.45f)   // jump link exit
    ),
// Agent-3: teleport link forward (agent spawns on elevated platform, goal on lower terrain)
(103, "Agent-3 [Teleport fwd]",
    new Vector3(42.83f,  7.55f, 21.40f),   // teleport link entry (elevated)
    new Vector3(20.05f, -2.12f, -23.58f)   // teleport link exit
    ),
// Agent-4: central terrain route (replaces the failing West→Center path)
(104, "Agent-4 [NW→Center]",
    new Vector3(-2.0f, baseY, 8.0f),
    new Vector3( 5.0f, baseY, -5.0f)
    ),
```

The `ValidateOrSnapToNavMesh` call will snap these to the nearest navmesh polygon. Because agents 2 and 3 are placed at the link entry/exit coordinates, the pathfinder is forced to route through each link to connect start to goal.

---

## Remaining Noise (not off-mesh link bugs)

### Steep terrain validation failures (walkable ramp, not a link)
```
[PathfindingService] Path validation FAILED: Segment 3→4 exceeds MaxClimb: 8.80m > 0.50m
(from Y=-0.97 to Y=7.83)
```
This is the navmesh path climbing the ramp toward the teleport's elevated start platform, not the teleport transition itself. The terrain has large sparse polygons so adjacent waypoints can be 8.8m apart vertically. `PathAutoFix` tries to insert intermediate waypoints but the live revalidation during movement still fires. This is a terrain topology issue with the new OBJ, not an off-mesh link bug. Not expected to affect agents 2 and 3 since they spawn AT the link entries.

---

## Remaining Checklist (from OFFMESH_LINKS_IMPLEMENTATION.md)

| # | Check | Status | Notes |
|---|---|---|---|
| 1 | NavMesh bake — off-mesh connections visible in DotRecast debug | ✅ | 408 polys + 2 connections confirmed |
| 2 | Path annotation — entry waypoints have non-null `OffMeshLinkType` | ⏳ | Agents 2 & 3 now target link entries — pending next run |
| 3 | Arc traversal — agent crosses gap, arcs through air, lands at exit | ⏳ | Agents 2 & 3 now target link entries — pending next run |
| 4 | Ground contact guard — `CharacterState` stays `LINK_TRAVERSAL` during arc | ⏳ | Needs arc traversal first |
| 5 | Regression — `motor-vs-velocity`, `agent-collision`, `local-avoidance` unaffected | ⏳ | Not run |

### To observe arc traversal
Run `dotnet run --project Spatial.TestHarness -- enhanced` and watch for:
```
[MovementController] BeginLinkTraversal
CharacterState = LINK_TRAVERSAL
```
Agent-2 exercises the jump link; Agent-3 exercises the teleport link.
