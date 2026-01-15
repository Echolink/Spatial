using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using System.Numerics;

namespace Spatial.Physics;

/// <summary>
/// Main physics world manager that wraps BepuPhysics v2 Simulation.
/// This is the core of the physics system - all physics operations go through here.
/// 
/// Key BepuPhysics v2 concepts:
/// - Simulation: The main physics world container
/// - BufferPool: Memory management for physics operations (recommended pattern)
/// - BodyHandle: Type-safe reference to a physics body
/// - Fixed Timestep: Ensures deterministic simulation for multiplayer
/// </summary>
public class PhysicsWorld : IDisposable
{
    private readonly Simulation _simulation;
    private readonly BufferPool _bufferPool;
    private readonly PhysicsEntityRegistry _entityRegistry;
    private CollisionHandler _collisionHandler;
    private readonly PhysicsConfiguration _config;
    
    /// <summary>
    /// Gets the entity registry for looking up entities.
    /// </summary>
    public PhysicsEntityRegistry EntityRegistry => _entityRegistry;
    
    /// <summary>
    /// Gets the BepuPhysics simulation instance.
    /// </summary>
    public Simulation Simulation => _simulation;
    
    /// <summary>
    /// Creates a new physics world with the specified configuration.
    /// </summary>
    public PhysicsWorld(PhysicsConfiguration? config = null, CollisionEventHandler? onCollision = null)
    {
        _config = config ?? new PhysicsConfiguration();
        
        // Create BufferPool - BepuPhysics v2 recommended pattern
        // BufferPool reuses memory efficiently, improving performance
        _bufferPool = new BufferPool();
        
        // Create collision handler
        _entityRegistry = new PhysicsEntityRegistry();
        _collisionHandler = new CollisionHandler(_entityRegistry, onCollision);
        
        // Create simulation with our collision handler
        // NarrowPhaseCallbacks handles collision detection
        // PoseIntegratorCallbacks handles gravity and other forces
        var poseIntegrator = new GravityPoseIntegrator(_config.Gravity);
        // SolveDescription: (velocityIterationCount, substepCount)
        // Using default values: 1 velocity iteration, 1 substep
        var solveDescription = new SolveDescription(8, 1);
        _simulation = Simulation.Create(
            _bufferPool,
            _collisionHandler,
            poseIntegrator,
            solveDescription
        );
    }
    
