using System.Numerics;
using Spatial.Physics;
using Spatial.Integration.Commands;

namespace Spatial.Integration;

/// <summary>
/// Centralized entity lifecycle management.
/// Handles spawning, despawning, and temporary obstacles.
/// 
/// Key features:
/// - Spawn entities with physics bodies
/// - Despawn entities and cleanup
/// - Track all active entities
/// - Handle temporary obstacles (spawn with lifetime)
/// - Communicate spawn/despawn to physics world
/// </summary>
public class EntityManager
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly Dictionary<int, PhysicsEntity> _activeEntities = new();
    private readonly Dictionary<int, TemporaryObstacleData> _temporaryObstacles = new();
    private int _nextEntityId = 1;
    
    /// <summary>
    /// Event fired when an entity is spawned
    /// </summary>
    public event Action<int>? OnEntitySpawned;
    
    /// <summary>
    /// Event fired when an entity is despawned
    /// </summary>
    public event Action<int>? OnEntityDespawned;
    
    public EntityManager(PhysicsWorld physicsWorld)
    {
        _physicsWorld = physicsWorld;
    }
    
    /// <summary>
    /// Spawns a permanent entity with the specified parameters.
    /// </summary>
    /// <returns>Handle to the spawned entity</returns>
    public EntityHandle SpawnEntity(SpawnEntityCommand command)
    {
        var entityId = _nextEntityId++;
        
        Console.WriteLine($"[EntityManager] Spawning entity {entityId} of type {command.EntityType} at ({command.Position.X:F2}, {command.Position.Y:F2}, {command.Position.Z:F2})");
        
        // Create shape based on type
        PhysicsEntity entity;
        switch (command.ShapeType)
        {
            case ShapeType.Box:
                var (boxShape, boxInertia) = _physicsWorld.CreateBoxShapeWithInertia(command.Size, command.Mass);
                entity = _physicsWorld.RegisterEntityWithInertia(
                    entityId, 
                    command.EntityType, 
                    command.Position, 
                    boxShape, 
                    boxInertia, 
                    command.IsStatic
                );
                break;
                
            case ShapeType.Capsule:
                var radius = command.Size.X;
                var length = command.Size.Y;
                var (capsuleShape, capsuleInertia) = _physicsWorld.CreateCapsuleShapeWithInertia(radius, length, command.Mass);
                entity = _physicsWorld.RegisterEntityWithInertia(
                    entityId, 
                    command.EntityType, 
                    command.Position, 
                    capsuleShape, 
                    capsuleInertia, 
                    command.IsStatic
                );
                break;
                
            case ShapeType.Sphere:
                var sphereRadius = command.Size.X;
                var (sphereShape, sphereInertia) = _physicsWorld.CreateSphereShapeWithInertia(sphereRadius, command.Mass);
                entity = _physicsWorld.RegisterEntityWithInertia(
                    entityId, 
                    command.EntityType, 
                    command.Position, 
                    sphereShape, 
                    sphereInertia, 
                    command.IsStatic
                );
                break;
                
            default:
                throw new ArgumentException($"Unknown shape type: {command.ShapeType}");
        }
        
        // Track entity
        _activeEntities[entityId] = entity;
        
        // Raise event
        OnEntitySpawned?.Invoke(entityId);
        
        return new EntityHandle(entityId, entity);
    }
    
    /// <summary>
    /// Spawns a temporary obstacle that automatically despawns after the specified duration.
    /// </summary>
    /// <param name="position">World position</param>
    /// <param name="duration">Lifetime in seconds</param>
    /// <param name="size">Size of the obstacle</param>
    /// <returns>Handle to the spawned obstacle</returns>
    public EntityHandle SpawnTemporaryObstacle(Vector3 position, float duration, Vector3 size)
    {
        Console.WriteLine($"[EntityManager] Spawning temporary obstacle at ({position.X:F2}, {position.Y:F2}, {position.Z:F2}) for {duration}s");
        
        var command = new SpawnEntityCommand
        {
            EntityType = EntityType.TemporaryObstacle,
            Position = position,
            ShapeType = ShapeType.Box,
            Size = size,
            Mass = 1.0f,
            IsStatic = false // Dynamic so it can be detected by pathfinding changes
        };
        
        var handle = SpawnEntity(command);
        
        // Track as temporary obstacle
        _temporaryObstacles[handle.EntityId] = new TemporaryObstacleData
        {
            SpawnTime = DateTime.UtcNow,
            Duration = duration
        };
        
        return handle;
    }
    
    /// <summary>
    /// Despawns an entity and removes it from the physics world.
    /// </summary>
    public void DespawnEntity(int entityId)
    {
        if (!_activeEntities.TryGetValue(entityId, out var entity))
        {
            Console.WriteLine($"[EntityManager] Warning: Attempted to despawn non-existent entity {entityId}");
            return;
        }
        
        Console.WriteLine($"[EntityManager] Despawning entity {entityId}");
        
        // Remove from physics world
        _physicsWorld.UnregisterEntity(entity);
        
        // Remove from tracking
        _activeEntities.Remove(entityId);
        _temporaryObstacles.Remove(entityId);
        
        // Raise event
        OnEntityDespawned?.Invoke(entityId);
    }
    
    /// <summary>
    /// Gets all active entities of a specific type.
    /// </summary>
    public List<PhysicsEntity> GetEntitiesOfType(EntityType type)
    {
        return _activeEntities.Values
            .Where(e => e.EntityType == type)
            .ToList();
    }
    
    /// <summary>
    /// Gets an entity by its ID.
    /// </summary>
    public PhysicsEntity? GetEntityById(int entityId)
    {
        _activeEntities.TryGetValue(entityId, out var entity);
        return entity;
    }
    
    /// <summary>
    /// Gets all active entities.
    /// </summary>
    public IReadOnlyDictionary<int, PhysicsEntity> GetAllEntities()
    {
        return _activeEntities;
    }
    
    /// <summary>
    /// Updates temporary obstacles and despawns expired ones.
    /// Call this every frame.
    /// </summary>
    public void Update(float deltaTime)
    {
        var now = DateTime.UtcNow;
        var toRemove = new List<int>();
        
        // Check for expired temporary obstacles
        foreach (var kvp in _temporaryObstacles)
        {
            var data = kvp.Value;
            var elapsed = (now - data.SpawnTime).TotalSeconds;
            
            if (elapsed >= data.Duration)
            {
                toRemove.Add(kvp.Key);
            }
        }
        
        // Despawn expired obstacles
        foreach (var entityId in toRemove)
        {
            Console.WriteLine($"[EntityManager] Temporary obstacle {entityId} expired, despawning");
            DespawnEntity(entityId);
        }
    }
}

/// <summary>
/// Handle to a spawned entity.
/// </summary>
public class EntityHandle
{
    public int EntityId { get; }
    public PhysicsEntity Entity { get; }
    
    public EntityHandle(int entityId, PhysicsEntity entity)
    {
        EntityId = entityId;
        Entity = entity;
    }
}

/// <summary>
/// Internal data for tracking temporary obstacles.
/// </summary>
internal class TemporaryObstacleData
{
    public DateTime SpawnTime { get; set; }
    public float Duration { get; set; }
}
