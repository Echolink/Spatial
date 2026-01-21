using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using System.Numerics;

namespace Spatial.Physics;

/// <summary>
/// Handles collision detection callbacks from BepuPhysics.
/// Implements INarrowPhaseCallbacks to receive collision events.
/// 
/// This allows us to:
/// - Filter collisions (e.g., players don't collide with each other)
/// - Trigger game logic events (damage, pickups, etc.)
/// 
/// Enhanced with:
/// - Event callback support for game logic
/// - Collision pair tracking for deduplication
/// - Contact normal and penetration depth information
/// </summary>
public struct CollisionHandler : INarrowPhaseCallbacks
{
    private PhysicsEntityRegistry _entityRegistry;
    private CollisionEventHandler? _onCollision;
    private Dictionary<(int, int), DateTime>? _collisionPairTracker;
    private readonly float _collisionEventCooldown;
    
    // Ground contact callbacks - use holder object so they can be updated after creation
    private GroundContactCallbacks? _groundContactCallbacks;
    
    /// <summary>
    /// Creates a new collision handler.
    /// </summary>
    /// <param name="entityRegistry">Registry to look up entities from body handles</param>
    /// <param name="onCollision">Optional callback when collisions occur</param>
    /// <param name="collisionEventCooldown">Minimum time between collision events for the same pair (seconds)</param>
    /// <param name="groundContactCallbacks">Optional holder for ground contact callbacks (can be updated after creation)</param>
    public CollisionHandler(PhysicsEntityRegistry entityRegistry, CollisionEventHandler? onCollision = null, float collisionEventCooldown = 0.1f,
        GroundContactCallbacks? groundContactCallbacks = null)
    {
        _entityRegistry = entityRegistry;
        _onCollision = onCollision;
        _collisionEventCooldown = collisionEventCooldown;
        _collisionPairTracker = onCollision != null ? new Dictionary<(int, int), DateTime>() : null;
        _groundContactCallbacks = groundContactCallbacks;
    }
    
    /// <summary>
    /// Sets the collision callback handler.
    /// </summary>
    public void SetCollisionCallback(CollisionEventHandler callback)
    {
        _onCollision = callback;
        _collisionPairTracker ??= new Dictionary<(int, int), DateTime>();
    }
    
    /// <summary>
    /// Called when BepuPhysics detects a collision.
    /// We can filter collisions here before they're processed.
    /// </summary>
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        // FIXED: Use larger speculative margin for ground collisions to prevent penetration
        // Speculative contacts are created when objects are within this distance
        // This allows the physics engine to predict and prevent penetration BEFORE it happens
        // For agent-agent: use smaller margin (0.05) to let them get close
        // For agent-ground: use larger margin (0.2) to prevent sinking
        
        PhysicsEntity? entityA = ResolveEntity(a);
        PhysicsEntity? entityB = ResolveEntity(b);
        
        bool isAgentAgentCollision = IsAgent(entityA) && IsAgent(entityB);
        
        if (isAgentAgentCollision)
        {
            // Agent-agent: smaller margin for close blocking
            speculativeMargin = Math.Max(speculativeMargin, 0.05f);
        }
        else
        {
            // Agent-ground or other: larger margin to prevent penetration
            speculativeMargin = Math.Max(speculativeMargin, 0.2f);
        }
        
