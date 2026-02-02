# NavMesh Height Investigation

## Problem Report
Units are sinking into the ground during movement. The character's body appears half-submerged in the mesh surface.

## What I've Done

### 1. Fixed Spawn Position Bug ✅
**File**: `Spatial.TestHarness/TestMotorVsVelocity.cs`

**Problem**: The agent was being spawned without the proper capsule height offset.

**Before**:
```csharp
var startPos = new Vector3(51.89f, 0.29f, 10.19f);  // Ground surface Y=0.29
physicsWorld.RegisterEntityWithInertia(position: startPos, ...);  // ❌ Center at ground = feet underground!
```

**After**:
```csharp
float capsuleHalfHeight = (agentConfig.Height / 2.0f) + agentConfig.Radius;  // = 1.4m
var spawnPosition = new Vector3(
    startPos.X,
    startPos.Y + capsuleHalfHeight,  // ✅ Offset so feet are at ground
    startPos.Z
);
physicsWorld.RegisterEntityWithInertia(position: spawnPosition, ...);
```

### 2. Created NavMesh Accuracy Diagnostic Tool ✅
**File**: `Spatial.TestHarness/TestNavMeshAccuracy.cs`

This tool compares:
- **NavMesh surface positions** (what pathfinding reports)
- **Actual mesh surface positions** (from the OBJ file geometry)
- **Character feet positions** (physics + offset calculations)

### 3. Updated Test Harness Menu ✅
**File**: `Spatial.TestHarness/Program.cs`

Added new test option: `dotnet run -- navmesh-accuracy [meshPath]`

## How to Run the Diagnostic

```bash
cd Spatial.TestHarness
dotnet run -- navmesh-accuracy
```

This will:
1. Load `separated_land.obj` mesh
2. Generate the navmesh
3. Test specific waypoints from your simulation
4. Compare navmesh Y vs actual mesh Y at each location
5. Report any height discrepancies

## What the Diagnostic Will Tell You

For each test point, you'll see:
```
Test Point: (51.89, 0.29, 10.19)
  NavMesh surface Y:      7.130
  Actual mesh surface Y:  7.150
  Height error:           -0.020m
  ✓ Height matches within tolerance

  Character positioning:
    Capsule half-height:  1.400m
    Physics center Y:     8.530m
    Feet Y:               7.130m
    Ground Y:             7.150m
    Feet offset error:    -0.020m
    ✓ Character positioning correct
```

### Possible Outcomes

#### Scenario A: NavMesh Matches Mesh (error < 0.1m)
✅ **Height is correct**, problem is elsewhere:
- Check Unity visualization offset (EntityVisualizer.cs)
- Check coordinate system transformations
- Verify capsule visual scale vs physics size

#### Scenario B: NavMesh Height is Wrong (error > 0.1m)
❌ **NavMesh generation issue**:
- DotRecast might be interpreting the mesh incorrectly
- Voxelization parameters may need adjustment
- Agent config (MaxClimb, cell height) affects navmesh quality

#### Scenario C: Character Feet Don't Match Ground
❌ **Offset calculation issue**:
- Movement controller might not be adding proper offset
- Waypoint Y interpretation is wrong
- Half-height calculation is incorrect

## Next Steps Based on Results

### If NavMesh is Accurate
The issue is in visualization or movement execution:
1. Check `MovementController.cs` line 680: `targetY = interpolatedGroundY + agentHalfHeight`
2. Check `EntityVisualizer.cs` line 449: `yOffset = -capsuleHeight * 0.5f`
3. Verify Unity coordinate system transformation (line 456: negated X-axis)

### If NavMesh Heights are Wrong
The issue is in navmesh generation:
1. Check `NavMeshGenerator.cs` voxelization parameters
2. Increase `CellHeight` precision in AgentConfig
3. Try different `MaxClimb` values
4. Consider if the mesh itself has issues (thin faces, overlapping geometry)

### If Feet Offset is Wrong
The issue is in offset calculations:
1. Verify `capsuleHalfHeight = (Height/2) + Radius` formula
2. Check if waypoint Y represents surface or center
3. Examine how MovementController interprets waypoints

## Key Formulas to Remember

### Capsule Geometry
- **Total Height** = CylinderHeight + 2×Radius (includes hemispherical caps)
- **Half Height** = (CylinderHeight/2) + Radius
- For Height=1.8m, Radius=0.5m: HalfHeight = 0.9 + 0.5 = **1.4m**

### Position Conversions
- **Ground Surface Y** = What navmesh returns (feet position)
- **Physics Center Y** = Ground Y + HalfHeight
- **Unity Visual Y** = Physics Y - HalfHeight (to show feet on ground)

### Example
- NavMesh: Y=7.13 (surface)
- Physics: Y=7.13+1.4=8.53 (capsule center)
- Unity displays capsule at Y=8.53, but offsets visual by -1.4 so feet show at Y=7.13 ✅

## Understanding the Coordinate Flow

```
1. OBJ File (Source Mesh)
   └─> Vertex: (X, Y, Z)
        └─> Y = actual surface height

2. Physics World (BepuPhysics)
   └─> Loads mesh triangles
        └─> Creates static colliders

3. NavMesh Generation (DotRecast)
   └─> Voxelizes physics geometry
        └─> Generates walkable polygons
             └─> Returns surface Y positions

4. Pathfinding (Movement)
   └─> Waypoints from navmesh (feet positions)
        └─> Add halfHeight offset for physics
             └─> Physics centers capsule at Y+offset

5. Unity Visualization
   └─> Receives physics center Y
        └─> Subtracts halfHeight offset
             └─> Displays feet at ground level
```

## Files Modified

1. ✅ `Spatial.TestHarness/TestMotorVsVelocity.cs` - Fixed spawn position
2. ✅ `Spatial.TestHarness/TestNavMeshAccuracy.cs` - NEW diagnostic tool
3. ✅ `Spatial.TestHarness/Program.cs` - Added menu option
4. ✅ `CHARACTER_POSITIONING_FIX.md` - Documentation
5. ✅ `NAVMESH_HEIGHT_INVESTIGATION.md` - This file

## Test Again

After running the diagnostic, test the actual simulation:

```bash
dotnet run -- motor-vs-velocity --motor
```

Watch Unity visualization:
- ✅ Character should walk ON the surface
- ✅ Feet should be at ground level
- ✅ Body should be above ground
- ❌ If still sinking, check diagnostic results

## Contact Points to Check

If problem persists after diagnostics:

1. **MovementController.cs**:
   - Line 233: `agentHalfHeight` calculation
   - Line 680: `targetY = interpolatedGroundY + agentHalfHeight`
   - Line 683: `ApplyGroundingForce(entity, desiredVelocity, targetY, agentHalfHeight)`

2. **EntityVisualizer.cs**:
   - Line 246-260: Spawn position offset
   - Line 441-450: Update position offset
   - Line 455-459: Coordinate transform with offset

3. **PathfindingService.cs**:
   - Line 191-250: `FindNearestValidPosition` implementation
   - Returns Y position of navmesh surface

4. **SimulationStateBuilder.cs**:
   - Line 69: Position sent to Unity (should be physics center)
   - Line 107: Capsule size calculation

## Summary

✅ **Spawn position fixed** in TestMotorVsVelocity.cs
🔍 **Diagnostic tool created** to compare navmesh vs actual mesh
📊 **Run diagnostic** to identify where the height error originates
🔧 **Follow the coordinate flow** from mesh → navmesh → physics → Unity

The diagnostic will give us the data to pinpoint exactly where the height discrepancy occurs.
