# Ramp Climbing Fix - NavMesh Surface Query

## Problem
After the CHARACTER_POSITIONING_FIX update, units had better positioning on flat ground but experienced severe vertical snapping when climbing ramps. The unit would:
- Work well on ground level (good surface positioning)
- Get stuck inside the ramp mesh when climbing (not on top)
- Experience constant vertical snapping/jittering
- Repeatedly snap to the same Y position without progressing

### Root Cause
The grounding system was **interpolating Y position between sparse navmesh waypoints** instead of querying the actual surface beneath the agent.

```
Example ramp scenario:
- Waypoint 1: Ground level (Y=7.0)
- Waypoint 2: Ramp top (Y=10.0)
- Agent position: Middle of ramp (XZ progress = 50%)

OLD BEHAVIOR (Interpolation):
- Interpolated Y = 7.0 + (10.0 - 7.0) × 0.5 = 8.5
- But actual ramp surface at this XZ might be Y=8.2
- Result: Agent pushed above/below surface → snapping

NEW BEHAVIOR (Direct Query):
- Query navmesh at agent's current XZ position
- Get actual surface Y = 8.2
- Result: Agent stays on surface smoothly
```

## Solution

### Code Changes
Modified `MovementController.cs` to query the navmesh at the agent's current XZ position instead of interpolating:

**Before (Interpolation):**
```csharp
// Calculate progress between waypoints
float progress = coveredHorizontalDist / totalHorizontalDist;

// Interpolate Y between sparse waypoints
float interpolatedGroundY = prevPos.Y + (targetWaypoint.Y - prevPos.Y) * progress;
float targetY = interpolatedGroundY + agentHalfHeight;

_characterController.ApplyGroundingForce(entity, desiredVelocity, targetY, agentHalfHeight);
```

**After (Direct Query):**
```csharp
// Query navmesh at agent's current XZ position
var currentXZ = new Vector3(currentPosition.X, currentPosition.Y, currentPosition.Z);
var smallSearchExtents = new Vector3(1.0f, 2.0f, 1.0f);
var surfaceAtCurrentPos = _pathfindingService.FindNearestValidPosition(currentXZ, smallSearchExtents);

float targetY;
if (surfaceAtCurrentPos != null)
{
    // Use actual navmesh surface Y at this XZ position
    targetY = surfaceAtCurrentPos.Value.Y + agentHalfHeight;
}
else
{
    // Fallback: interpolate if query fails
    // (original interpolation code as backup)
}

_characterController.ApplyGroundingForce(entity, desiredVelocity, targetY, agentHalfHeight);
```

### Why This Works

1. **NavMesh is Continuous**: The navmesh surface covers the entire ramp, not just start/end points
2. **Accurate Height**: Querying at current XZ gives the exact surface Y at that location
3. **No Interpolation Error**: Avoids mismatch between interpolated Y and actual surface
4. **Smooth Climbing**: Agent follows the actual ramp surface continuously

### Performance Considerations

- **Query Cost**: Navmesh queries happen every frame while moving
- **Optimization**: Small search extents (1m horizontal, 2m vertical) keep queries fast
- **Fallback**: Original interpolation serves as backup if query fails
- **Trade-off**: Extra queries are worth it for smooth climbing behavior

## Areas Fixed

1. **Grounded Movement** (lines 653-686):
   - Query navmesh at current position
   - Apply grounding with accurate surface Y
   - Smooth climbing on ramps and slopes

2. **Recovery State** (lines 687-735):
   - Same query approach while recovering from airborne
   - Prevents sinking during landing stabilization

## Testing Checklist

When testing ramp climbing:

- ✅ Unit stays ON TOP of ramp surface (not inside)
- ✅ No vertical snapping or jittering while climbing
- ✅ Smooth upward progression on ramps
- ✅ Ground level positioning still works correctly
- ✅ Agent reaches target at top of ramp successfully
- ✅ Multi-level climbing scenarios work (e.g., Agent-3: Y=-2.17m → Y=7.83m)

## Files Modified

- `Spatial.Integration/MovementController.cs` - Updated grounding logic in two places:
  - Grounded movement state (lines ~653-686)
  - Recovering state (lines ~687-735)

## Related Documents

- `CHARACTER_POSITIONING_FIX.md` - Original fix for ground-level positioning
- `SMOOTH_MOVEMENT_FIX.md` - Overall smoothness improvements
- `HEIGHT_SNAPPING_FIX.md` - Height correction tuning
- `MOVEMENT_SMOOTHNESS_OPTIMIZATION.md` - **IMPORTANT**: Performance optimization to prevent stuttering

## Key Principle

**Always query the actual surface beneath the agent, don't interpolate between distant waypoints.**

Waypoints represent checkpoints along the path, not surface samples. The navmesh itself is the authoritative source for surface heights.