    /// <summary>
    /// Updates the physics simulation by one timestep.
    /// Call this every frame with a fixed timestep for deterministic simulation.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Fixed timestep ensures deterministic simulation
        // Important for multiplayer games where all clients must simulate identically
        _simulation.Timestep(_config.Timestep);
    }
    
    /// <summary>
    /// Registers a new entity with a physics body.
    /// </summary>
    /// <param name="entityId">Game entity ID</param>
    /// <param name="entityType">Type of entity</param>
    /// <param name="position">Initial position</param>
    /// <param name="shape">Collision shape</param>
    /// <param name="isStatic">Whether the entity is static (immovable)</param>
    /// <returns>The created physics entity</returns>
    /// <summary>
    /// Registers a new entity with a physics body.
    /// </summary>
    /// <param name="entityId">Game entity ID</param>
    /// <param name="entityType">Type of entity</param>
    /// <param name="position">Initial position</param>
    /// <param name="shape">Collision shape</param>
    /// <param name="isStatic">Whether the entity is static (immovable)</param>
    /// <param name="mass">Mass for dynamic bodies (ignored for static bodies)</param>
    /// <returns>The created physics entity</returns>
    public PhysicsEntity RegisterEntity(int entityId, EntityType entityType, Vector3 position, TypedIndex shape, bool isStatic = false, float mass = 1.0f)
    {
        PhysicsEntity entity;
        
        if (isStatic)
        {
            // In BepuPhysics v2, static bodies go in the Statics collection
            var staticDescription = new StaticDescription(
                position,
                Quaternion.Identity,
                shape
            );
            var staticHandle = _simulation.Statics.Add(staticDescription);
            entity = new PhysicsEntity(entityId, entityType, staticHandle, shape);
        }
        else
        {
            // For dynamic bodies, we need proper inertia computed from the shape
            // Since we only have TypedIndex here, we cannot compute inertia properly
            // This method should be used with pre-computed inertia from Create*ShapeWithInertia methods
            // For backward compatibility, we'll create a basic inertia, but this may cause issues
            // TODO: Consider deprecating this overload in favor of RegisterEntityWithInertia
            var basicInertia = new BodyInertia { InverseMass = 1f / mass };
            var bodyDescription = new BodyDescription
            {
                Activity = new BodyActivityDescription(0.01f),
                Collidable = new CollidableDescription(shape, 0.1f),
                Pose = new RigidPose(position, Quaternion.Identity),
                LocalInertia = basicInertia
            };
            
            // Add dynamic body to simulation
            var bodyHandle = _simulation.Bodies.Add(bodyDescription);
            entity = new PhysicsEntity(entityId, entityType, bodyHandle, shape);
        }
        
        // Register entity
        _entityRegistry.Register(entity);
        
        return entity;
    }
    
    /// <summary>
    /// Registers a new entity with pre-computed inertia (recommended for dynamic bodies).
    /// </summary>
    public PhysicsEntity RegisterEntityWithInertia(int entityId, EntityType entityType, Vector3 position, TypedIndex shape, BodyInertia inertia, bool isStatic = false)
    {
        PhysicsEntity entity;
        
        if (isStatic)
        {
            // In BepuPhysics v2, static bodies go in the Statics collection
            var staticDescription = new StaticDescription(
                position,
                Quaternion.Identity,
                shape
            );
            var staticHandle = _simulation.Statics.Add(staticDescription);
            entity = new PhysicsEntity(entityId, entityType, staticHandle, shape);
        }
        else
        {
            // Create dynamic body description
            var bodyDescription = new BodyDescription
            {
                Activity = new BodyActivityDescription(0.001f), // Low threshold to keep body active
                Collidable = new CollidableDescription(shape, 0.1f), // 0.1f is speculative margin
                Pose = new RigidPose(position, Quaternion.Identity),
                LocalInertia = inertia
            };
            
            var bodyHandle = _simulation.Bodies.Add(bodyDescription);
            
            // Ensure dynamic bodies are awake and active
            var bodyReference = _simulation.Bodies[bodyHandle];
            bodyReference.Awake = true; // Explicitly wake the body
            
            entity = new PhysicsEntity(entityId, entityType, bodyHandle, shape);
        }
        
        _entityRegistry.Register(entity);
        
        return entity;
    }
    
    /// <summary>
    /// Unregisters an entity and removes its physics body.
    /// </summary>
    public void UnregisterEntity(PhysicsEntity entity)
    {
        if (entity.IsStatic)
        {
            _simulation.Statics.Remove(entity.StaticHandle);
        }
        else
        {
            _simulation.Bodies.Remove(entity.BodyHandle);
        }
        _entityRegistry.Unregister(entity);
    }
    
    /// <summary>
    /// Gets the position of an entity.
    /// </summary>
    public Vector3 GetEntityPosition(PhysicsEntity entity)
    {
        if (entity.IsStatic)
        {
            var staticReference = _simulation.Statics[entity.StaticHandle];
            return staticReference.Pose.Position;
        }
        else
        {
            var bodyReference = _simulation.Bodies[entity.BodyHandle];
            return bodyReference.Pose.Position;
        }
    }
    
    /// <summary>
    /// Sets the position of an entity.
    /// </summary>
    public void SetEntityPosition(PhysicsEntity entity, Vector3 position)
    {
        if (entity.IsStatic)
        {
            var staticReference = _simulation.Statics[entity.StaticHandle];
            var pose = staticReference.Pose;
            pose.Position = position;
            staticReference.Pose = pose;
        }
        else
        {
            var bodyReference = _simulation.Bodies[entity.BodyHandle];
            var pose = bodyReference.Pose;
            pose.Position = position;
            bodyReference.Pose = pose;
        }
    }
    
    /// <summary>
    /// Gets the velocity of an entity.
    /// </summary>
    public Vector3 GetEntityVelocity(PhysicsEntity entity)
    {
        if (entity.IsStatic)
            return Vector3.Zero; // Static bodies have no velocity
        
        var bodyReference = _simulation.Bodies[entity.BodyHandle];
        return bodyReference.Velocity.Linear;
    }
    
    /// <summary>
    /// Sets the velocity of an entity.
    /// Velocity-based movement is recommended for responsive character control.
    /// </summary>
    public void SetEntityVelocity(PhysicsEntity entity, Vector3 velocity)
    {
        if (entity.IsStatic)
            return; // Can't set velocity on static bodies
        
        var bodyReference = _simulation.Bodies[entity.BodyHandle];
        
        // Wake the body if it's asleep - critical for movement to work!
        if (!bodyReference.Awake)
        {
            bodyReference.Awake = true;
        }
        
        var vel = bodyReference.Velocity;
        vel.Linear = velocity;
        bodyReference.Velocity = vel;
    }
    
    /// <summary>
    /// Applies a linear impulse to an entity.
    /// Useful for jump mechanics or knockback effects.
    /// </summary>
    public void ApplyLinearImpulse(PhysicsEntity entity, Vector3 impulse)
    {
        if (entity.IsStatic)
            return; // Can't apply impulse to static bodies
        
        var bodyReference = _simulation.Bodies[entity.BodyHandle];
        bodyReference.ApplyLinearImpulse(impulse);
    }
    
    /// <summary>
    /// Creates a box shape and returns its index.
    /// Shapes are shared and can be reused by multiple bodies.
    /// </summary>
    public TypedIndex CreateBoxShape(Vector3 size)
    {
        var box = new Box(size.X, size.Y, size.Z);
        return _simulation.Shapes.Add(box);
    }
    
    /// <summary>
    /// Creates a box shape and computes its inertia for the given mass.
    /// </summary>
    public (TypedIndex ShapeIndex, BodyInertia Inertia) CreateBoxShapeWithInertia(Vector3 size, float mass)
    {
        var box = new Box(size.X, size.Y, size.Z);
        var inertia = box.ComputeInertia(mass);
        var shapeIndex = _simulation.Shapes.Add(box);
        return (shapeIndex, inertia);
    }
    
    /// <summary>
    /// Creates a capsule shape (good for characters).
    /// </summary>
    public TypedIndex CreateCapsuleShape(float radius, float length)
    {
        var capsule = new Capsule(radius, length);
        return _simulation.Shapes.Add(capsule);
    }
    
    /// <summary>
    /// Creates a capsule shape and computes its inertia for the given mass.
    /// </summary>
    public (TypedIndex ShapeIndex, BodyInertia Inertia) CreateCapsuleShapeWithInertia(float radius, float length, float mass)
    {
        var capsule = new Capsule(radius, length);
        var inertia = capsule.ComputeInertia(mass);
        var shapeIndex = _simulation.Shapes.Add(capsule);
        return (shapeIndex, inertia);
    }
    
    /// <summary>
    /// Creates a sphere shape.
    /// </summary>
    public TypedIndex CreateSphereShape(float radius)
    {
        var sphere = new Sphere(radius);
        return _simulation.Shapes.Add(sphere);
    }
    
    /// <summary>
    /// Creates a sphere shape and computes its inertia for the given mass.
    /// </summary>
    public (TypedIndex ShapeIndex, BodyInertia Inertia) CreateSphereShapeWithInertia(float radius, float mass)
    {
        var sphere = new Sphere(radius);
        var inertia = sphere.ComputeInertia(mass);
        var shapeIndex = _simulation.Shapes.Add(sphere);
        return (shapeIndex, inertia);
    }
    
    /// <summary>
    /// Performs a raycast from start to end.
    /// Returns true if something was hit.
    /// Note: For MVP, this is simplified. Full implementation would return hit details.
    /// </summary>
    public bool Raycast(Vector3 start, Vector3 end)
    {
        var direction = end - start;
        var rayOrigin = start;
        var rayDirection = Vector3.Normalize(direction);
        var maxDistance = direction.Length();
        
        // TODO: Implement proper raycast using BepuPhysics v2 API
        // This requires checking the correct Ray/RayHit types
        return false; // Placeholder
    }
    
    /// <summary>
    /// Gets all entities within a specified radius of a position.
    /// Used for local avoidance and spatial queries.
    /// </summary>
    /// <param name="position">Center position</param>
    /// <param name="radius">Search radius</param>
    /// <returns>List of entities within the radius</returns>
    public List<PhysicsEntity> GetEntitiesInRadius(Vector3 position, float radius)
    {
        var result = new List<PhysicsEntity>();
        var radiusSquared = radius * radius;
        
        // Query all registered entities
        foreach (var entity in _entityRegistry.GetAllEntities())
        {
            var entityPos = GetEntityPosition(entity);
            var distanceSquared = Vector3.DistanceSquared(position, entityPos);
            
            if (distanceSquared <= radiusSquared)
            {
                result.Add(entity);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets the N closest entities to a position.
    /// </summary>
    /// <param name="position">Center position</param>
    /// <param name="count">Maximum number of entities to return</param>
    /// <param name="maxRadius">Optional maximum search radius</param>
    /// <returns>List of closest entities, sorted by distance</returns>
    public List<PhysicsEntity> GetClosestEntities(Vector3 position, int count, float maxRadius = float.MaxValue)
    {
        var radiusSquared = maxRadius * maxRadius;
        
        // Get all entities within max radius and sort by distance
        return _entityRegistry.GetAllEntities()
            .Select(entity => new { Entity = entity, DistanceSquared = Vector3.DistanceSquared(position, GetEntityPosition(entity)) })
            .Where(x => x.DistanceSquared <= radiusSquared)
            .OrderBy(x => x.DistanceSquared)
            .Take(count)
            .Select(x => x.Entity)
            .ToList();
    }
    
    /// <summary>
    /// Gets all entities of a specific type within a radius.
    /// </summary>
    public List<PhysicsEntity> GetEntitiesInRadius(Vector3 position, float radius, EntityType entityType)
    {
        return GetEntitiesInRadius(position, radius)
            .Where(e => e.EntityType == entityType)
            .ToList();
    }
    
    /// <summary>
    /// Checks if any entities of a specific type are within a radius.
    /// More efficient than GetEntitiesInRadius when you only need to check presence.
    /// </summary>
    public bool HasEntitiesInRadius(Vector3 position, float radius, EntityType? entityType = null)
    {
        var radiusSquared = radius * radius;
        
        foreach (var entity in _entityRegistry.GetAllEntities())
        {
            if (entityType.HasValue && entity.EntityType != entityType.Value)
                continue;
            
            var entityPos = GetEntityPosition(entity);
            var distanceSquared = Vector3.DistanceSquared(position, entityPos);
            
            if (distanceSquared <= radiusSquared)
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Cleans up resources.
    /// </summary>
    public void Dispose()
    {
        _simulation.Dispose();
        _bufferPool.Clear();
    }
}

