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
        // ENHANCED: Use larger speculative margin for ground collisions to prevent penetration
        // Speculative contacts are created when objects are within this distance
        // This allows the physics engine to predict and prevent penetration BEFORE it happens
        // For agent-agent: use smaller margin (0.05) to let them get close
        // For agent-ground: use even larger margin (0.3) to prevent sinking during collisions
        
        PhysicsEntity? entityA = ResolveEntity(a);
        PhysicsEntity? entityB = ResolveEntity(b);
        
        bool isAgentAgentCollision = IsAgent(entityA) && IsAgent(entityB);
        bool isGroundCollision = (IsAgent(entityA) && entityB?.IsStatic == true) || 
                                 (IsAgent(entityB) && entityA?.IsStatic == true);
        
        if (isAgentAgentCollision)
        {
            // Agent-agent: smaller margin for close blocking
            speculativeMargin = Math.Max(speculativeMargin, 0.05f);
        }
        else if (isGroundCollision)
        {
            // Agent-ground: LARGER margin to prevent sinking (especially during agent collisions)
            speculativeMargin = Math.Max(speculativeMargin, 0.3f);
        }
        else
        {
            // Other collisions: standard margin
            speculativeMargin = Math.Max(speculativeMargin, 0.15f);
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
        // Check if this is ground collision specifically
        bool isGroundCollision = (IsAgent(entityA) && entityB?.IsStatic == true) || 
                                 (IsAgent(entityB) && entityA?.IsStatic == true);
        
        // Agent-agent collisions block unless one is explicitly pushable
        if (isAgentAgentCollision && !isPushable)
        {
            // AGENT-AGENT COLLISION: Very rigid blocking behavior, no pushing
            // High spring frequency = stiff contact (like hitting a wall)
            // Zero maximum recovery = no bouncing or pushing forces
            // Very low friction = can slide past each other easily (for avoidance)
            // 
            // CRITICAL: Vertical displacement prevention is handled by:
            // 1. MovementController: Aggressive Y position and velocity clamping
            // 2. CharacterController: Zero upward velocity when grounded
            // 3. This reduces spring stiffness slightly to minimize force spikes
            pairMaterialProperties = new PairMaterialProperties
            {
                FrictionCoefficient = 0.0f, // Zero friction for easy sliding (agents should slide past each other)
                MaximumRecoveryVelocity = 0f, // No recovery forces = no pushing!
                SpringSettings = new SpringSettings(180f, 1f) // Stiff (180 Hz), critically damped - balanced for blocking without force spikes
            };
        }
        else if (isGroundCollision)
        {
            // GROUND COLLISION: Extra stiff to prevent any sinking
            // This is critical to prevent agents from sinking when they collide with each other above ground
            pairMaterialProperties = new PairMaterialProperties
            {
                FrictionCoefficient = 0.1f, // Low friction for smooth movement
                MaximumRecoveryVelocity = float.MaxValue, // Unlimited recovery to push out of ground
                SpringSettings = new SpringSettings(180f, 1f) // VERY STIFF: 180 Hz (extra strength), critically damped
            };
        }
        else
        {
            // NORMAL COLLISION (agent-obstacle, etc.)
            pairMaterialProperties = new PairMaterialProperties
            {
                FrictionCoefficient = 0.1f, // Low friction for smooth character movement
                MaximumRecoveryVelocity = float.MaxValue, // No limit on penetration recovery speed
                SpringSettings = new SpringSettings(120f, 1f) // Standard: 120 Hz, critically damped
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
            
            // DIAGNOSTIC LOGGING: Log detailed contact information for Agent-3
            if (ContactDiagnostics.IsEnabled && dynamicEntity.EntityId == ContactDiagnostics.TrackedEntityId)
            {
                var normalAngleDegrees = MathF.Acos(normal.Y) * (180.0f / MathF.PI);
                ContactDiagnostics.LogContact(new ContactInfo
                {
                    DynamicEntityId = dynamicEntity.EntityId,
                    GroundEntityId = groundEntity.EntityId,
                    ContactNormal = normal,
                    PenetrationDepth = depth,
                    ContactOffset = offset,
                    NormalAngleDegrees = normalAngleDegrees,
                    IsGroundContact = isGroundContact,
                    Timestamp = DateTime.UtcNow
                });
            }
            
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

/// <summary>
/// Diagnostic utility for tracking detailed contact information.
/// Enable this to log contact data for a specific entity (e.g., Agent-3).
/// </summary>
public static class ContactDiagnostics
{
    private static readonly List<ContactInfo> _contactHistory = new();
    private static readonly object _lock = new object();
    private static int _maxHistorySize = 10000;
    
    /// <summary>
    /// Enable/disable contact diagnostics.
    /// </summary>
    public static bool IsEnabled { get; set; } = false;
    
    /// <summary>
    /// Entity ID to track (e.g., 103 for Agent-3).
    /// </summary>
    public static int TrackedEntityId { get; set; } = -1;
    
    /// <summary>
    /// Logs a contact event.
    /// </summary>
    public static void LogContact(ContactInfo contact)
    {
        lock (_lock)
        {
            _contactHistory.Add(contact);
            
            // Trim history if it gets too large
            if (_contactHistory.Count > _maxHistorySize)
            {
                _contactHistory.RemoveRange(0, _contactHistory.Count - _maxHistorySize);
            }
        }
    }
    
    /// <summary>
    /// Gets all recorded contacts.
    /// </summary>
    public static List<ContactInfo> GetContactHistory()
    {
        lock (_lock)
        {
            return new List<ContactInfo>(_contactHistory);
        }
    }
    
    /// <summary>
    /// Clears contact history.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _contactHistory.Clear();
        }
    }
    
    /// <summary>
    /// Prints a summary of contacts to console.
    /// </summary>
    public static void PrintSummary()
    {
        lock (_lock)
        {
            if (_contactHistory.Count == 0)
            {
                Console.WriteLine("[ContactDiagnostics] No contacts recorded.");
                return;
            }
            
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ CONTACT DIAGNOSTICS SUMMARY (Entity {TrackedEntityId})        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"Total Contacts Recorded: {_contactHistory.Count}");
            Console.WriteLine($"Time Range: {_contactHistory.First().Timestamp:HH:mm:ss.fff} to {_contactHistory.Last().Timestamp:HH:mm:ss.fff}");
            Console.WriteLine();
            
            // Analyze ground vs non-ground contacts
            var groundContacts = _contactHistory.Where(c => c.IsGroundContact).ToList();
            var nonGroundContacts = _contactHistory.Where(c => !c.IsGroundContact).ToList();
            
            Console.WriteLine($"Ground Contacts (normal Y > 0.7): {groundContacts.Count} ({100.0 * groundContacts.Count / _contactHistory.Count:F1}%)");
            Console.WriteLine($"Non-Ground Contacts (wall/ceiling): {nonGroundContacts.Count} ({100.0 * nonGroundContacts.Count / _contactHistory.Count:F1}%)");
            Console.WriteLine();
            
            if (groundContacts.Any())
            {
                Console.WriteLine("Ground Contact Statistics:");
                Console.WriteLine($"  Average Normal Angle: {groundContacts.Average(c => c.NormalAngleDegrees):F2}°");
                Console.WriteLine($"  Normal Angle Range: [{groundContacts.Min(c => c.NormalAngleDegrees):F2}°, {groundContacts.Max(c => c.NormalAngleDegrees):F2}°]");
                Console.WriteLine($"  Average Penetration: {groundContacts.Average(c => c.PenetrationDepth):F4}m");
                Console.WriteLine($"  Max Penetration: {groundContacts.Max(c => c.PenetrationDepth):F4}m");
                
                // Find periods where contact was lost
                var lostContactPeriods = new List<(DateTime start, DateTime end)>();
                DateTime? lastContactTime = null;
                
                foreach (var contact in _contactHistory.OrderBy(c => c.Timestamp))
                {
                    if (contact.IsGroundContact)
                    {
                        if (lastContactTime.HasValue)
                        {
                            var gap = (contact.Timestamp - lastContactTime.Value).TotalSeconds;
                            if (gap > 0.1) // More than 100ms gap
                            {
                                lostContactPeriods.Add((lastContactTime.Value, contact.Timestamp));
                            }
                        }
                        lastContactTime = contact.Timestamp;
                    }
                }
                
                if (lostContactPeriods.Any())
                {
                    Console.WriteLine();
                    Console.WriteLine($"⚠️ Ground Contact Lost {lostContactPeriods.Count} time(s):");
                    foreach (var (start, end) in lostContactPeriods.Take(10))
                    {
                        var duration = (end - start).TotalMilliseconds;
                        Console.WriteLine($"  - {start:HH:mm:ss.fff} to {end:HH:mm:ss.fff} (gap: {duration:F1}ms)");
                    }
                }
            }
            
            Console.WriteLine();
        }
    }
}

/// <summary>
/// Detailed information about a physics contact.
/// </summary>
public struct ContactInfo
{
    public int DynamicEntityId { get; set; }
    public int GroundEntityId { get; set; }
    public Vector3 ContactNormal { get; set; }
    public float PenetrationDepth { get; set; }
    public Vector3 ContactOffset { get; set; }
    public float NormalAngleDegrees { get; set; }
    public bool IsGroundContact { get; set; }
    public DateTime Timestamp { get; set; }
}
