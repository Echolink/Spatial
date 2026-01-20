# Coordinate System Guide: Physics vs Visual Representation

## Overview

The Spatial project uses **two different coordinate system conventions** for agent positioning:
1. **Physics System (BepuPhysics)**: Uses **center-pivot** for capsule positioning
2. **Visual System (Unity)**: Uses **feet-pivot** for character visualization

This guide explains how these systems interact and how positioning is handled correctly.

---

## Physics System: Center-Pivot (BepuPhysics)

### Convention
- **Position = Center of capsule**
- This is standard for physics engines
- Allows for symmetric calculations and rotations

### Example
For a **2m tall capsule** standing on ground at **Y=0**:
- **Physics Position Y = 1.0** (center of capsule)
- **Bottom of capsule = 0.0** (touching ground)
- **Top of capsule = 2.0**

### Why Center-Pivot?
- Standard physics convention
- Simplifies collision detection
- Natural for rotation calculations
- Mass center alignment

---

## Visual System: Feet-Pivot (Unity)

### Convention
- **Position = Feet/bottom of character**
- More intuitive for game developers and designers
- "Y=0" means "standing on ground level"

### Example
For a **2m tall capsule** standing on ground at **Y=0**:
- **Visual Position Y = 0.0** (feet on ground)
- **Center of visual = 1.0**
- **Top of visual = 2.0**

### Why Feet-Pivot?
- More intuitive for level design
- "Character at Y=0" means "on the ground"
- Easier to understand spawn positions
- Common in game engines

---

## Converting Between Systems

### Physics → Visual (for Unity visualization)

```csharp
// In EntityVisualizer.cs
float yOffset = 0;
if (state.ShapeType == "Capsule" && state.Size.Length >= 2)
{
    float capsuleHeight = state.Size[1];
    yOffset = -capsuleHeight * 0.5f;  // Move down by half height
}

visualPosition.y = physicsPosition.y + yOffset;
```

**Example:**
- Physics reports: Y = 1.0 (capsule center)
- Capsule height: 2.0m
- Visual position: Y = 1.0 + (-1.0) = **0.0** (feet on ground) ✓

### NavMesh → Physics (for agent positioning)

```csharp
// In MovementController.cs
float agentHalfHeight = state.AgentHeight * 0.5f;
var physicsPosition = new Vector3(
    navMeshPosition.X,
    navMeshPosition.Y + agentHalfHeight,  // Raise by half height
    navMeshPosition.Z
);
```

**Example:**
- NavMesh surface: Y = 0.0
- Agent height: 2.0m
- Physics position: Y = 0.0 + 1.0 = **1.0** (capsule center) ✓

---

## Practical Scenarios

### Scenario 1: Spawning an Agent

```csharp
// NavMesh returns spawn point at Y = -2.3 (surface level)
var navMeshSpawnY = -2.3f;
var agentHeight = 2.0f;

// Calculate physics spawn position (capsule center)
var physicsSpawnY = navMeshSpawnY + (agentHeight * 0.5f);
// Result: -2.3 + 1.0 = -1.3 (CORRECT)

// Unity will visualize this as:
var unityVisualY = physicsSpawnY - (agentHeight * 0.5f);
// Result: -1.3 - 1.0 = -2.3 (feet on navmesh surface) ✓
```

### Scenario 2: Agent Movement

During pathfinding movement:
1. **NavMesh waypoint**: Y = -2.27 (surface)
2. **MovementController sets physics Y**: -2.27 + 1.0 = **-1.27** (center)
3. **Unity visualizes at**: -1.27 - 1.0 = **-2.27** (feet on surface) ✓

### Scenario 3: Agents with Different Heights

| Agent Type | Height | NavMesh Y | Physics Y | Unity Visual Y |
|------------|--------|-----------|-----------|----------------|
| Player     | 2.0m   | 0.0       | 1.0       | 0.0            |
| NPC        | 1.8m   | 0.0       | 0.9       | 0.0            |
| Child      | 1.2m   | 0.0       | 0.6       | 0.0            |

All characters appear to stand on the ground (Y=0) in Unity, while their physics centers are at appropriate heights.

---

## Critical Implementation Details

### 1. Disable Gravity for NavMesh Agents

**Problem:** If gravity is enabled, physics will pull agents down, conflicting with navmesh positioning.

**Solution:**
```csharp
var agent = physicsWorld.RegisterEntityWithInertia(
    // ... other params ...
    disableGravity: true  // CRITICAL for navmesh agents
);
```

### 2. Always Pass Agent Height

**Problem:** Hard-coded height values (like `+0.5f`) cause incorrect positioning.

