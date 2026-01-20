# Coordinate System Fix: NavMesh X-Axis Flip Issue

## Problem Description

When exporting the navmesh to OBJ and importing it into Unity, the navmesh aligned correctly with the `separated_land.obj`. However, when the server sent the navmesh data directly to the Unity client at runtime via WebSocket, the navmesh appeared flipped on the x-axis.

## Root Cause

### Coordinate System Differences

1. **Server/Physics System**: Uses a **right-handed coordinate system**
   - This is standard for many 3D math libraries and physics engines
   - X points right, Y points up, Z points forward

2. **Unity**: Uses a **left-handed coordinate system**
   - X points right, Y points up, Z points forward
   - BUT the coordinate system is left-handed (different handedness)

### Why OBJ Export Worked

When Unity imports OBJ files, it **automatically converts** from right-handed to left-handed coordinate systems by:
1. Negating one axis (typically X or Z)
2. Reversing triangle winding order

This automatic conversion made the exported OBJ file align correctly.

### Why Network Data Didn't Work

When sending navmesh data directly via WebSocket:
- Data was sent as raw float arrays
- No automatic coordinate conversion occurred
- Unity received right-handed coordinates in a left-handed system
- Result: X-axis appeared flipped

## Solution

### Applied Coordinate Transformation in Unity Scripts

I modified three Unity scripts to apply coordinate system conversion:

#### 1. NavMeshVisualizer.cs

**Vertex Conversion:**
```csharp
// Before:
vertices[i] = new Vector3(v[0], v[1], v[2]);

// After:
vertices[i] = new Vector3(-v[0], v[1], v[2]); // Negate X-axis
```

**Triangle Winding Order:**
```csharp
// Reverse winding order when flipping axis
for (int i = 0; i < navMesh.Indices.Count; i += 3)
{
    triangles[i] = navMesh.Indices[i];
    triangles[i + 1] = navMesh.Indices[i + 2]; // Swap
    triangles[i + 2] = navMesh.Indices[i + 1];
}
```

**Path Waypoints:**
```csharp
// Before:
pathLine.SetPosition(i, new Vector3(wp[0], wp[1], wp[2]));

// After:
pathLine.SetPosition(i, new Vector3(-wp[0], wp[1], wp[2])); // Negate X-axis
```

#### 2. EntityVisualizer.cs

**Entity Positions:**
```csharp
// Before:
entityObj.transform.position = new Vector3(
    state.Position[0],
    state.Position[1] + yOffset,
    state.Position[2]
);

// After:
entityObj.transform.position = new Vector3(
    -state.Position[0],  // Negate X-axis
    state.Position[1] + yOffset,
    state.Position[2]
);
```

**Entity Mesh Vertices:**
```csharp
// Before:
vertices[i] = new Vector3(v[0], v[1], v[2]);

// After:
vertices[i] = new Vector3(-v[0], v[1], v[2]); // Negate X-axis
```

**Velocity Vectors:**
```csharp
// Before:
Vector3 velocity = new Vector3(state.Velocity[0], state.Velocity[1], state.Velocity[2]);

// After:
Vector3 velocity = new Vector3(-state.Velocity[0], state.Velocity[1], state.Velocity[2]);
```

### Why This Works

1. **Negating X-axis**: Converts from right-handed to left-handed coordinate system
2. **Reversing winding order**: Maintains correct normal directions after axis flip
3. **Consistent transformation**: Applied to all geometric data (vertices, positions, velocities)

## Testing the Fix

### Before Fix
- ❌ Exported OBJ navmesh aligned correctly in Unity
- ❌ Network-sent navmesh appeared flipped on X-axis
- ❌ Entities appeared on wrong side of navmesh
- ❌ Paths were mirrored

### After Fix
- ✅ Exported OBJ navmesh aligns correctly in Unity
- ✅ Network-sent navmesh aligns correctly in Unity
- ✅ Both navmeshes are in the same position
- ✅ Entities and paths appear in correct positions

## How to Verify

1. Export navmesh using:
   ```bash
   dotnet run --project Spatial.TestHarness -- showcase --export-navmesh
   ```

2. Import the exported OBJ into Unity

3. Run the server and connect Unity client:
   ```bash
   dotnet run --project Spatial.TestHarness -- showcase
   ```

4. Verify that:
   - The runtime navmesh (green transparent) aligns with the imported OBJ
   - Entities spawn on the navmesh correctly
   - Paths follow the navmesh surface
   - No X-axis mirroring occurs

## Alternative Solutions Considered

### Option 1: Convert on Server Side
**Rejected because:**
- Would require changing physics coordinate system
- BepuPhysics expects standard right-handed coordinates
- Would affect all physics calculations
- More invasive change

### Option 2: Change OBJ Export to Match Network
**Rejected because:**
- OBJ format convention is right-handed
- Unity's OBJ importer would still convert it
- Doesn't solve the underlying issue
- Less standard approach

### Option 3: Convert in Unity (Chosen)
**Accepted because:**
- Minimal changes required
- Follows Unity conventions
- Physics system remains standard
- Clear separation of concerns
- Matches Unity's OBJ importer behavior

## Technical Details

### Coordinate System Handedness

**Right-handed (Server):**
```
      Y (up)
      |
      |
      +---- X (right)
     /
    Z (forward)
```

**Left-handed (Unity):**
```
      Y (up)
      |
      |
      +---- X (right)
     /
    Z (forward)
```

The difference is subtle but critical: In a right-handed system, if you curl your right hand fingers from X to Z, your thumb points up (Y). In a left-handed system, you use your left hand.

### Triangle Winding Order

**Right-handed (Counter-clockwise front faces):**
```
Triangle: [0, 1, 2]
Looking from front: 0 -> 1 -> 2 (CCW)
```

**After X-axis flip (must reverse to maintain CCW):**
```
Triangle: [0, 2, 1]  
Looking from front: 0 -> 2 -> 1 (CCW)
```

When we negate X, vertices appear in reverse order when viewed from the same direction, so we must swap the winding to maintain the correct normal direction.

## Related Files

Modified files:
- `Unity/Scripts/NavMeshVisualizer.cs` - NavMesh and path visualization
- `Unity/Scripts/EntityVisualizer.cs` - Entity position and mesh visualization

Reference documentation:
- `COORDINATE_SYSTEM_GUIDE.md` - Physics vs Visual coordinate systems
- `Unity/README.md` - Unity integration documentation

## Future Considerations

If you add more visualization features that receive coordinates from the server, remember to:

1. **Negate X-axis** for all position/vertex data
2. **Negate X-component** of direction/velocity vectors
3. **Reverse triangle winding** when flipping axis on mesh data
4. **Test alignment** with OBJ-imported geometry

## Summary

The fix converts server coordinates (right-handed) to Unity coordinates (left-handed) by negating the X-axis for all geometric data and reversing triangle winding order. This matches Unity's OBJ importer behavior and ensures runtime data aligns with imported assets.

---

**Fixed Date:** 2026-01-20  
**Issue:** NavMesh X-axis flip between OBJ import and network data  
**Solution:** Coordinate system transformation in Unity visualization scripts
