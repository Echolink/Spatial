# Character Positioning Fix - Feet on Ground

## Problem
Units were appearing half-submerged in the ground in Unity visualization, with their bodies partially underground while moving.

## Root Cause
The physics engine positions entities by their **center point**, while pathfinding/navmesh works with **ground surface positions** (where feet should be).

When spawning an agent at a navmesh position without offsetting for the capsule height, the capsule center is placed at ground level, causing half the character to be underground.

## Solution

### Correct Spawn Position Calculation

When spawning a character, always add the capsule half-height offset:

```csharp
// Agent configuration
var agentConfig = new AgentConfig
{
    Height = 1.8f,    // Cylinder length (body)
    Radius = 0.5f     // Capsule radius (includes caps)
};

// Calculate capsule half-height
// Total height = Height + 2*Radius (includes hemispherical caps)
// Half-height = (Height/2) + Radius
float capsuleHalfHeight = (agentConfig.Height / 2.0f) + agentConfig.Radius;
// Example: (1.8/2) + 0.5 = 0.9 + 0.5 = 1.4m

// navmeshY represents the ground surface (where feet should be)
var spawnPosition = new Vector3(
    x,
    navmeshY + capsuleHalfHeight,  // Offset up so feet are at surface
    z
);

// Now register entity with physics
var agent = physicsWorld.RegisterEntityWithInertia(
    position: spawnPosition,  // ← Capsule center position
    // ... other params
);
```

### Why This Works

1. **NavMesh/Pathfinding**: Returns Y positions representing walkable surfaces (ground level)
2. **Physics Engine**: Positions entities by their center point
3. **Capsule Geometry**: 
   - Total height = CylinderHeight + 2×Radius (includes hemispherical caps)
   - Half-height = (CylinderHeight/2) + Radius
4. **Correct Positioning**: CapsuleCenter.Y = GroundSurface.Y + HalfHeight

### Visual Representation

```
Correct Positioning:          Incorrect (Bug):
                              
    ╭─────╮                       ╭─────╮
    │     │  ← Center             │     │
    │     │    Y = Ground + 1.4   ├─────┤ ← Ground (Y = 0)
    │     │                       │     │  ← Center at ground
    ╰─────╯  ← Feet               │     │     causes half to be
Ground ▓▓▓▓▓  ← Y = 0            ╰─────╯     underground!
      ▓▓▓▓▓                   Underground ▓▓▓▓▓
```

## Unity Visualization

The `EntityVisualizer.cs` in Unity correctly handles the visualization offset:

```csharp
// Line 441-450
float yOffset = 0;
if (state.ShapeType == "Capsule" && state.Size.Length >= 2)
{
    float capsuleHeight = state.Size[1];  // Total height from physics
    yOffset = -capsuleHeight * 0.5f;      // Offset down by half
}

Vector3 targetPos = new Vector3(
    -state.Position[0],
    state.Position[1] + yOffset,  // Apply feet-pivot offset
    state.Position[2]
);
```

This ensures that even though physics sends the capsule CENTER position, Unity displays it so the feet are on the ground.

## Files Fixed

- ✅ `Spatial.TestHarness/TestMotorVsVelocity.cs` - Fixed agent spawn position
- ✅ `Spatial.TestHarness/TestEnhancedShowcase.cs` - Already correct
- ✅ `Unity/Scripts/EntityVisualizer.cs` - Visualization offset already correct

## Key Principle

**Always remember**: 
- Navmesh/Pathfinding Y = Ground surface (where feet touch)
- Physics Entity Y = Capsule center point
- **Conversion**: PhysicsY = NavmeshY + CapsuleHalfHeight

## Testing Checklist

When testing character positioning:

1. ✅ Character feet should be ON the ground surface, not inside it
2. ✅ Character should not float above the ground
3. ✅ Character capsule center should be at: Ground Y + HalfHeight
4. ✅ In Unity, visual should match expected ground position
5. ✅ Pathfinding waypoints represent ground positions (feet level)
6. ✅ Movement controller adds half-height offset when targeting waypoints

## Example Calculation

For standard humanoid character:
- Cylinder Height: 1.8m (body)
- Radius: 0.5m (including head/feet caps)
- Total Capsule Height: 1.8 + 2×0.5 = 2.8m
- Half-Height: 1.4m
- Ground at Y=7.81m
- **Physics spawn position: Y = 7.81 + 1.4 = 9.21m** ✅
- Capsule bottom (feet): 9.21 - 1.4 = 7.81m (on ground) ✅
