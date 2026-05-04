# Off-Mesh Links — Procedural World Generation Design

## The Core Problem

In a static level, off-mesh links are known before the game runs. In a procedural world, geometry isn't known until generation completes. Links must be **discovered algorithmically** after geometry exists but before (or during) NavMesh use.

---

## The Critical Timing Constraint

Off-mesh connections must be registered in `SimpleInputGeomProvider` **before** `RcBuilder.Build()` is called. They get baked into NavMesh tile data during that call — you cannot inject them into a finished `DtNavMesh`.

This means any scan is a **pre-build geometry scan**, not a post-build one. The correct pipeline order is:

```
ExtractRawMeshGeometry()
    ↓
ScanBoundaryEdges(vertices, indices)
    ↓  finds candidate pairs, calls geomProvider.AddOffMeshConnection()
RcBuilder.Build(geomProvider, ...)   ← consumes connections here
    ↓
DtNavMesh (links baked in)
```

---

## Mental Model: NavMesh Boundary Edge Analysis

After building the NavMesh, every polygon edge is one of:

- **Internal edge** — shared by two polygons, fully traversable
- **Boundary edge** — belongs to only one polygon; the NavMesh stops here

Boundary edges on different NavMesh islands that face each other across a jumpable gap are natural off-mesh link candidates. This is the foundation of every automated link generation system, including Unity's NavMesh Link auto-generation.

```
Platform A NavMesh          Platform B NavMesh
[==========|                |==========]
           ↑                ↑
     boundary edge    boundary edge
           ←  gap width  →
```

If gap width ≤ max jump distance and height difference is within range, register a jump link between the nearest points on those edges.

---

## Three Approaches

### Approach A — Pre-build geometry scan (recommended starting point)

Scan the raw vertex/triangle arrays that `ExtractRawMeshGeometry()` already produces, before the NavMesh build. An edge is a boundary if it is shared by only one triangle and the adjacent area is non-walkable or absent.

**Best for:** Worlds fully generated up front (dungeons, grid-based levels, island maps). Build once at startup.

**Algorithm:**
```
1. Enumerate boundary edges in raw triangle mesh
2. Spatial-index them for fast neighbor lookup (grid or BVH)
3. For each boundary edge:
   a. Query nearby boundary edges on other NavMesh islands
   b. For each candidate pair:
      - horizontal gap ≤ maxJumpDistance?
      - height difference ≤ maxJumpHeight (up) or maxFallHeight (down)?
      - edge normals roughly facing each other? (avoid linking same-wall edges)
   c. If valid: call geomProvider.AddOffMeshConnection()
4. Proceed to RcBuilder.Build() with connections populated
```

**Tunable parameters:**
- `maxJumpDistance` — max horizontal gap an agent can cross
- `maxJumpHeight` — max upward height difference
- `maxFallHeight` — max downward drop (usually larger than jump height)
- `minEdgeLength` — ignore tiny boundary slivers that aren't real ledges

---

### Approach B — Generation-time attachment points

If the world generator assembles pieces (rooms, chunks, platform segments), each piece declares link attachment points as part of its definition. The generator knows about potential links while placing pieces.

```csharp
PlatformPiece {
    Geometry: ...,
    LinkAttachments: [
        { LocalPos: (5, 0, 0), Direction: +X, Type: Jump },
        { LocalPos: (-5, 0, 0), Direction: -X, Type: Jump }
    ]
}
```

When two pieces are placed near each other, the system checks if any attachment points are within range and face each other, then registers the link. No post-processing pass needed — links are known the moment pieces are placed.

**Best for:** Structured generators that assemble prefab pieces. Most accurate because the generator has semantic knowledge (it knows this is a gap between two platforms, not two walls).

---

### Approach C — Runtime tile rebuilds (streaming / infinite worlds)

For worlds that load and unload chunks at runtime, links must be generated and removed per tile. DotRecast supports tile-based NavMesh rebuilding — each tile can be rebuilt independently via the existing `Pathfinder.RebuildTile()` (line 177–246).

**When a chunk loads:**
1. Build its NavMesh tile
2. Scan its boundary edges against already-loaded neighbor tiles
3. Register links between them

**When a chunk unloads:**
- Remove its tile and any links that referenced it

**Best for:** Infinite or streaming worlds. Most complex; required when chunks load/unload at runtime.

---

## Two-Pass Build Alternative

If you want to scan NavMesh polygon edges instead of raw triangles (more accurate, avoids re-scanning geometry the NavMesh already processed):

1. Build NavMesh without links
2. Scan resulting polygon boundary edges via `tile.data.polys[].neiPoly[]` — a boundary edge is where `neiPoly[edge] == 0`
3. Rebuild tiles with discovered links baked in

