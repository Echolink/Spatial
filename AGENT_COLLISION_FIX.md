# Agent Collision Issues - Fixed

## Issues Identified

### 1. Agents Still Push Each Other
**Root Cause**: Spring settings and friction values were not aggressive enough to prevent pushing.

**Fix Applied**:
- Increased spring frequency from 120Hz to 240Hz (extra stiff contact)
- Reduced friction from 0.05f to 0.0f (zero friction for effortless sliding)
- These changes make agent-agent collisions behave like hitting a wall

### 2. Agents Stop Far Apart
**Root Cause**: Speculative margin was set too high (0.5f), causing collision detection to trigger when agents were 0.5 units apart.

**Fixes Applied**:
- `CollisionHandler.cs`: Reduced speculative margin from 0.5f to 0.1f
- `PhysicsWorld.cs`: Reduced body speculative margin from 0.5f to 0.1f

**Result**: Agents now get much closer before collision response activates (approximately touching distance with capsule radius 0.5f = 1.0 unit separation).

### 3. Agents Sink Into Ground
**Root Cause**: Agents were spawned with incorrect Y positioning.

**Problem**: For a capsule with:
- Radius = 0.5f
- Length (height parameter) = 1.8f
- Total height = length + 2*radius = 1.8 + 1.0 = 2.8f
- Bottom offset from center = length/2 + radius = 0.9 + 0.5 = 1.4f

If ground surface is at Y=0 (ground box center at -0.05f, thickness 0.1f, so top at 0.0f):
- Correct capsule center Y = 0 + 1.4 = **1.4f**
- Previous (incorrect) Y = **1.0f** → bottom at -0.4f (below ground!)

**Fix Applied**:
- `TestAgentCollision.cs`: Changed spawn position from Y=1.0f to Y=1.4f
- Updated position validation check from 1.0f to 1.4f

## Files Modified

### 1. `Spatial.Physics/CollisionHandler.cs`
**Lines 63-68**: Reduced speculative margin
```csharp
// Changed from 0.5f to 0.1f
speculativeMargin = Math.Max(speculativeMargin, 0.1f);
```

**Lines 106-112**: Increased blocking stiffness
```csharp
pairMaterialProperties = new PairMaterialProperties
{
    FrictionCoefficient = 0.0f,        // Changed from 0.05f
    MaximumRecoveryVelocity = 0f,      // No change
    SpringSettings = new SpringSettings(240f, 1f)  // Changed from 120f
};
```

### 2. `Spatial.Physics/PhysicsWorld.cs`
**Line 185**: Reduced speculative margin
```csharp
// Changed from 0.5f to 0.1f
Collidable = new CollidableDescription(shape, 0.1f),
```

### 3. `Spatial.TestHarness/TestAgentCollision.cs`
**Lines 88-103**: Fixed agent spawn positions
```csharp
// FIXED: Position agents correctly above the ground
// Capsule: radius=0.5f, length=1.8f
// Bottom of capsule = center - (length/2 + radius) = center - 1.4f
// Ground top surface is at Y=0
// For agent to stand on ground: center = 0 + 1.4 = 1.4f
var agent1Cmd = new SpawnEntityCommand
{
    EntityType = EntityType.NPC,
    Position = new Vector3(-5, 1.4f, 0),  // Changed from 1.0f
    ...
};
```

**Lines 199-205**: Updated validation check
```csharp
// Expected Y position is 1.4f (standing on ground at Y=0)
if (Math.Abs(finalPos1.Y - 1.4f) < 0.5f && Math.Abs(finalPos2.Y - 1.4f) < 0.5f)
```

## How to Test

Run the agent collision test:
```bash
cd Spatial.TestHarness
dotnet run -- agent-collision
```

## Expected Behavior

1. **Agents should NOT push each other**
   - When agents collide, they should stop moving (block)
   - No upward forces should launch them off the ground
   - They should remain stable at their collision point

2. **Agents should stop at approximately 1.0 unit apart**
   - With capsule radius 0.5f for each agent
   - Final distance should be 0.9-1.1 units (sum of radii ±10%)

3. **Agents should stand on the ground**
   - Capsule center at Y=1.4f
   - Bottom of capsule at Y=0.0f (ground surface)
   - No sinking below ground

## Understanding Capsule Positioning

For BepuPhysics capsules:

```
Total Height = Length + 2×Radius
Bottom Offset = (Length/2) + Radius
Center Y = Ground Y + Bottom Offset

Example (radius=0.5, length=1.8):
Total Height = 1.8 + 2×0.5 = 2.8
Bottom Offset = (1.8/2) + 0.5 = 1.4
If ground at Y=0, center at Y=1.4
```

## Speculative Margin Explanation

**Speculative margin** is the distance at which BepuPhysics starts generating collision contacts *before* actual penetration occurs. This prevents tunneling at high speeds.

- **Too high (0.5f)**: Agents stop 0.5 units apart (looks like they're not touching)
- **Optimal (0.1f)**: Agents stop approximately at surface contact
- **Too low (<0.05f)**: Risk of tunneling through surfaces at high speeds

## Physics Material Properties

For **agent-agent blocking**:
- **Spring Frequency (240Hz)**: Extra stiff contact response
- **Damping Ratio (1.0)**: Critically damped (no bouncing)
- **Max Recovery Velocity (0f)**: No pushing forces
- **Friction (0f)**: Zero friction for effortless sliding/avoidance

For **normal collisions** (agent-world):
- **Spring Frequency (30Hz)**: Standard stiffness
- **Damping Ratio (1.0)**: Critically damped
- **Max Recovery Velocity (2f)**: Standard penetration resolution
- **Friction (0.1f)**: Low for smooth movement

## Troubleshooting

### If agents still push each other:
- Verify `IsPushable` is false (default) for both agents
- Check that collision handler detects both as agents (EntityType)
- Ensure spring frequency is 240Hz or higher

### If agents stop too far apart:
- Check speculative margin in both files
- Verify collision detection is active (collision callback triggered)

### If agents sink into ground:
- Calculate correct Y position: Ground Y + (CapsuleLength/2 + CapsuleRadius)
- Verify ground surface position
- Check that ground has collision enabled

## Related Documentation

- `AGENT_COLLISION_GUIDE.md` - Complete guide to agent collision behavior
- `AGENT_COLLISION_SUMMARY.md` - High-level overview
- `TestAgentCollision.cs` - Demonstration test
