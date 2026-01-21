# Agent Collision Behavior - Implementation Changes

## Summary

Fixed agent-agent collision behavior to block instead of push, while adding support for explicit push mechanics (skills, explosions, etc.).

## Problem

Agents were pushing each other off the ground with strong forces when they collided. The user wanted:
- Agents to block each other (like hitting a wall) instead of pushing
- Avoidance logic to handle agent-agent proximity
- Push forces only when explicitly defined (skills, explosions, or pushable entities)

## Solution

Modified the collision system to distinguish between agent-agent collisions and other collisions, applying different material properties for each case.

## Changes Made

### 1. Modified `CollisionHandler.cs`

**Location**: `Spatial.Physics/CollisionHandler.cs`

**Changes**:
- Added entity resolution logic to detect agent-agent collisions
- Added different material properties for agent-agent vs normal collisions
- Agent-agent collisions use:
  - `MaximumRecoveryVelocity = 0` (no pushing forces!)
  - `SpringSettings(120f, 1f)` (very stiff, like hitting a wall)
  - `FrictionCoefficient = 0.05f` (very low for easy sliding/avoidance)

**Key Methods Added**:
```csharp
private PhysicsEntity? ResolveEntity(CollidableReference collidable)
private bool IsAgent(PhysicsEntity? entity)
```

### 2. Extended `PhysicsEntity.cs`

**Location**: `Spatial.Physics/PhysicsEntity.cs`

**Changes**:
- Added `IsPushable` property (default: false)
- When true, agent can be pushed by other agents
- When false, agent blocks instead of pushing

**New Property**:
```csharp
public bool IsPushable { get; set; } = false;
```

### 3. Extended `PhysicsWorld.cs`

**Location**: `Spatial.Physics/PhysicsWorld.cs`

**Changes**:
- Added helper methods to set entity pushable state

**New Methods**:
```csharp
public void SetEntityPushable(PhysicsEntity entity, bool isPushable)
public bool SetEntityPushable(int entityId, bool isPushable)
```

### 4. Extended `MovementController.cs`

**Location**: `Spatial.Integration/MovementController.cs`

**Changes**:
- Added `Push()` method for applying directional pushes (skills, abilities)
- Enhanced `Knockback()` documentation
- Push method supports temporary pushable state

**New Method**:
```csharp
public void Push(int entityId, Vector3 direction, float force, bool makePushable = false, float pushableDuration = 1.0f)
```

### 5. Created `AGENT_COLLISION_GUIDE.md`

**Location**: `Spatial/AGENT_COLLISION_GUIDE.md`

**Contents**:
- Comprehensive guide on agent collision behavior
- How to use push mechanics
- Examples for implementing push skills
- Best practices and troubleshooting

### 6. Created `TestAgentCollision.cs`

**Location**: `Spatial.TestHarness/TestAgentCollision.cs`

**Contents**:
- Test demonstrating agent blocking behavior
- Test for push mechanics
- Test for knockback mechanics
- Test for pushable flag behavior

### 7. Updated `Program.cs`

**Location**: `Spatial.TestHarness/Program.cs`

**Changes**:
- Added command-line option for agent collision test
- `dotnet run -- agent-collision` to run the test

## How It Works

### Default Behavior (Blocking)

When two agents collide:
1. Physics engine detects collision
2. `CollisionHandler` checks if both are agents
3. If both are agents and neither is pushable:
   - Apply very stiff spring settings (120 Hz)
   - Set recovery velocity to 0 (no pushing)
   - Set low friction (easy to slide past)
4. Result: Agents stop when they touch (blocking)

### Explicit Push Behavior

To push an agent:
1. Mark entity as pushable: `physicsWorld.SetEntityPushable(entityId, true)`
2. Apply impulse: `movementController.Push(entityId, direction, force)`
3. Agent can now be pushed by physics forces
4. Optionally disable pushable after duration

### Avoidance Integration

The local avoidance system automatically handles blocked agents:
1. Agent detects nearby agents (within 5 units)
2. Calculates steering force to avoid
3. If still blocked, replans path

## Testing

Run the agent collision test:
```bash
cd Spatial.TestHarness
dotnet run -- agent-collision
```

This test demonstrates:
- Two agents moving toward each other
- Agents block when they meet (don't push)
- Push mechanic applied to one agent
- Knockback mechanic launching agent into air
- Pushable flag being toggled

## API Usage Examples

### Basic Blocking (Default)
```csharp
// Agents block automatically - no code needed!
// Just spawn agents and they'll block each other
var agent1 = entityManager.SpawnEntity(new SpawnEntityCommand { EntityType = EntityType.NPC, ... });
var agent2 = entityManager.SpawnEntity(new SpawnEntityCommand { EntityType = EntityType.NPC, ... });
// When they collide, they'll block instead of push
```

### Push Skill
```csharp
// Apply a directional push (e.g., from a skill)
var pushDirection = targetPos - casterPos;
movementController.Push(
    targetEntityId, 
    pushDirection, 
    force: 10.0f,
    makePushable: true,      // Temporarily make pushable
    pushableDuration: 0.5f   // For 0.5 seconds
);
```

### Knockback Ability
```csharp
// Knock enemy backward (launches into air)
var knockbackDir = enemyPos - attackerPos;
movementController.Knockback(
    enemyEntityId,
    knockbackDir,
    force: 15.0f
);
```

### Explosion Push
```csharp
// Push all entities in radius
var entities = physicsWorld.GetEntitiesInRadius(explosionCenter, radius);
foreach (var entity in entities)
{
    var pushDir = entityPos - explosionCenter;
    var distance = pushDir.Length();
    var falloff = 1.0f - (distance / radius);
    var actualForce = explosionForce * falloff;
    
    physicsWorld.SetEntityPushable(entity, true);
    movementController.Push(entity.EntityId, pushDir, actualForce);
}
```

### Make Entity Permanently Pushable
```csharp
// For special units that should always be pushable
var entity = entityManager.SpawnEntity(...);
physicsWorld.SetEntityPushable(entity.EntityId, true);
```

## Configuration

No configuration needed! The behavior is automatic:
- **Agent-Agent**: Block by default
- **Agent-Terrain**: Normal physics
- **Agent-Obstacle**: Normal physics
- **Pushable Agent-Agent**: Normal physics (when one is pushable)

## Benefits

1. **Stable Navigation**: Agents don't push each other off the ground
2. **Predictable Behavior**: Blocking is deterministic and stable
3. **Explicit Control**: Push only when you want it
4. **Natural Crowds**: Avoidance system creates realistic crowd behavior
5. **Combat Ready**: Easy to implement knockback, pushes, explosions

## Backward Compatibility

✅ Fully backward compatible!
- Existing code continues to work
- Default behavior prevents pushing (better than before)
- New push mechanics are opt-in via new methods

## Performance Impact

Minimal:
- One additional boolean check per collision
- No performance overhead for normal collisions
- Blocking is actually more efficient than pushing

## Future Enhancements

Potential future improvements:
1. Auto-disable pushable after duration (timer system)
2. Per-entity push resistance values
3. Mass-based push strength modifiers
4. Collision damage based on impact force

## References

- **Guide**: `AGENT_COLLISION_GUIDE.md` - Complete usage guide
- **Test**: `TestAgentCollision.cs` - Demonstration and validation
- **Physics**: `CollisionHandler.cs` - Core collision logic
- **Entity**: `PhysicsEntity.cs` - Pushable property
- **Movement**: `MovementController.cs` - Push/Knockback methods