**Tradeoff:** More accurate gap detection, but doubles build time. Acceptable for static worlds built once at startup; matters more for streaming worlds.

---

## What Already Exists in the Codebase

| Component | Location | Status |
|-----------|----------|--------|
| `AddOffMeshConnection()` | `NavMeshGenerator.cs:742` | Exists, never called |
| `GetOffMeshConnections()` | `NavMeshGenerator.cs:735` | Exists, never called |
| Tile + polygon iteration | `NavMeshData.cs:102–238` | Exists (`ExportToObj`, `AnalyzeSlopes`) |
| Area-based walkable classification | `NavMeshBuilder.ExtractStaticColliderGeometry()` | Exists |
| Runtime tile rebuild | `Pathfinder.RebuildTile():177–246` | Exists |
| `DtStraightPath` flags reading | `PathfindingService` | Missing — flags currently ignored |

The infrastructure is approximately 80% complete. The missing piece is the scanning algorithm and the pipeline insertion point.

---

## Landing Zone Validation

A gap-detection scan can find candidate links but cannot always confirm the landing is safe. After identifying a candidate pair, validate:

- NavMesh query at the `_end` point — is it actually on walkable NavMesh?
- Landing area radius ≥ agent radius — the agent can't land on a 0.1m ledge
- Optionally: simulate the jump arc and verify it doesn't clip geometry mid-flight

This prevents the pathfinder from routing agents through links that look valid geometrically but would strand or clip the agent on arrival.

---

## Implementation Plan

### Phase 1 — PathResult annotation (needed regardless of link source)

`FindStraightPath()` already returns flags per waypoint. Currently ignored.

- Read `DT_STRAIGHTPATH_OFFMESH_CONNECTION` flag in `PathfindingService`
- Add `IsOffMeshLink` and `LinkType` fields to `PathResult` waypoints (or a parallel flags array)
- `MovementController` detects link entry waypoints and dispatches to traversal handler

### Phase 2 — Boundary edge scanner (Approach A)

New `BoundaryEdgeScanner` class:

- Takes raw vertex/index arrays from `ExtractRawMeshGeometry()`
- Outputs `List<OffMeshLinkCandidate>` with start/end positions and classified type
- Inserted in `NavMeshGenerator.GenerateNavMeshDirect()` or `GenerateTiledNavMesh()` before `RcBuilder.Build()`
- Calls `geomProvider.AddOffMeshConnection()` for each candidate

### Phase 3 — Traversal handling in movement controllers

- `MovementController`: detect link entry waypoints, dispatch to handler per link type
- `MotorCharacterController`: implement `LINK_TRAVERSAL` state with kinematic arc (see `OFFMESH_LINKS_DESIGN.md` for full detail)

---

## Effort Estimate

| Phase | Effort |
|-------|--------|
| Boundary edge scan on raw geometry | 2–4 hours |
| Gap classification heuristics + tuning | 2–3 hours |
| Pipeline insertion + `AddOffMeshConnection` calls | 1 hour |
| `PathResult` annotation (flag reading) | 1 hour |
| Testing + visualization verification in Unity | 3–4 hours |
| **Total** | **9–13 hours** |

The geometry scanning algorithm is the only genuinely new work. Everything else is wiring existing infrastructure together.

---

## Key API Reference

| API | File | Line | Notes |
|-----|------|------|-------|
| `SimpleInputGeomProvider.AddOffMeshConnection()` | `NavMeshGenerator.cs` | 742 | Must call before `RcBuilder.Build()` |
| `DtNavMesh.GetMaxTiles()` | `NavMeshData.cs` | 112 | Tile count for iteration |
| `DtNavMesh.GetTile(i)` | `NavMeshData.cs` | 114 | Access `DtMeshTile` |
| `DtMeshData.polys[]` | `NavMeshData.cs` | 133 | `DtPoly` array |
| `DtPoly.neiPoly[]` | — | — | `0` = boundary edge |
| `DtPoly.vertCount` | `NavMeshData.cs` | 139 | Vertices per polygon |
| `DtMeshData.verts[]` | `NavMeshData.cs` | 124 | Float triplets (x,y,z) |
| `NavMeshData.InvalidateQuery()` | `NavMeshData.cs` | 93 | Rebuild query after tile changes |
| `Pathfinder.RebuildTile()` | `Pathfinder.cs` | 177 | Runtime tile rebuild pattern |

## Related Documents

- `OFFMESH_LINKS_DESIGN.md` — General off-mesh link design (static levels, physics handling, kinematic arc traversal)
- `LEVEL_DESIGNER_OFFMESH_GUIDE.md` — Blender workflow for manually authored links
