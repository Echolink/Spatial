# Off-Mesh Links — Level Designer Guide

Off-mesh links tell the game's pathfinding system that two points are connected even though there is no walkable floor between them. Use them to create jumps across gaps, teleport portals, or any connection that requires special traversal.

---

## The Core Idea

You place two small marker cubes in Blender — one at the take-off point, one at the landing point — and give them a matching name. The game engine reads those names on load and registers the connection automatically. No JSON, no scripts, no extra export steps.

---

## Step-by-Step: Adding a Jump Link

**1. Model your level normally.** Platforms, walls, floors — nothing changes about how you build the level.

**2. Add the take-off marker.**
- `Shift+A` → Mesh → Cube
- Scale it small: `S` → `0.1` → `Enter`
- Move it (`G`) to the edge of the platform where the agent will jump from. Place it at floor level — the game uses the cube's center point as the agent's foot position.
- In the **Outliner**, rename it: `offmesh_jump_01_start`

**3. Add the landing marker.**
- `Shift+A` → Mesh → Cube
- Scale it small the same way
- Move it to where the agent should land on the other platform, at floor level
- Rename it: `offmesh_jump_01_end`

**4. Export as normal.**
- `File → Export → Wavefront (.obj)`
- Same settings you always use. The markers export with the rest of the level. The game filters them out of physics and NavMesh automatically — they only affect pathfinding.

---

## Naming Convention

The name has three parts separated by underscores:

```
offmesh _ [type] _ [id] _ [start|end]
```

| Part | Options | Notes |
|------|---------|-------|
| prefix | `offmesh` | Always this exact word |
| type | `jump`, `teleport` | Determines how the agent traverses |
| id | `01`, `02`, `03` … | Matches a start to its end — must be identical |
| suffix | `start`, `end` | Which side of the link |

### Examples

```
offmesh_jump_01_start
offmesh_jump_01_end

offmesh_jump_02_start
offmesh_jump_02_end

offmesh_teleport_01_start
offmesh_teleport_01_end
```

---

## Link Types

**`jump`** — The agent runs to the edge, arcs through the air, and lands at the end point. The arc height and duration are calculated automatically from the distance between the two markers. The agent cannot be knocked off course mid-jump.

**`teleport`** — The agent instantly moves from start to end. Use for portals, trap doors, or any connection where a visible arc does not make sense.

---

## What the Outliner Looks Like

```
Outliner
├── Platform_A
├── Platform_B
├── Wall_01
├── Ceiling
├── offmesh_jump_01_start     ← at the ledge of Platform_A
├── offmesh_jump_01_end       ← landing spot on Platform_B
├── offmesh_teleport_01_start ← portal entrance
└── offmesh_teleport_01_end   ← portal exit
```

---

## Adjusting Links

| What you want | How to do it |
|---------------|--------------|
| Move the take-off point | Select the `_start` cube, move it (`G`) |
| Move the landing point | Select the `_end` cube, move it (`G`) |
| Add another link | Add two cubes, name them with a new ID (`_02_`, `_03_`, …) |
| Remove a link | Delete both cubes |
| Change link type | Rename both cubes (e.g. `_jump_` → `_teleport_`) |
| Temporarily disable | Add `DISABLED_` prefix to both names |

---

## Placement Tips

- **Keep markers at floor level.** The center of the cube is used as the agent's foot position. If the cube floats 0.5m above the floor, the agent will try to start or land in mid-air.
- **Keep markers small.** The size of the cube does not matter for gameplay, but a small cube (`0.1` scale) is easier to position precisely and less visually distracting.
- **Place the start marker at the very edge.** The agent will walk to this point before beginning the jump or teleport. If it is set back from the edge, the agent will stop short and the jump will look wrong.
- **Place the end marker on solid floor.** The landing point must be on walkable geometry. If the NavMesh does not cover that spot, the agent will not be able to continue moving after landing.
- **Bidirectional jumps need two separate link pairs.** A `_01_` pair only sends agents from `start` to `end`. To allow travel in both directions, add a `_02_` pair with start and end swapped.

---

## Common Mistakes

**Mismatched IDs — link silently does not exist.**
```
offmesh_jump_01_start   ← ID is "01"
offmesh_jump_1_end      ← ID is "1" — these will NOT pair
```
The game will not crash, but the link will be ignored. Check the console log on startup — missing pairs are reported there.

**Wrong suffix — both named `_start` or both named `_end`.**
Both sides must exist. If either is missing, the link is ignored.

**Marker not at floor level.**
The agent targets the cube's center. A cube sitting on top of a surface is fine — but a cube floating in the air will cause the agent to jump to or from a point above the floor.

**Overlapping markers from different links.**
If two `_start` markers are placed at the same spot, both links are still registered — but agents may behave unexpectedly. Keep link markers clearly separated.

---

## Quick Reference

```
Minimum setup for one jump link:
  offmesh_jump_01_start   (cube at take-off edge, floor level)
  offmesh_jump_01_end     (cube at landing spot, floor level)

Minimum setup for a teleport:
  offmesh_teleport_01_start   (cube at entry)
  offmesh_teleport_01_end     (cube at exit)

To disable without deleting:
  DISABLED_offmesh_jump_01_start
  DISABLED_offmesh_jump_01_end
```
