# Agent Collision and Push Behavior Guide

## Overview

This guide explains how agent-agent collisions work in the Spatial physics system and how to implement push mechanics for skills, explosions, and other gameplay features.

## Default Behavior: Agents Block Each Other

By default, when two agents (Player, NPC, or Enemy) collide with each other:

1. **They block each other** - Like running into a wall, they stop movement
2. **No pushing forces** - Physics engine doesn't push them around
3. **Avoidance kicks in** - Local avoidance system steers them around each other
4. **Path replanning** - If blocked for too long, agents replan their path

This prevents agents from pushing each other off the ground or applying unwanted forces.

### Technical Details

Agent-agent collisions use special material properties:
- **MaximumRecoveryVelocity = 0**: No recovery/separation forces (no pushing!)
- **SpringSettings frequency = 120 Hz**: Very stiff contact (like hitting a wall)
- **FrictionCoefficient = 0.05**: Very low friction (easy to slide past)

All other collisions (agent-terrain, agent-obstacle) use normal physics properties.

## Explicitly Pushable Agents

For special cases where you want agents to be pushed (skills, explosions, ragdoll), you can mark them as **pushable**.

### Making an Agent Pushable

```csharp
// Method 1: Using PhysicsWorld directly
physicsWorld.SetEntityPushable(entityId, true);

// Method 2: Using the entity reference
var entity = physicsWorld.EntityRegistry.GetEntityById(entityId);
physicsWorld.SetEntityPushable(entity, true);

// To disable pushable
physicsWorld.SetEntityPushable(entityId, false);
```

When an agent is marked as pushable:
- Other agents can push it with physics forces
- It can still push back if it's also an agent
- All collision forces apply normally

### Use Cases for Pushable Agents

1. **Knockback Skills**: Enemy hit by a knockback ability
2. **Explosions**: Units caught in explosion radius
3. **Ragdoll State**: Dead/stunned units that can be pushed around
4. **Special Units**: Units designed to be lighter/pushable by default

## Push Methods

The `MovementController` provides two methods for applying push forces:

### 1. Knockback (Airborne)

Use for hits, abilities, or effects that should launch the target into the air:

```csharp
// Knockback target away from attacker
var direction = targetPos - attackerPos;
movementController.Knockback(targetEntityId, direction, force: 8.0f);
```

**Effects:**
- Applies impulse in specified direction
- Forces entity into AIRBORNE state
- Pauses pathfinding until entity lands
- Gravity takes over (entity falls)

### 2. Push (Grounded)

Use for pushing without launching into air:

```csharp
// Push entity in a direction
movementController.Push(
    entityId: targetId,
    direction: pushDirection,
    force: 5.0f,
    makePushable: true,      // Optional: makes entity pushable by others
    pushableDuration: 1.0f   // Optional: how long to stay pushable
);
```

**Effects:**
- Applies impulse in specified direction
- Doesn't change grounded state
- Optionally makes entity pushable temporarily
- Pathfinding continues (if grounded)

## Implementing a Push Skill

Here's an example of implementing an area-of-effect push skill:

```csharp
public void ExplosionPush(Vector3 explosionCenter, float radius, float force)
{
    // Find all entities in radius
    var affectedEntities = physicsWorld.GetEntitiesInRadius(
        explosionCenter, 
        radius, 
        EntityType.NPC  // Or null for all types
    );
    
    foreach (var entity in affectedEntities)
    {
        // Skip static entities
        if (entity.IsStatic)
            continue;
        
        // Calculate push direction (away from explosion)
        var entityPos = physicsWorld.GetEntityPosition(entity);
        var pushDirection = entityPos - explosionCenter;
        
        // Apply push force (stronger when closer)
        var distance = pushDirection.Length();
        var falloff = 1.0f - (distance / radius); // Linear falloff
        var actualForce = force * falloff;
        
        // Make entity pushable and apply push
        physicsWorld.SetEntityPushable(entity, true);
        movementController.Push(
            entity.EntityId, 
            pushDirection, 
            actualForce,
            makePushable: true,
            pushableDuration: 0.5f
        );
        
        // Later: Disable pushable after duration
        // (You'll need to implement a timer system for this)
    }
}
```

## Collision Event Handling

You can listen to collision events to implement custom push logic:

```csharp
var physicsWorld = new PhysicsWorld(
    onCollision: (collisionEvent) =>
    {
        // Check if this is a special collision that should push
        if (ShouldPush(collisionEvent.EntityA, collisionEvent.EntityB))
        {
            // Calculate push direction from contact normal
            var pushDirection = collisionEvent.ContactNormal;
            var pushForce = 5.0f;
            
            // Apply push to entity B
            physicsWorld.ApplyLinearImpulse(
                collisionEvent.EntityB, 
                pushDirection * pushForce
            );
        }
    }
);
```

## Avoidance System Integration

The local avoidance system automatically handles agent-agent proximity:

