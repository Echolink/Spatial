using Spatial.Physics;

namespace Spatial.Integration;

/// <summary>
/// Filters and routes collision events from the physics system to the game server.
/// Provides type-specific collision handlers and event deduplication.
/// 
/// Key features:
/// - Subscribe to physics collision callbacks
/// - Filter collisions by type (Player-Enemy, Unit-Obstacle, etc.)
/// - Rate-limit collision events (don't spam same collision)
/// - Provide rich collision context
/// </summary>
public class CollisionEventSystem
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly Dictionary<(int, int), DateTime> _lastCollisionTime = new();
    private readonly Dictionary<(EntityType, EntityType), List<Action<CollisionEvent>>> _typeHandlers = new();
    private readonly float _collisionCooldown = 0.5f; // Minimum time between same collision events
    
    /// <summary>
    /// Event fired when a player hits an enemy
    /// </summary>
    public event Action<CollisionEvent>? OnPlayerHitEnemy;
    
    /// <summary>
    /// Event fired when a unit hits an obstacle
    /// </summary>
    public event Action<CollisionEvent>? OnUnitHitObstacle;
    
    /// <summary>
    /// Event fired when a projectile hits a target
    /// </summary>
    public event Action<CollisionEvent>? OnProjectileHitTarget;
    
    /// <summary>
    /// Event fired for any collision (after filtering)
    /// </summary>
    public event Action<CollisionEvent>? OnAnyCollision;
    
    public CollisionEventSystem(PhysicsWorld physicsWorld)
    {
        _physicsWorld = physicsWorld;
        
        // Subscribe to physics collision events
        // Note: This requires enhancing CollisionHandler to support callbacks
        // For now, this is a placeholder that will be connected once CollisionHandler is enhanced
    }
    
    /// <summary>
    /// Registers a custom handler for specific entity type pairs.
    /// </summary>
    public void RegisterHandler(EntityType typeA, EntityType typeB, Action<CollisionEvent> handler)
    {
        var key = GetOrderedTypePair(typeA, typeB);
        
        if (!_typeHandlers.ContainsKey(key))
        {
            _typeHandlers[key] = new List<Action<CollisionEvent>>();
        }
        
        _typeHandlers[key].Add(handler);
    }
    
    /// <summary>
    /// Handles a collision event from the physics system.
    /// This should be called by the CollisionHandler when collisions occur.
    /// </summary>
    public void HandleCollision(CollisionEvent collision)
    {
        // Check for cooldown to prevent spam
        var pairKey = GetOrderedEntityPair(collision.EntityA.EntityId, collision.EntityB.EntityId);
        var now = DateTime.UtcNow;
        
        if (_lastCollisionTime.TryGetValue(pairKey, out var lastTime))
        {
            var elapsed = (now - lastTime).TotalSeconds;
            if (elapsed < _collisionCooldown)
            {
                // Too soon since last collision event for this pair
                return;
            }
        }
        
        // Update last collision time
        _lastCollisionTime[pairKey] = now;
        
        // Get entity types
        var typeA = collision.EntityA.EntityType;
        var typeB = collision.EntityB.EntityType;
        
        Console.WriteLine($"[CollisionEventSystem] Collision: {typeA} (ID {collision.EntityA.EntityId}) <-> {typeB} (ID {collision.EntityB.EntityId})");
        
        // Fire type-specific events
        FireTypeSpecificEvents(collision, typeA, typeB);
        
        // Fire custom handlers
        var typePairKey = GetOrderedTypePair(typeA, typeB);
        if (_typeHandlers.TryGetValue(typePairKey, out var handlers))
        {
            foreach (var handler in handlers)
            {
                handler(collision);
            }
        }
        
        // Fire generic event
        OnAnyCollision?.Invoke(collision);
    }
    
    /// <summary>
    /// Fires type-specific collision events.
    /// </summary>
    private void FireTypeSpecificEvents(CollisionEvent collision, EntityType typeA, EntityType typeB)
    {
        // Player-Enemy collisions
        if ((typeA == EntityType.Player && typeB == EntityType.Enemy) ||
            (typeA == EntityType.Enemy && typeB == EntityType.Player))
        {
            OnPlayerHitEnemy?.Invoke(collision);
        }
        
        // Unit-Obstacle collisions
        if (IsUnit(typeA) && IsObstacle(typeB))
        {
            OnUnitHitObstacle?.Invoke(collision);
        }
        else if (IsObstacle(typeA) && IsUnit(typeB))
        {
            // Swap so obstacle is always EntityB
            var swapped = new CollisionEvent(
                collision.EntityB,
                collision.EntityA,
                collision.ContactPoint,
                -collision.Normal,
                collision.PenetrationDepth
            );
            OnUnitHitObstacle?.Invoke(swapped);
        }
        
        // Projectile collisions
        if (typeA == EntityType.Projectile || typeB == EntityType.Projectile)
        {
            OnProjectileHitTarget?.Invoke(collision);
        }
    }
    
    /// <summary>
    /// Checks if an entity type is a unit (Player, NPC, Enemy).
    /// </summary>
    private bool IsUnit(EntityType type)
    {
        return type == EntityType.Player || 
               type == EntityType.NPC || 
               type == EntityType.Enemy;
    }
    
    /// <summary>
    /// Checks if an entity type is an obstacle.
    /// </summary>
    private bool IsObstacle(EntityType type)
    {
        return type == EntityType.Obstacle || 
               type == EntityType.TemporaryObstacle || 
               type == EntityType.StaticObject;
    }
    
    /// <summary>
    /// Gets an ordered pair of entity IDs (smaller first).
    /// </summary>
    private (int, int) GetOrderedEntityPair(int idA, int idB)
    {
        return idA < idB ? (idA, idB) : (idB, idA);
    }
    
    /// <summary>
    /// Gets an ordered pair of entity types.
    /// </summary>
    private (EntityType, EntityType) GetOrderedTypePair(EntityType typeA, EntityType typeB)
    {
        return (int)typeA < (int)typeB ? (typeA, typeB) : (typeB, typeA);
    }
    
    /// <summary>
    /// Cleans up old collision cooldown entries.
    /// Call this periodically to prevent memory growth.
    /// </summary>
    public void CleanupOldCollisions()
    {
        var now = DateTime.UtcNow;
        var toRemove = new List<(int, int)>();
        
        foreach (var kvp in _lastCollisionTime)
        {
            var elapsed = (now - kvp.Value).TotalSeconds;
            if (elapsed > 10.0) // Remove entries older than 10 seconds
            {
                toRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in toRemove)
        {
            _lastCollisionTime.Remove(key);
        }
    }
}
