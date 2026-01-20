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
    
    // Ground contact callbacks holder (can be updated after creation)
    private readonly GroundContactCallbacks _groundContactCallbacks;
    
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
        
        // Create ground contact callbacks holder (can be updated later)
        _groundContactCallbacks = new GroundContactCallbacks();
        
        // Create BufferPool - BepuPhysics v2 recommended pattern
        // BufferPool reuses memory efficiently, improving performance
        _bufferPool = new BufferPool();
        
        // Create collision handler with ground contact callbacks holder
        _entityRegistry = new PhysicsEntityRegistry();
        _collisionHandler = new CollisionHandler(_entityRegistry, onCollision, 
            groundContactCallbacks: _groundContactCallbacks);
        
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
    /// Registers ground contact callbacks for character controller integration.
    /// These callbacks are called when dynamic entities make/lose contact with static ground.
    /// Can be called after PhysicsWorld creation - callbacks are stored in a mutable holder.
    /// </summary>
    public void RegisterGroundContactCallbacks(
        Action<PhysicsEntity, PhysicsEntity>? onGroundContact,
        Action<PhysicsEntity, PhysicsEntity>? onGroundContactRemoved)
    {
        _groundContactCallbacks.OnGroundContact = onGroundContact;
        _groundContactCallbacks.OnGroundContactRemoved = onGroundContactRemoved;
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
    /// <param name="disableGravity">If true, gravity will not affect this entity (useful for navmesh-controlled agents)</param>
    public PhysicsEntity RegisterEntityWithInertia(int entityId, EntityType entityType, Vector3 position, TypedIndex shape, BodyInertia inertia, bool isStatic = false, bool disableGravity = false)
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
                Activity = new BodyActivityDescription(0.01f), // Activity threshold - higher = stays active longer
                Collidable = new CollidableDescription(shape, 0.5f), // FIXED: Increased speculative margin from 0.1f to 0.5f for better collision detection
                Pose = new RigidPose(position, Quaternion.Identity),
                LocalInertia = disableGravity ? new BodyInertia { InverseMass = inertia.InverseMass } : inertia
            };
            
            var bodyHandle = _simulation.Bodies.Add(bodyDescription);
            
            // Ensure dynamic bodies are awake and active
            var bodyReference = _simulation.Bodies[bodyHandle];
            bodyReference.Awake = true; // Explicitly wake the body
            
            entity = new PhysicsEntity(entityId, entityType, bodyHandle, shape);
            entity.GravityDisabled = disableGravity;
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
        
        // Wake the body if it's asleep - critical for impulse to work!
        if (!bodyReference.Awake)
        {
            bodyReference.Awake = true;
        }
        
        bodyReference.ApplyLinearImpulse(impulse);
    }
    
    /// <summary>
    /// Applies a linear impulse to an entity by ID.
    /// Convenience method that looks up the entity first.
    /// </summary>
    public bool ApplyLinearImpulse(int entityId, Vector3 impulse)
    {
        var entity = _entityRegistry.GetEntityById(entityId);
        if (entity == null)
            return false;
        
        ApplyLinearImpulse(entity, impulse);
        return true;
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
    /// Creates a mesh shape from triangle data for static collision.
    /// Uses actual triangle geometry instead of approximating with a bounding box.
    /// Note: BepuPhysics v2 meshes are one-sided based on triangle winding order.
    /// </summary>
    /// <param name="vertices">Array of vertex positions</param>
    /// <param name="indices">Array of triangle indices (must be multiple of 3)</param>
    /// <param name="doubleSided">If true, creates reversed triangles for two-sided collision (for walkable ground)</param>
    /// <returns>Shape index for the created mesh</returns>
    public TypedIndex CreateMeshShape(Vector3[] vertices, int[] indices, bool doubleSided = false)
    {
        if (vertices.Length < 3)
            throw new ArgumentException("Mesh must have at least 3 vertices", nameof(vertices));
        
        if (indices.Length < 3 || indices.Length % 3 != 0)
            throw new ArgumentException("Mesh indices must be a multiple of 3 (triangles)", nameof(indices));
        
        // Calculate triangle count (double if two-sided)
        int originalTriangleCount = indices.Length / 3;
        int totalTriangleCount = doubleSided ? originalTriangleCount * 2 : originalTriangleCount;
        
        // Allocate triangle buffer from pool
        _bufferPool.Take<Triangle>(totalTriangleCount, out var triangles);
        
        // Convert vertices and indices to BepuPhysics Triangle structs
        for (int i = 0; i < originalTriangleCount; i++)
        {
            int idx = i * 3;
            var v0 = vertices[indices[idx]];
            var v1 = vertices[indices[idx + 1]];
            var v2 = vertices[indices[idx + 2]];
            
            // Original triangle
            triangles[i] = new Triangle(v0, v1, v2);
            
            // Add reversed triangle for double-sided collision (flip winding order)
            if (doubleSided)
            {
                triangles[originalTriangleCount + i] = new Triangle(v0, v2, v1);
            }
        }
        
        // Create mesh shape
        // BepuPhysics v2 Mesh uses a tree structure for efficient collision detection
        var mesh = new Mesh(triangles, Vector3.One, _bufferPool);
        
        // Add mesh to simulation's shape collection
        var shapeIndex = _simulation.Shapes.Add(mesh);
        
        string sideInfo = doubleSided ? " (double-sided)" : " (single-sided)";
        Console.WriteLine($"[PhysicsWorld] Created mesh shape with {totalTriangleCount} triangles{sideInfo}");
        
        return shapeIndex;
    }
    
    /// <summary>
    /// Stores raw mesh data for an entity.
    /// This mesh data will be used by NavMeshBuilder to extract geometry for navmesh generation.
    /// Note: The mesh is converted to a bounding box for physics collision purposes.
    /// </summary>
    private class MeshStorage
    {
        public Vector3[] Vertices { get; set; } = Array.Empty<Vector3>();
        public int[] Indices { get; set; } = Array.Empty<int>();
        public NavMeshAreaType NavMeshArea { get; set; } = NavMeshAreaType.Walkable;
    }
    
    private readonly Dictionary<int, MeshStorage> _meshData = new();
    
    /// <summary>
    /// NavMesh area type for Recast navigation generation.
    /// </summary>
    public enum NavMeshAreaType
    {
        /// <summary>Walkable surface (ground, floors)</summary>
        Walkable = 0,
        /// <summary>Unwalkable/blocking volume (walls, buildings)</summary>
        Unwalkable = 1,
        /// <summary>Ignore (not included in navmesh)</summary>
        Ignore = 2
    }
    
    /// <summary>
    /// Creates a mesh-based static entity from raw triangle data.
    /// Uses actual triangle mesh collision geometry for accurate physics.
    /// The raw triangle data is also preserved for navmesh generation.
    /// </summary>
    /// <param name="entityId">Unique entity ID</param>
    /// <param name="entityType">Type of entity</param>
    /// <param name="vertices">Mesh vertices</param>
    /// <param name="indices">Mesh triangle indices</param>
    /// <param name="position">World position offset</param>
    /// <param name="navMeshArea">NavMesh area type for this mesh</param>
    /// <returns>Created physics entity</returns>
    public PhysicsEntity RegisterMeshEntity(int entityId, EntityType entityType, 
        Vector3[] vertices, int[] indices, Vector3 position = default, NavMeshAreaType navMeshArea = NavMeshAreaType.Walkable)
    {
        if (vertices.Length < 3)
        {
            throw new ArgumentException("Mesh must have at least 3 vertices", nameof(vertices));
        }
        
        if (indices.Length < 3 || indices.Length % 3 != 0)
        {
            throw new ArgumentException("Mesh indices must be a multiple of 3 (triangles)", nameof(indices));
        }
        
        // Store mesh data for navmesh extraction
        _meshData[entityId] = new MeshStorage
        {
            Vertices = vertices,
            Indices = indices,
            NavMeshArea = navMeshArea
        };
        
        // Calculate bounding box for debug logging and validation
        var min = vertices[0];
        var max = vertices[0];
        
        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        
        var center = (min + max) * 0.5f;
        var size = max - min;
        
        // FIXED: Create double-sided mesh for walkable surfaces (ground)
        // This ensures collision detection works from both above and below
        // Walkable surfaces need collision from the top (players walking on it)
        // Double-sided prevents issues when mesh normals point the wrong direction
        bool isWalkableGround = (navMeshArea == NavMeshAreaType.Walkable);
        var meshShape = CreateMeshShape(vertices, indices, doubleSided: isWalkableGround);
        
        // Debug logging for terrain mesh collision
        Console.WriteLine($"[PhysicsWorld] Registering mesh entity {entityId} with triangle mesh collision:");
        Console.WriteLine($"[PhysicsWorld]   Position: ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");
        Console.WriteLine($"[PhysicsWorld]   Triangles: {indices.Length / 3}");
        Console.WriteLine($"[PhysicsWorld]   NavMesh Area: {navMeshArea}");
        Console.WriteLine($"[PhysicsWorld]   Bounds: ({min.X:F2}, {min.Y:F2}, {min.Z:F2}) to ({max.X:F2}, {max.Y:F2}, {max.Z:F2})");
        Console.WriteLine($"[PhysicsWorld]   Size: ({size.X:F2}, {size.Y:F2}, {size.Z:F2})");
        
        // Register as static entity with mesh collision
        return RegisterEntity(entityId, entityType, position, meshShape, isStatic: true);
    }
    
    /// <summary>
    /// Gets the raw mesh data for an entity, if it was created with RegisterMeshEntity.
    /// Returns null if the entity doesn't have mesh data.
    /// </summary>
    public (Vector3[] vertices, int[] indices, NavMeshAreaType navMeshArea)? GetMeshData(int entityId)
    {
        if (_meshData.TryGetValue(entityId, out var meshStorage))
        {
            return (meshStorage.Vertices, meshStorage.Indices, meshStorage.NavMeshArea);
        }
        return null;
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