1. **Detection**: When agents are within 5 units of each other
2. **Steering**: Calculates avoidance velocity to steer around
3. **Replanning**: If blocked for too long, finds new path

### How Blocking Triggers Avoidance

```
1. Agent A moves toward waypoint
2. Agent B is in the way (blocking)
3. Agent A stops (blocking behavior)
4. Local avoidance detects Agent B nearby
5. Avoidance calculates steering force
6. Agent A steers around Agent B
7. If still blocked, Agent A replans path
```

This creates natural crowd behavior without explicit pushing.

## Configuration

You can tune the avoidance behavior in `PathfindingConfiguration`:

```csharp
var config = new PathfindingConfiguration
{
    EnableLocalAvoidance = true,        // Enable avoidance system
    LocalAvoidanceRadius = 5.0f,        // Detection radius
    MaxAvoidanceNeighbors = 5,          // Max agents to avoid
    TryLocalAvoidanceFirst = true,      // Try avoidance before replan
    PathValidationInterval = 0.5f,      // Check for blocking every 0.5s
    ReplanCooldown = 1.0f              // Min time between replans
};
```

## Best Practices

### DO:
✅ Use `Knockback()` for combat abilities that launch enemies  
✅ Use `Push()` for directional force without airborne  
✅ Mark entities as pushable only when needed (skills, effects)  
✅ Rely on avoidance for normal agent-agent navigation  
✅ Implement timer systems to auto-disable pushable state  

### DON'T:
❌ Make all agents pushable by default (defeats the purpose)  
❌ Use physics forces for normal pathfinding (use velocity instead)  
❌ Forget to disable pushable state after skill duration  
❌ Apply excessive forces (agents can fall through ground)  
❌ Disable local avoidance (it's essential for smooth movement)  

## Troubleshooting

### Agents are still pushing each other
- Check if entities are marked as pushable
- Verify entity types are Player/NPC/Enemy
- Ensure entities are registered with correct EntityType

### Agents are getting stuck
- Increase local avoidance radius
- Reduce path validation interval for faster replanning
- Check if collision geometry is blocking paths

### Push forces are too weak/strong
- Tune force values (typical range: 3-10 for push, 5-20 for knockback)
- Check entity mass (heavier entities need more force)
- Verify impulse is being applied (check console output)

## Example: Complete Push Skill System

```csharp
public class PushSkillSystem
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly MovementController _movementController;
    private readonly Dictionary<int, float> _pushableTimers = new();
    
    public void ApplyPushSkill(int casterId, Vector3 direction, float range, float force)
    {
        var casterEntity = _physicsWorld.EntityRegistry.GetEntityById(casterId);
        var casterPos = _physicsWorld.GetEntityPosition(casterEntity);
        
        // Find targets in cone
        var targets = FindTargetsInCone(casterPos, direction, range, angle: 45f);
        
        foreach (var target in targets)
        {
            // Make target pushable
            _physicsWorld.SetEntityPushable(target, true);
            _pushableTimers[target.EntityId] = 0.5f; // 0.5s duration
            
            // Apply push
            _movementController.Push(
                target.EntityId,
                direction,
                force,
                makePushable: true,
                pushableDuration: 0.5f
            );
        }
    }
    
    public void Update(float deltaTime)
    {
        // Update pushable timers
        var expiredTimers = new List<int>();
        
        foreach (var kvp in _pushableTimers)
        {
            _pushableTimers[kvp.Key] -= deltaTime;
            
            if (_pushableTimers[kvp.Key] <= 0)
            {
                // Timer expired, disable pushable
                _physicsWorld.SetEntityPushable(kvp.Key, false);
                expiredTimers.Add(kvp.Key);
            }
        }
        
        // Clean up expired timers
        foreach (var entityId in expiredTimers)
        {
            _pushableTimers.Remove(entityId);
        }
    }
    
    private List<PhysicsEntity> FindTargetsInCone(
        Vector3 origin, 
        Vector3 direction, 
        float range, 
        float angle)
    {
        var targets = new List<PhysicsEntity>();
        var entities = _physicsWorld.GetEntitiesInRadius(origin, range);
        
        foreach (var entity in entities)
        {
            if (entity.IsStatic)
                continue;
            
            var entityPos = _physicsWorld.GetEntityPosition(entity);
            var toEntity = Vector3.Normalize(entityPos - origin);
            var dot = Vector3.Dot(direction, toEntity);
            var angleToEntity = MathF.Acos(dot) * (180f / MathF.PI);
            
            if (angleToEntity <= angle)
            {
                targets.Add(entity);
            }
        }
        
        return targets;
    }
}
```

## Summary

The collision system provides:
- **Default blocking**: Agents stop when they collide (no pushing)
- **Avoidance system**: Automatically steers agents around each other
- **Explicit pushing**: Mark entities as pushable for skills/effects
- **Push methods**: Knockback and Push for different use cases
- **Flexible control**: Full control over when and how pushing occurs

This gives you the best of both worlds: stable navigation by default, with explicit push mechanics when you need them.
