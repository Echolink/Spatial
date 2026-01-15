# Game Server Integration Guide

This guide shows game server developers how to integrate the Spatial physics and pathfinding system into their game server.

## Table of Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [Core Systems](#core-systems)
4. [Entity Management](#entity-management)
5. [Movement and Pathfinding](#movement-and-pathfinding)
6. [Collision Handling](#collision-handling)
7. [Configuration](#configuration)
8. [Complete Example](#complete-example)
9. [Best Practices](#best-practices)

---

## Overview

The Spatial integration system provides:

- **Physics simulation** via BepuPhysics v2
- **Pathfinding** via DotRecast
- **Entity lifecycle management** with automatic cleanup
- **Event-driven architecture** for loose coupling
- **Local avoidance** for dynamic obstacles
- **Automatic path validation** and replanning

### Architecture

```
Game Server
    ↓ Commands (Spawn, Move, Despawn)
EntityManager → PhysicsWorld
    ↓                ↓
MovementController ← Pathfinder
    ↓
CollisionEventSystem
    ↓ Events (Collisions, Movement, etc.)
Game Server
```

---

## Quick Start

### 1. Initialize Core Systems

```csharp
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Integration;
using Spatial.Integration.Commands;
using System.Numerics;

public class GameServer
{
    private PhysicsWorld _physicsWorld;
    private Pathfinder _pathfinder;
    private EntityManager _entityManager;
    private MovementController _movementController;
    private CollisionEventSystem _collisionSystem;
    
    public void Initialize()
    {
        // 1. Create physics world
        _physicsWorld = new PhysicsWorld(new PhysicsConfiguration 
        {
            Gravity = new Vector3(0, -9.81f, 0),
            Timestep = 0.016f // 60 FPS
        });
        
        // 2. Build navmesh from your level geometry
        var navMeshData = BuildNavMesh(); // See section below
        _pathfinder = new Pathfinder(navMeshData);
        
        // 3. Create integration systems
        _entityManager = new EntityManager(_physicsWorld);
        
        var config = new PathfindingConfiguration
        {
            PathValidationInterval = 0.5f,
            EnableLocalAvoidance = true,
            EnableAutomaticReplanning = true
        };
        _movementController = new MovementController(_physicsWorld, _pathfinder, config);
        
        _collisionSystem = new CollisionEventSystem(_physicsWorld);
        
        // 4. Subscribe to events
        SetupEventHandlers();
    }
    
    private void SetupEventHandlers()
    {
        // Movement events
        _movementController.OnDestinationReached += HandleDestinationReached;
        _movementController.OnPathBlocked += HandlePathBlocked;
        _movementController.OnPathReplanned += HandlePathReplanned;
        _movementController.OnMovementProgress += HandleMovementProgress;
        
        // Collision events
        _collisionSystem.OnPlayerHitEnemy += HandlePlayerEnemyCollision;
        _collisionSystem.OnUnitHitObstacle += HandleUnitObstacleCollision;
        
        // Entity lifecycle events
        _entityManager.OnEntitySpawned += HandleEntitySpawned;
        _entityManager.OnEntityDespawned += HandleEntityDespawned;
    }
}
```

### 2. Build NavMesh

```csharp
using Spatial.Integration;

private NavMeshData BuildNavMesh()
{
    // Option 1: Build from static geometry in physics world
    var staticObstacles = GetStaticGeometry(); // Your level geometry
    var builder = new NavMeshBuilder(_physicsWorld);
    
    var bounds = new Vector3(-50, -10, -50); // Min bounds
    var boundsMax = new Vector3(50, 10, 50);  // Max bounds
    
    return builder.BuildNavMesh(
        bounds, 
        boundsMax,
        cellSize: 0.3f,
        cellHeight: 0.2f,
        agentHeight: 2.0f,
        agentRadius: 0.6f,
        agentMaxClimb: 0.9f
    );
    
    // Option 2: Load pre-built navmesh
    // return NavMeshData.Load("path/to/navmesh.bin");
}
```

### 3. Game Loop

```csharp
public void Update(float deltaTime)
{
    // Update systems in order
    _movementController.UpdateMovement(deltaTime);
    _physicsWorld.Update(deltaTime);
    _entityManager.Update(deltaTime); // Cleanup temporary obstacles
    
    // Optional: Periodic cleanup
    if (Time.frameCount % 600 == 0) // Every 10 seconds at 60 FPS
    {
        _collisionSystem.CleanupOldCollisions();
    }
}
```

---

## Core Systems

### PhysicsWorld

Manages all physics simulation using BepuPhysics v2.

**Key Methods:**
- `RegisterEntityWithInertia()` - Add physics body
- `UnregisterEntity()` - Remove physics body
- `GetEntityPosition()` / `SetEntityPosition()`
- `GetEntityVelocity()` / `SetEntityVelocity()`
- `GetEntitiesInRadius()` - Spatial queries

### Pathfinder

Finds paths through the navmesh using DotRecast.

**Key Methods:**
- `FindPath(start, end, extents)` - Compute path
- Returns `PathResult` with waypoints and success status

### EntityManager

Centralized entity lifecycle management.

**Key Methods:**
- `SpawnEntity(command)` - Create entity
- `SpawnTemporaryObstacle(pos, duration, size)` - Temporary blockage
- `DespawnEntity(id)` - Remove entity
- `GetEntitiesOfType(type)` - Query by type

### MovementController

Orchestrates movement with pathfinding, validation, and avoidance.

**Key Methods:**
- `RequestMovement(request)` - Start moving entity
- `StopMovement(entityId)` - Stop entity
- `UpdateMovement(deltaTime)` - Update all moving entities

### CollisionEventSystem

Filters and routes collision events.

**Key Methods:**
- `RegisterHandler(typeA, typeB, handler)` - Custom collision handlers
- `HandleCollision(collision)` - Process collision
- `CleanupOldCollisions()` - Periodic maintenance

---

## Entity Management

### Spawning Entities

```csharp
// Spawn a player character
var playerCommand = new SpawnEntityCommand
{
    EntityType = EntityType.Player,
    Position = new Vector3(0, 1, 0),
    ShapeType = ShapeType.Capsule,
    Size = new Vector3(0.5f, 1.8f, 0), // radius, height
    Mass = 70.0f,
    IsStatic = false
};
var playerHandle = _entityManager.SpawnEntity(playerCommand);
Console.WriteLine($"Spawned player with ID: {playerHandle.EntityId}");

// Spawn multiple NPCs
for (int i = 0; i < 10; i++)
{
    var npcCommand = new SpawnEntityCommand
    {
        EntityType = EntityType.NPC,
        Position = new Vector3(i * 2, 1, 0),
        ShapeType = ShapeType.Capsule,
        Size = new Vector3(0.5f, 1.8f, 0),
        Mass = 70.0f,
        IsStatic = false
    };
    _entityManager.SpawnEntity(npcCommand);
}

// Spawn static obstacle
var wallCommand = new SpawnEntityCommand
{
    EntityType = EntityType.StaticObject,
    Position = new Vector3(10, 2, 0),
    ShapeType = ShapeType.Box,
    Size = new Vector3(5, 4, 1), // width, height, depth
    IsStatic = true
};
_entityManager.SpawnEntity(wallCommand);
```

### Temporary Obstacles

Perfect for area-of-effect spells, traps, or temporary blockages:

```csharp
// Spawn wall of fire that lasts 5 seconds
var fireWallHandle = _entityManager.SpawnTemporaryObstacle(
    position: new Vector3(5, 1, 5),
    duration: 5.0f,
    size: new Vector3(3, 2, 0.5f)
);

// After 5 seconds, it automatically despawns
// OnEntityDespawned event will fire
```

### Despawning

```csharp
// Manual despawn
_entityManager.DespawnEntity(playerHandle.EntityId);

// Query and despawn all enemies
var enemies = _entityManager.GetEntitiesOfType(EntityType.Enemy);
foreach (var enemy in enemies)
{
    _entityManager.DespawnEntity(enemy.EntityId);
}
```

---

## Collision and Local Avoidance Behavior

### What Happens During Collisions?

When entities collide, the system handles it through multiple layers:

1. **Physics Response** - BepuPhysics handles the physical collision:
   - Entities bounce or stop based on their mass and velocity
   - Collision forces are automatically applied
   - Physics constraints prevent interpenetration

2. **Collision Events** - The `CollisionEventSystem` detects and routes collision events:
   - Fires type-specific events (e.g., `OnPlayerHitEnemy`, `OnUnitHitObstacle`)
   - Includes collision details: contact point, normal, velocity
   - Rate-limited to prevent event spam

3. **Movement Response** - The `MovementController` reacts to blocked paths:
   - Detects when path is obstructed
   - Fires `OnPathBlocked` event
   - Automatically replans if `EnableAutomaticReplanning = true`

### Local Avoidance System

The local avoidance system uses **steering behaviors** to prevent collisions before they happen:

**How It Works:**
1. **Neighbor Detection** - Each moving entity scans for nearby entities within `LocalAvoidanceRadius`
2. **Separation Forces** - Calculates repulsion forces from entities within `SeparationRadius`
3. **Steering Calculation** - Combines avoidance forces with path-following forces
4. **Velocity Adjustment** - Applies steering to smoothly avoid obstacles while maintaining progress toward goal

**Configuration Parameters:**
- `EnableLocalAvoidance` - Toggle the entire system (default: true)
- `LocalAvoidanceRadius` - How far to scan for obstacles (default: 5.0f units)
- `MaxAvoidanceNeighbors` - Max entities to consider (default: 5, higher = more CPU)
- `AvoidanceStrength` - Force multiplier (default: 2.0f, higher = more aggressive avoidance)
- `SeparationRadius` - Personal space bubble (default: 2.0f units)
- `TryLocalAvoidanceFirst` - Use avoidance before replanning (default: true)

**When Local Avoidance Works Best:**
- Dynamic moving obstacles (other units)
- Temporary obstacles that will clear soon
- Minor path obstructions

**When Replanning Is Triggered:**
- Avoidance cannot find a clear path
- Path is completely blocked by static obstacles
- Target becomes unreachable

**Example: Unit-to-Unit Collision Sequence:**
1. Unit A detects Unit B within `LocalAvoidanceRadius` (e.g., 5 units away)
2. Steering force applied to both units to maintain `SeparationRadius` (e.g., 2 units)
3. Units smoothly curve around each other while continuing toward goals
4. If units still get too close, physics collision occurs
5. `CollisionEventSystem` fires appropriate event
6. Units continue moving after collision resolves

**Example: Unit-to-Obstacle Collision Sequence:**
1. Unit approaches static obstacle (e.g., wall)
2. Local avoidance tries to steer around it within `LocalAvoidanceRadius`
3. If obstacle blocks entire path, `OnPathBlocked` event fires
4. If `EnableAutomaticReplanning = true`, new path is calculated
5. If no alternative path exists, unit stops and remains blocked
6. If unit physically hits obstacle, `OnUnitHitObstacle` event fires

---

## Movement and Pathfinding

### Basic Movement

```csharp
// Move entity to target position
var request = new MovementRequest(
    entityId: playerHandle.EntityId,
    targetPosition: new Vector3(20, 0, 15),
    maxSpeed: 5.0f
);

bool started = _movementController.RequestMovement(request);
if (started)
{
    Console.WriteLine("Movement started!");
}
else
{
    Console.WriteLine("Failed to start movement (no path found)");
}
```

### Movement Events

```csharp
private void HandleDestinationReached(int entityId, Vector3 position)
{
    Console.WriteLine($"Entity {entityId} reached destination at {position}");
    
    // Give quest reward, trigger next AI action, etc.
    if (IsQuestTarget(entityId, position))
    {
        CompleteQuest(entityId);
    }
}

private void HandlePathBlocked(int entityId)
{
    Console.WriteLine($"Entity {entityId} path is blocked!");
    
    // Notify player, find alternative route, etc.
    if (IsPlayerControlled(entityId))
    {
        SendMessageToPlayer(entityId, "Path blocked - finding alternative route");
    }
}

private void HandlePathReplanned(int entityId)
{
    Console.WriteLine($"Entity {entityId} replanned path");
    
    // Update client, recalculate ETA, etc.
}

private void HandleMovementProgress(int entityId, float percentComplete)
{
    // Update progress bar, trigger waypoint events, etc.
    if (percentComplete >= 0.5f && !_halfwayTriggered[entityId])
    {
        TriggerHalfwayEvent(entityId);
        _halfwayTriggered[entityId] = true;
    }
}
```

### Stopping Movement

```csharp
// Stop entity manually (e.g., player clicked stop button)
_movementController.StopMovement(playerHandle.EntityId);
```

---

## Collision Handling

### Built-in Collision Events

```csharp
private void HandlePlayerEnemyCollision(CollisionEvent collision)
{
    // Determine which is player and which is enemy
    var player = collision.EntityA.EntityType == EntityType.Player 
        ? collision.EntityA 
        : collision.EntityB;
    var enemy = collision.EntityA.EntityType == EntityType.Enemy 
        ? collision.EntityA 
        : collision.EntityB;
    
    // Apply damage
    ApplyDamage(player.EntityId, 10);
    
    // Apply knockback using collision normal
    var knockbackForce = collision.Normal * 5.0f;
    _physicsWorld.ApplyLinearImpulse(player, knockbackForce);
    
    // Play effects
    PlayHitEffect(collision.ContactPoint);
}

private void HandleUnitObstacleCollision(CollisionEvent collision)
{
    // EntityA is always the unit, EntityB is the obstacle
    var unit = collision.EntityA;
    var obstacle = collision.EntityB;
    
    Console.WriteLine($"Unit {unit.EntityId} hit obstacle {obstacle.EntityId}");
    
    // Check if obstacle is destructible
    if (IsDestructible(obstacle))
    {
        ApplyDamageToObstacle(obstacle.EntityId, 5);
    }
}
```

### Custom Collision Handlers

```csharp
// Register custom handler for projectile-player collisions
_collisionSystem.RegisterHandler(
    EntityType.Projectile, 
    EntityType.Player,
    collision =>
    {
        var projectile = collision.EntityA.EntityType == EntityType.Projectile 
            ? collision.EntityA 
            : collision.EntityB;
        var player = collision.EntityA.EntityType == EntityType.Player 
            ? collision.EntityA 
            : collision.EntityB;
        
        // Apply damage
        var damage = GetProjectileDamage(projectile.EntityId);
        ApplyDamage(player.EntityId, damage);
        
        // Destroy projectile
        _entityManager.DespawnEntity(projectile.EntityId);
        
        // Play effects
        SpawnExplosionEffect(collision.ContactPoint);
    }
);
```

---

## Configuration

### PathfindingConfiguration Options

```csharp
var config = new PathfindingConfiguration
{
    // Path validation
    PathValidationInterval = 0.5f,        // Check path every 0.5 seconds
    EnableAutomaticReplanning = true,     // Auto-replan when blocked
    ReplanCooldown = 1.0f,                // Min 1 second between replans
    
    // Local avoidance
    EnableLocalAvoidance = true,          // Enable steering behavior
    LocalAvoidanceRadius = 5.0f,          // Consider entities within 5 units
    MaxAvoidanceNeighbors = 5,            // Max 5 entities for avoidance
    AvoidanceStrength = 2.0f,             // Steering force multiplier
    SeparationRadius = 2.0f,              // Repel entities closer than 2 units
    TryLocalAvoidanceFirst = true,        // Try avoidance before replanning
    
    // Movement thresholds
    WaypointReachedThreshold = 0.5f,      // Distance to consider waypoint reached
    DestinationReachedThreshold = 0.3f    // Distance to consider destination reached
};

var movementController = new MovementController(_physicsWorld, _pathfinder, config);
```

### PhysicsConfiguration Options

```csharp
var physicsConfig = new PhysicsConfiguration
{
    Gravity = new Vector3(0, -9.81f, 0),  // Earth gravity
    Timestep = 0.016f                      // 60 FPS (1/60)
};

var physicsWorld = new PhysicsWorld(physicsConfig);
```

### Unity Client Rendering Delay

When testing with Unity Editor visualization, add a delay before spawning entities to allow Unity to fully load and render:

```csharp
public class GameServer
{
    // Set to 3-5 seconds for Unity Editor (slower loading)
    // Set to 0 for production or when not using Unity visualization
    private float _unityClientRenderDelay = 3.0f;
    
    private void SpawnTestWorld()
    {
        // Wait for Unity client to finish rendering
        if (_unityClientRenderDelay > 0)
        {
            Console.WriteLine($"[Server] Waiting {_unityClientRenderDelay}s for Unity client...");
            Thread.Sleep((int)(_unityClientRenderDelay * 1000));
        }
        
        // Now spawn entities...
    }
}
```

**Recommended Values:**
- Unity Editor: 3-5 seconds (editor has slower initialization)
- Unity Build: 1-2 seconds (faster loading)
- Production (no visualization): 0 seconds (no delay needed)

---

## Complete Example

Here's a complete game server implementation:

```csharp
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Integration;
using Spatial.Integration.Commands;
using System.Numerics;

public class CompleteGameServer
{
    private PhysicsWorld _physicsWorld;
    private Pathfinder _pathfinder;
    private EntityManager _entityManager;
    private MovementController _movementController;
    private CollisionEventSystem _collisionSystem;
    
    private Dictionary<int, PlayerData> _players = new();
    private bool _running = true;
    
    // Configuration: Delay for Unity client rendering (set to 0 to disable)
    private float _unityClientRenderDelay = 3.0f; // 3-5 seconds recommended for Unity Editor
    
    public void Run()
    {
        Initialize();
        
        // Spawn test entities
        SpawnTestWorld();
        
        // Main game loop
        float deltaTime = 0.016f; // 60 FPS
        while (_running)
        {
            Update(deltaTime);
            Thread.Sleep(16); // Roughly 60 FPS
        }
        
        Cleanup();
    }
    
    private void Initialize()
    {
        Console.WriteLine("[Server] Initializing game server...");
        
        // Physics
        _physicsWorld = new PhysicsWorld(new PhysicsConfiguration 
        {
            Gravity = new Vector3(0, -9.81f, 0),
            Timestep = 0.016f
        });
        
        // Pathfinding
        var navMeshData = BuildNavMesh();
        _pathfinder = new Pathfinder(navMeshData);
        
        // Integration systems
        _entityManager = new EntityManager(_physicsWorld);
        
        // Configure movement with local avoidance enabled
        var pathfindingConfig = new PathfindingConfiguration
        {
            PathValidationInterval = 0.5f,
            EnableLocalAvoidance = true,          // Enable steering avoidance
            LocalAvoidanceRadius = 5.0f,          // Check entities within 5 units
            MaxAvoidanceNeighbors = 5,            // Consider up to 5 neighbors
            AvoidanceStrength = 2.0f,             // Steering force multiplier
            SeparationRadius = 2.0f,              // Maintain 2 unit separation
            TryLocalAvoidanceFirst = true,        // Use avoidance before replanning
            EnableAutomaticReplanning = true,
            ReplanCooldown = 1.0f
        };
        _movementController = new MovementController(_physicsWorld, _pathfinder, pathfindingConfig);
        _collisionSystem = new CollisionEventSystem(_physicsWorld);
        
        // Event handlers
        _movementController.OnDestinationReached += (id, pos) =>
            Console.WriteLine($"[Server] Entity {id} reached destination");
        
        _movementController.OnPathBlocked += (id) =>
            Console.WriteLine($"[Server] Entity {id} path blocked!");
        
        _movementController.OnPathReplanned += (id) =>
            Console.WriteLine($"[Server] Entity {id} replanned path");
        
        _collisionSystem.OnPlayerHitEnemy += HandlePlayerEnemyCollision;
        
        _entityManager.OnEntitySpawned += (id) =>
            Console.WriteLine($"[Server] Entity {id} spawned");
        
        _entityManager.OnEntityDespawned += (id) =>
            Console.WriteLine($"[Server] Entity {id} despawned");
        
        Console.WriteLine("[Server] Initialization complete!");
    }
    
    private NavMeshData BuildNavMesh()
    {
        // Build navmesh for a simple test arena
        var builder = new NavMeshBuilder(_physicsWorld);
        
        // Create ground plane
        var groundCommand = new SpawnEntityCommand
        {
            EntityType = EntityType.StaticObject,
            Position = new Vector3(0, -0.5f, 0),
            ShapeType = ShapeType.Box,
            Size = new Vector3(50, 1, 50),
            IsStatic = true
        };
        _entityManager.SpawnEntity(groundCommand);
        
        // Build navmesh
        return builder.BuildNavMesh(
            new Vector3(-25, -1, -25),
            new Vector3(25, 5, 25),
            cellSize: 0.3f,
            cellHeight: 0.2f,
            agentHeight: 2.0f,
            agentRadius: 0.6f,
            agentMaxClimb: 0.9f
        );
    }
    
    private void SpawnTestWorld()
    {
        Console.WriteLine("[Server] Spawning test world...");
        
        // Wait for Unity client to finish rendering before spawning units
        if (_unityClientRenderDelay > 0)
        {
            Console.WriteLine($"[Server] Waiting {_unityClientRenderDelay} seconds for Unity client to render...");
            Thread.Sleep((int)(_unityClientRenderDelay * 1000));
        }
        
        // Spawn 5 NPCs on the left side, moving to the right
        for (int i = 0; i < 5; i++)
        {
            var npc = new SpawnEntityCommand
            {
                EntityType = EntityType.NPC,
                Position = new Vector3(-15, 1, i * 3 - 6),  // Start from left side
                ShapeType = ShapeType.Capsule,
                Size = new Vector3(0.5f, 1.8f, 0),
                Mass = 70.0f
            };
            var handle = _entityManager.SpawnEntity(npc);
            
            // Make all units move through the center to the right side
            var targetPos = new Vector3(
                15,                           // Move to right side
                0,
                i * 2 - 4                     // Stagger targets
            );
            
            _movementController.RequestMovement(new MovementRequest(
                handle.EntityId,
                targetPos,
                3.0f
            ));
        }
        
        // Spawn a temporary wall obstacle that blocks the center path after 3 seconds
        // This forces all units to encounter it and demonstrate avoidance/replanning
        Task.Delay(3000).ContinueWith(_ =>
        {
            Console.WriteLine("[Server] Spawning temporary wall obstacle blocking center path!");
            _entityManager.SpawnTemporaryObstacle(
                new Vector3(0, 1, 0),          // Center of the map
                5.0f,                           // Lasts 5 seconds
                new Vector3(1, 2, 15)          // Wall: thin (1) but tall (2) and wide (15) to block all paths
            );
        });
    }
    
    private void Update(float deltaTime)
    {
        _movementController.UpdateMovement(deltaTime);
        _physicsWorld.Update(deltaTime);
        _entityManager.Update(deltaTime);
    }
    
    private void HandlePlayerEnemyCollision(CollisionEvent collision)
    {
        Console.WriteLine("[Server] Player-Enemy collision detected!");
        // Apply damage, knockback, etc.
    }
    
    private void Cleanup()
    {
        _physicsWorld.Dispose();
    }
}

// Player data structure
public class PlayerData
{
    public int EntityId { get; set; }
    public string Name { get; set; }
    public int Health { get; set; }
}
```

---

## Best Practices

### 1. Entity Lifecycle

- Always use `EntityManager` for spawning/despawning
- Subscribe to lifecycle events for cleanup
- Use temporary obstacles for time-limited effects

### 2. Movement

- Let `MovementController` handle path validation and replanning
- Subscribe to events rather than polling entity positions
- Configure thresholds based on your game's scale

### 3. Collisions

- Use type-specific handlers to avoid checking entity types in every collision
- Apply rate limiting for frequent collisions (already built-in)
- Use collision normals for knockback/physics responses

### 4. Performance

- Adjust `PathValidationInterval` based on your needs (higher = less CPU)
- Limit `MaxAvoidanceNeighbors` for better performance
- Use `CleanupOldCollisions()` periodically
- Consider disabling local avoidance if not needed

### 5. Configuration

- Start with defaults and tune based on testing
- Higher `ReplanCooldown` prevents excessive replanning
- Adjust thresholds based on entity size and movement speed

### 6. Debugging

- Enable console output to trace entity lifecycle
- Monitor event frequencies
- Use Unity visualization (see Unity integration guide)

---

## Common Patterns

### Patrol Behavior

```csharp
private void SetupPatrol(int entityId, List<Vector3> waypoints)
{
    int currentWaypoint = 0;
    
    _movementController.OnDestinationReached += (id, pos) =>
    {
        if (id == entityId)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Count;
            var request = new MovementRequest(
                entityId,
                waypoints[currentWaypoint],
                3.0f
            );
            _movementController.RequestMovement(request);
        }
    };
}
```

### Chase Behavior

```csharp
private void ChaseTarget(int chaserId, int targetId)
{
    // Update chase every second
    var timer = new Timer(_ =>
    {
        var target = _entityManager.GetEntityById(targetId);
        if (target != null)
        {
            var targetPos = _physicsWorld.GetEntityPosition(target);
            _movementController.RequestMovement(new MovementRequest(
                chaserId,
                targetPos,
                5.0f
            ));
        }
    }, null, 0, 1000);
}
```

### Area Damage

```csharp
private void ApplyAreaDamage(Vector3 center, float radius, float damage)
{
    var entities = _physicsWorld.GetEntitiesInRadius(center, radius);
    
    foreach (var entity in entities)
    {
        if (entity.EntityType == EntityType.Player || 
            entity.EntityType == EntityType.Enemy)
        {
            ApplyDamage(entity.EntityId, damage);
            
            // Knockback from center
            var pos = _physicsWorld.GetEntityPosition(entity);
            var direction = Vector3.Normalize(pos - center);
            _physicsWorld.ApplyLinearImpulse(entity, direction * 10.0f);
        }
    }
}
```

---

## Troubleshooting

### Entity Not Moving

- Check if pathfinding succeeded (`RequestMovement` returns true)
- Verify entity has proper mass and inertia
- Ensure navmesh covers the target position
- Check console for path validation failures

### Excessive Replanning

- Increase `ReplanCooldown` in configuration
- Check if obstacles are constantly changing
- Consider using local avoidance for dynamic obstacles

### Collisions Not Firing

- Verify collision event subscriptions
- Check entity types match your handlers
- Ensure collision cooldown isn't too long

### Poor Performance

- Reduce `MaxAvoidanceNeighbors`
- Increase `PathValidationInterval`
- Disable local avoidance if not needed
- Profile spatial queries

---

## Next Steps

- See `MultiUnitTest.cs` for working examples
- Check `Unity/README.md` for client visualization
- Read `IMPLEMENTATION_SUMMARY.md` for architecture details
- Explore `Spatial.Integration` source code for advanced usage