**Solution:**
```csharp
// In MovementRequest
public float AgentHeight { get; }  // Pass actual agent height

// In MovementController
float agentHalfHeight = state.AgentHeight * 0.5f;  // Calculate dynamically
```

### 3. Consistent Coordinate System

**Remember:**
- **NavMesh Y** = surface level (where agent walks)
- **Physics Y** = center of capsule
- **Unity Visual Y** = feet of character (with offset applied)

---

## Common Issues and Solutions

### Issue 1: Agents Sink Into Ground

**Symptom:** Agents gradually sink below surface during movement  
**Cause:** Gravity enabled for navmesh agents  
**Fix:** Set `disableGravity: true` when registering agents

### Issue 2: Agents Float Above Ground

**Symptom:** Agents hover above navmesh surface  
**Cause:** Incorrect Y offset calculation  
**Fix:** Ensure using `agentHeight * 0.5f`, not hard-coded values

### Issue 3: Visual Misalignment in Unity

**Symptom:** Capsules appear half-buried or floating in Unity  
**Cause:** Not applying feet-pivot offset in visualization  
**Fix:** Apply `-capsuleHeight * 0.5f` offset in `EntityVisualizer.cs`

### Issue 4: Wrong Y After Goal Reached

**Symptom:** Agents jump to different Y when movement completes  
**Cause:** Reading position after physics update, or not maintaining Y in CompleteMovement  
**Fix:** Ensure last waypoint Y is correctly set, or explicitly set Y in CompleteMovement

---

## Testing Your Implementation

### Test 1: Verify Y Coordinates

```csharp
// After spawning agent
var physicsY = physicsWorld.GetEntityPosition(agent).Y;
var expectedPhysicsY = navMeshY + (agentHeight * 0.5f);

Console.WriteLine($"Physics Y: {physicsY:F2} (expected: {expectedPhysicsY:F2})");
// Should match within 0.1
```

### Test 2: Verify No Sinking

```csharp
// Record Y at start of movement
var startY = physicsWorld.GetEntityPosition(agent).Y;

// ... movement ...

// Check Y during movement
var currentY = physicsWorld.GetEntityPosition(agent).Y;
var drift = Math.Abs(currentY - startY);

Console.WriteLine($"Y drift: {drift:F2}");
// Should be < 0.2 for stable movement
```

### Test 3: Visual Verification in Unity

1. Run simulation with visualization
2. Check that capsule bottoms align with navmesh surface
3. Verify no gradual sinking during movement
4. Confirm agents don't float above ground

---

## Architecture Decision Records

### ADR 1: Why Not Make Physics Use Feet-Pivot?

**Decision:** Keep physics center-pivot, adjust visualization only

**Rationale:**
- Physics engines are designed for center-pivot
- Changing physics pivot would affect all collisions
- BepuPhysics expects standard conventions
- Easier to adjust visualization layer only

### ADR 2: Why Disable Gravity for NavMesh Agents?

**Decision:** Disable gravity for agents using navmesh-based movement

**Rationale:**
- NavMesh provides authoritative Y positioning
- Gravity conflicts with navmesh surface snapping
- Agents should "walk on" navmesh, not "fall onto" it
- Physics collisions still work without gravity

### ADR 3: Why Pass Agent Height Dynamically?

**Decision:** Pass agent height as parameter rather than hard-coding

**Rationale:**
- Different agent types have different heights
- Hard-coded values cause bugs when heights change
- Allows NPCs, players, children to coexist
- More maintainable and flexible

---

## Quick Reference

| System | Pivot Point | Example (2m capsule on ground at Y=0) |
|--------|-------------|--------------------------------------|
| **NavMesh** | Surface | Y = 0.0 |
| **Physics** | Center | Y = 1.0 |
| **Unity Visual** | Feet | Y = 0.0 |

**Conversion Formula:**
```
Physics Y = NavMesh Y + (Height / 2)
Unity Y = Physics Y - (Height / 2)
```

---

## Further Reading

- BepuPhysics Documentation: https://github.com/bepu/bepuphysics2
- Unity Primitive Pivots: https://docs.unity3d.com/Manual/PrimitiveObjects.html
- NavMesh Surface Snapping: See `ValidateOrSnapToNavMesh()` in TestEnhancedShowcase.cs

---

**Last Updated:** 2026-01-20  
**Author:** Cursor AI Assistant  
**Related Files:**
- `Spatial.Integration/MovementController.cs`
- `Unity/Scripts/EntityVisualizer.cs`
- `Spatial.TestHarness/TestEnhancedShowcase.cs`
- `Spatial.Integration/MovementRequest.cs`