        // Allow all contacts (no filtering for now)
        return true;
    }
    
    /// <summary>
    /// Called when BepuPhysics detects a collision between compound shapes.
    /// </summary>
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        // Allow all contacts (no filtering for now)
        return true;
    }
    
    /// <summary>
    /// Called to configure contact manifolds.
    /// This is where we can trigger game logic events when collisions occur.
    /// </summary>
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterialProperties) 
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        // Resolve entities to check if this is agent-agent collision
        PhysicsEntity? entityA = ResolveEntity(pair.A);
        PhysicsEntity? entityB = ResolveEntity(pair.B);
        
        // Check if both entities are agents (Player, NPC, Enemy)
        bool isAgentAgentCollision = IsAgent(entityA) && IsAgent(entityB);
        
        // Check if either entity is explicitly pushable
        bool isPushable = (entityA?.IsPushable ?? false) || (entityB?.IsPushable ?? false);
        
        // Configure material properties based on collision type
        // Agent-agent collisions block unless one is explicitly pushable
        if (isAgentAgentCollision && !isPushable)
        {
            // AGENT-AGENT COLLISION: Very rigid blocking behavior, no pushing
            // High spring frequency = stiff contact (like hitting a wall)
            // Zero maximum recovery = no bouncing or pushing forces
            // Very low friction = can slide past each other easily (for avoidance)
            pairMaterialProperties = new PairMaterialProperties
            {
                FrictionCoefficient = 0.0f, // Zero friction for easy sliding (agents should slide past each other)
                MaximumRecoveryVelocity = 0f, // No recovery forces = no pushing!
                SpringSettings = new SpringSettings(240f, 1f) // Extra stiff (240 Hz), critically damped - prevents any pushing
            };
        }
        else
        {
            // NORMAL COLLISION (agent-world, agent-obstacle, etc.)
            // FIXED: Increased spring frequency to prevent ground penetration
            // Higher frequency = stiffer collision response = less penetration
            pairMaterialProperties = new PairMaterialProperties
            {
                FrictionCoefficient = 0.1f, // Low friction for smooth character movement
                MaximumRecoveryVelocity = float.MaxValue, // No limit on penetration recovery speed
                SpringSettings = new SpringSettings(120f, 1f) // INCREASED: 120 Hz (4x stiffer), critically damped
            };
        }
        
        // Detect ground contacts (dynamic entity colliding with static ground)
        if (_groundContactCallbacks?.OnGroundContact != null)
        {
            DetectGroundContact(pair, ref manifold);
        }
        
        // Trigger collision event if callback is registered
        if (_onCollision != null)
        {
            TriggerCollisionEvent(pair, ref manifold);
        }
        
        return true; // Allow the contact
    }
    
    /// <summary>
    /// Resolves a collidable reference to a physics entity.
    /// </summary>
    private PhysicsEntity? ResolveEntity(CollidableReference collidable)
    {
        if (collidable.Mobility == CollidableMobility.Static)
        {
            return _entityRegistry.GetEntityByStaticHandle(collidable.StaticHandle);
        }
        else
        {
            return _entityRegistry.GetEntityByBodyHandle(collidable.BodyHandle);
        }
    }
    
    /// <summary>
    /// Checks if an entity is an agent (Player, NPC, or Enemy).
    /// </summary>
    private bool IsAgent(PhysicsEntity? entity)
    {
        if (entity == null)
            return false;
        
        return entity.EntityType == EntityType.Player ||
               entity.EntityType == EntityType.NPC ||
               entity.EntityType == EntityType.Enemy;
    }
    
    /// <summary>
    /// Triggers a collision event for game logic processing.
    /// </summary>
    private void TriggerCollisionEvent<TManifold>(CollidablePair pair, ref TManifold manifold)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        // Resolve entities from handles
        PhysicsEntity? entityA = null;
        PhysicsEntity? entityB = null;
        
        // Handle A
        if (pair.A.Mobility == CollidableMobility.Static)
        {
            entityA = _entityRegistry.GetEntityByStaticHandle(pair.A.StaticHandle);
        }
        else
        {
            entityA = _entityRegistry.GetEntityByBodyHandle(pair.A.BodyHandle);
        }
        
        // Handle B
        if (pair.B.Mobility == CollidableMobility.Static)
        {
            entityB = _entityRegistry.GetEntityByStaticHandle(pair.B.StaticHandle);
        }
        else
        {
            entityB = _entityRegistry.GetEntityByBodyHandle(pair.B.BodyHandle);
        }
        
        // Only fire event if both entities are registered
        if (entityA == null || entityB == null)
            return;
        
        // Check cooldown for this collision pair
        var pairKey = GetOrderedPair(entityA.EntityId, entityB.EntityId);
        var now = DateTime.UtcNow;
        
        if (_collisionPairTracker != null)
        {
            if (_collisionPairTracker.TryGetValue(pairKey, out var lastTime))
            {
                var elapsed = (now - lastTime).TotalSeconds;
                if (elapsed < _collisionEventCooldown)
                {
                    return; // Too soon, skip this event
                }
            }
            
            _collisionPairTracker[pairKey] = now;
        }
        
        // Get contact information from manifold
        var contactCount = manifold.Count;
        if (contactCount > 0)
        {
            // Get first contact point
            manifold.GetContact(0, out var offset, out var normal, out var depth, out _);
            
            // Create collision event
            var collisionEvent = new CollisionEvent(
                entityA,
                entityB,
                offset, // Contact point (relative to body A)
                normal,
                depth
            );
            
            // Fire callback
            _onCollision?.Invoke(collisionEvent);
        }
    }
    
    /// <summary>
    /// Detects ground contacts and notifies the callback.
    /// Ground contact = dynamic entity colliding with static entity, with normal pointing upward.
    /// </summary>
    private void DetectGroundContact<TManifold>(CollidablePair pair, ref TManifold manifold)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        // Resolve entities from handles
        PhysicsEntity? entityA = null;
        PhysicsEntity? entityB = null;
        
        // Handle A
        if (pair.A.Mobility == CollidableMobility.Static)
        {
            entityA = _entityRegistry.GetEntityByStaticHandle(pair.A.StaticHandle);
        }
        else
        {
            entityA = _entityRegistry.GetEntityByBodyHandle(pair.A.BodyHandle);
        }
        
        // Handle B
        if (pair.B.Mobility == CollidableMobility.Static)
        {
            entityB = _entityRegistry.GetEntityByStaticHandle(pair.B.StaticHandle);
        }
        else
        {
            entityB = _entityRegistry.GetEntityByBodyHandle(pair.B.BodyHandle);
        }
        
        // Need both entities to detect ground contact
        if (entityA == null || entityB == null)
            return;
        
        // Determine which is dynamic and which is static
        PhysicsEntity? dynamicEntity = null;
        PhysicsEntity? groundEntity = null;
        
        if (!entityA.IsStatic && entityB.IsStatic)
        {
            dynamicEntity = entityA;
            groundEntity = entityB;
        }
        else if (entityA.IsStatic && !entityB.IsStatic)
        {
            dynamicEntity = entityB;
            groundEntity = entityA;
        }
        else
        {
            // Both static or both dynamic - not a ground contact
            return;
        }
        
        // Get contact normal to check if it's pointing upward (ground contact)
        var contactCount = manifold.Count;
        if (contactCount > 0)
        {
            manifold.GetContact(0, out var offset, out var normal, out var depth, out _);
            
            // Check if normal is pointing mostly upward (Y > 0.7 means ~45 degree slope or flatter)
            bool isGroundContact = normal.Y > 0.7f;
            
            if (isGroundContact && _groundContactCallbacks?.OnGroundContact != null)
            {
                _groundContactCallbacks.OnGroundContact(dynamicEntity, groundEntity);
            }
        }
    }
    
    /// <summary>
    /// Gets an ordered pair of entity IDs (smaller first).
    /// </summary>
    private (int, int) GetOrderedPair(int a, int b)
    {
        return a < b ? (a, b) : (b, a);
    }
    
    /// <summary>
    /// Called to configure contact manifolds for convex shapes (child contacts in compounds).
    /// Note: Material properties are inherited from the parent ConfigureContactManifold call.
    /// </summary>
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
    {
        return true; // Allow the contact (material properties set by parent call)
    }
    
    public void OnContactRemoved(int workerIndex, CollidablePair pair)
    {
        // Notify ground contact removal if callback is registered
        if (_groundContactCallbacks?.OnGroundContactRemoved != null)
        {
            // Resolve entities
            PhysicsEntity? entityA = null;
            PhysicsEntity? entityB = null;
            
            if (pair.A.Mobility == CollidableMobility.Static)
            {
                entityA = _entityRegistry.GetEntityByStaticHandle(pair.A.StaticHandle);
            }
            else
            {
                entityA = _entityRegistry.GetEntityByBodyHandle(pair.A.BodyHandle);
            }
            
            if (pair.B.Mobility == CollidableMobility.Static)
            {
                entityB = _entityRegistry.GetEntityByStaticHandle(pair.B.StaticHandle);
            }
            else
            {
                entityB = _entityRegistry.GetEntityByBodyHandle(pair.B.BodyHandle);
            }
            
            if (entityA == null || entityB == null)
                return;
            
            // Determine which is dynamic and which is static
            PhysicsEntity? dynamicEntity = null;
            PhysicsEntity? groundEntity = null;
            
            if (!entityA.IsStatic && entityB.IsStatic)
            {
                dynamicEntity = entityA;
                groundEntity = entityB;
            }
            else if (entityA.IsStatic && !entityB.IsStatic)
            {
                dynamicEntity = entityB;
                groundEntity = entityA;
            }
            else
            {
                return;
            }
            
            _groundContactCallbacks.OnGroundContactRemoved(dynamicEntity, groundEntity);
        }
    }
    
    public void Initialize(Simulation simulation) { }
    
    public void Dispose() { }
}

/// <summary>
/// Holder for ground contact callbacks that can be updated after CollisionHandler creation.
/// </summary>
public class GroundContactCallbacks
{
    public Action<PhysicsEntity, PhysicsEntity>? OnGroundContact { get; set; }
    public Action<PhysicsEntity, PhysicsEntity>? OnGroundContactRemoved { get; set; }
}
