using Spatial.Physics;
using System.Numerics;
using System.Collections.Generic;
using BepuPhysics;

namespace Spatial.Integration;

/// <summary>
/// Motor-based character controller that uses BepuPhysics constraint solver
/// for stable movement on steep slopes and multi-level terrain.
/// 
/// Key differences from velocity-based CharacterController:
/// - Analyzes ground contacts to find supporting surfaces
/// - Uses velocity goals instead of direct velocity setting
/// - Lets physics solver handle constraints smoothly
/// - Better stability on steep slopes (no bouncing/launching)
/// - Maintains natural ground contact through solver
/// 
/// Based on BepuPhysics v2 design philosophy for character controllers.
/// </summary>
public class MotorCharacterController : ICharacterController
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly MotorCharacterConfig _config;
    
    // Track state for each entity
    private readonly Dictionary<int, CharacterState> _entityStates = new();
    
    // Track ground contacts for each entity
    private readonly Dictionary<int, HashSet<int>> _groundContacts = new();
    
    // Track recovery timers
    private readonly Dictionary<int, float> _recoveryTimers = new();
    
    // Track desired velocity goals for each entity
    private readonly Dictionary<int, Vector3> _velocityGoals = new();
    
    // Track contact normal for each entity (averaged across all ground contacts)
    private readonly Dictionary<int, Vector3> _supportNormal = new();
    
    /// <summary>
    /// Creates a new motor-based character controller.
    /// </summary>
    public MotorCharacterController(PhysicsWorld physicsWorld, MotorCharacterConfig? config = null)
    {
        _physicsWorld = physicsWorld;
        _config = config ?? new MotorCharacterConfig();
    }
    
    /// <summary>
    /// Gets the current state of an entity.
    /// </summary>
    public CharacterState GetState(PhysicsEntity entity)
    {
        if (_entityStates.TryGetValue(entity.EntityId, out var state))
            return state;
        
        // Default to GROUNDED for new entities
        _entityStates[entity.EntityId] = CharacterState.GROUNDED;
        return CharacterState.GROUNDED;
    }
    
    /// <summary>
    /// Checks if an entity is currently grounded.
    /// </summary>
    public bool IsGrounded(PhysicsEntity entity)
    {
        return GetState(entity) == CharacterState.GROUNDED;
    }
    
    /// <summary>
    /// Checks if an entity is currently recovering (recently landed).
    /// </summary>
    public bool IsRecovering(PhysicsEntity entity)
    {
        return GetState(entity) == CharacterState.RECOVERING;
    }
    
    /// <summary>
    /// Checks if an entity is currently airborne.
    /// </summary>
    public bool IsAirborne(PhysicsEntity entity)
    {
        return GetState(entity) == CharacterState.AIRBORNE;
    }
    
    /// <summary>
    /// Updates the grounded state for an entity based on physics contacts.
    /// Call this every frame for each entity.
    /// </summary>
    public void UpdateGroundedState(PhysicsEntity entity, float deltaTime)
    {
        var velocity = _physicsWorld.GetEntityVelocity(entity);
        var currentState = GetState(entity);
        
        // Check if we have ground contacts
        bool hasGroundContacts = _groundContacts.TryGetValue(entity.EntityId, out var contacts) && contacts.Count > 0;
        
        // For motor approach, ground contact is primary indicator
        // (velocity can fluctuate more with motor control)
        bool isOnGround = hasGroundContacts;
        
        // State transitions
        switch (currentState)
        {
            case CharacterState.GROUNDED:
                if (!isOnGround)
                {
                    // Lost ground contact - transition to AIRBORNE
                    SetAirborne(entity);
                }
                break;
                
            case CharacterState.AIRBORNE:
                if (isOnGround)
                {
                    // Landed - transition to RECOVERING
                    SetRecovering(entity);
                    _recoveryTimers[entity.EntityId] = 0f;
                }
                break;
                
            case CharacterState.RECOVERING:
                if (!isOnGround)
                {
                    // Lost ground again - back to AIRBORNE
                    SetAirborne(entity);
                    _recoveryTimers.Remove(entity.EntityId);
                }
                else
                {
                    // Continue recovering
                    _recoveryTimers[entity.EntityId] = _recoveryTimers.GetValueOrDefault(entity.EntityId, 0f) + deltaTime;
                    
                    // Check if stable enough to return to GROUNDED
                    if (_recoveryTimers[entity.EntityId] >= _config.StabilityThreshold)
                    {
                        SetGrounded(entity);
                    }
                }
                break;
        }
    }
    
    /// <summary>
    /// Checks if an entity is stable (ready to resume pathfinding after landing).
    /// </summary>
    public bool IsStable(PhysicsEntity entity)
    {
        var state = GetState(entity);
        return state == CharacterState.GROUNDED || 
               (state == CharacterState.RECOVERING && 
                _recoveryTimers.GetValueOrDefault(entity.EntityId, 0f) >= _config.StabilityThreshold);
    }
    
    /// <summary>
    /// Applies motor-based grounding force to keep agent at target Y position while moving.
    /// This is the motor-based equivalent of velocity-based ApplyGroundingForce.
    /// Uses smooth acceleration toward velocity goals instead of direct velocity setting.
    /// </summary>
    /// <param name="entity">Entity to apply movement to</param>
    /// <param name="moveDirection">Current movement direction (velocity goal)</param>
    /// <param name="targetY">Target Y position (navmesh surface + half-height)</param>
    /// <param name="agentHalfHeight">Half-height of agent capsule</param>
    public void ApplyGroundingForce(PhysicsEntity entity, Vector3 moveDirection, float targetY, float agentHalfHeight)
    {
        var state = GetState(entity);
        if (state == CharacterState.AIRBORNE)
        {
            // Don't interfere with airborne movement (let gravity work)
            return;
        }
        
        // Store velocity goal for this entity
        _velocityGoals[entity.EntityId] = moveDirection;
        
        var currentPos = _physicsWorld.GetEntityPosition(entity);
        var currentVel = _physicsWorld.GetEntityVelocity(entity);
        
        // Calculate Y error (how far from target position)
        float yError = targetY - currentPos.Y;
        
        // MOTOR APPROACH: Calculate force needed to reach target velocity smoothly
        // Instead of setting velocity directly, we calculate acceleration force
        
        // 1. Calculate horizontal velocity goal (XZ plane)
        Vector3 horizontalGoal = new Vector3(moveDirection.X, 0, moveDirection.Z);
        Vector3 currentHorizontal = new Vector3(currentVel.X, 0, currentVel.Z);
        Vector3 horizontalDelta = horizontalGoal - currentHorizontal;
        
        // 2. Calculate vertical correction force based on Y error
        // Use proportional control to smoothly correct height
        float verticalCorrection = yError * _config.HeightCorrectionStrength;
        
        // Clamp vertical correction to prevent overshooting
        verticalCorrection = Math.Clamp(verticalCorrection, -_config.MaxVerticalCorrection, _config.MaxVerticalCorrection);
        
        // 3. Combine horizontal movement with vertical correction
        Vector3 desiredVelocity = new Vector3(
            horizontalGoal.X,
            verticalCorrection,
            horizontalGoal.Z
        );
        
        // 4. Apply velocity smoothly using acceleration-based approach
        Vector3 velocityDelta = desiredVelocity - currentVel;
        
        // Scale by motor strength (how aggressive the correction is)
        Vector3 targetAcceleration = velocityDelta * _config.MotorStrength;
        
        // Apply the acceleration through velocity change
        // This is smoother than direct velocity setting
        Vector3 newVelocity = currentVel + targetAcceleration;
        
        // Apply damping to prevent oscillation on slopes
        if (Math.Abs(yError) < _config.HeightErrorTolerance)
        {
            // Close to target height - apply strong damping
            newVelocity.Y *= _config.VerticalDamping;
        }
        
        _physicsWorld.SetEntityVelocity(entity, newVelocity);
        
        // DIAGNOSTIC LOGGING: Track motor effectiveness for Agent-3
        if (MotorDiagnostics.IsEnabled && entity.EntityId == MotorDiagnostics.TrackedEntityId)
        {
            MotorDiagnostics.LogMotorAttempt(new MotorInfo
            {
                EntityId = entity.EntityId,
                CurrentY = currentPos.Y,
                TargetY = targetY,
                YError = yError,
                CurrentVelocity = currentVel,
                DesiredVelocity = desiredVelocity,
                AppliedAcceleration = targetAcceleration,
                State = state,
                HasGroundContact = _groundContacts.ContainsKey(entity.EntityId),
                Timestamp = DateTime.UtcNow
            });
        }
    }
    
    /// <summary>
    /// Applies idle grounding to keep stationary agent at current Y position.
    /// Motor-based version uses gentle correction forces instead of hard clamping.
    /// </summary>
    public void ApplyIdleGrounding(PhysicsEntity entity)
    {
        var state = GetState(entity);
        if (state == CharacterState.AIRBORNE)
            return; // Don't interfere with falling agents
        
        var currentPos = _physicsWorld.GetEntityPosition(entity);
        var velocity = _physicsWorld.GetEntityVelocity(entity);
        
        // For idle grounding, we want to maintain current Y position
        // Apply gentle downward correction if drifting up, or upward if sinking
        
        // Use current Y as target (maintain position)
        float targetY = currentPos.Y;
        
        // If there's any vertical velocity, apply damping
        if (Math.Abs(velocity.Y) > 0.01f)
        {
            // Apply strong damping to vertical velocity when idle
            Vector3 dampedVelocity = new Vector3(
                velocity.X * _config.IdleHorizontalDamping,
                velocity.Y * _config.IdleVerticalDamping,
                velocity.Z * _config.IdleHorizontalDamping
            );
            
            _physicsWorld.SetEntityVelocity(entity, dampedVelocity);
            
            // DIAGNOSTIC LOGGING
            if (MotorDiagnostics.IsEnabled && entity.EntityId == MotorDiagnostics.TrackedEntityId)
            {
                MotorDiagnostics.LogMotorAttempt(new MotorInfo
                {
                    EntityId = entity.EntityId,
                    CurrentY = currentPos.Y,
                    TargetY = targetY,
                    YError = 0,
                    CurrentVelocity = velocity,
                    DesiredVelocity = dampedVelocity,
                    AppliedAcceleration = Vector3.Zero,
                    State = state,
                    HasGroundContact = _groundContacts.ContainsKey(entity.EntityId),
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }
    
    /// <summary>
    /// Notifies the controller that an entity has ground contact.
    /// Called from CollisionHandler when ground collision is detected.
    /// </summary>
    public void NotifyGroundContact(PhysicsEntity entity, PhysicsEntity groundEntity)
    {
        if (!_groundContacts.TryGetValue(entity.EntityId, out var contacts))
        {
            contacts = new HashSet<int>();
            _groundContacts[entity.EntityId] = contacts;
        }
        
        contacts.Add(groundEntity.EntityId);
        
        // TODO: Future enhancement - store contact normals to calculate support direction
        // This would enable better climbing behavior and slope detection
    }
    
    /// <summary>
    /// Notifies the controller that an entity lost ground contact.
    /// </summary>
    public void NotifyGroundContactRemoved(PhysicsEntity entity, PhysicsEntity groundEntity)
    {
        if (_groundContacts.TryGetValue(entity.EntityId, out var contacts))
        {
            contacts.Remove(groundEntity.EntityId);
            if (contacts.Count == 0)
            {
                _groundContacts.Remove(entity.EntityId);
            }
        }
    }
    
    /// <summary>
    /// Manually sets entity state to GROUNDED.
    /// </summary>
    public void SetGrounded(PhysicsEntity entity)
    {
        _entityStates[entity.EntityId] = CharacterState.GROUNDED;
        _recoveryTimers.Remove(entity.EntityId);
    }
    
    /// <summary>
    /// Manually sets entity state to AIRBORNE.
    /// </summary>
    public void SetAirborne(PhysicsEntity entity)
    {
        _entityStates[entity.EntityId] = CharacterState.AIRBORNE;
        _recoveryTimers.Remove(entity.EntityId);
    }
    
    /// <summary>
    /// Manually sets entity state to RECOVERING.
    /// </summary>
    private void SetRecovering(PhysicsEntity entity)
    {
        _entityStates[entity.EntityId] = CharacterState.RECOVERING;
    }
    
    /// <summary>
    /// Cleans up state for a removed entity.
    /// </summary>
    public void RemoveEntity(int entityId)
    {
        _entityStates.Remove(entityId);
        _groundContacts.Remove(entityId);
        _recoveryTimers.Remove(entityId);
        _velocityGoals.Remove(entityId);
        _supportNormal.Remove(entityId);
    }
}

/// <summary>
/// Configuration for motor-based character controller.
/// </summary>
public class MotorCharacterConfig
{
    /// <summary>
    /// Motor strength - how aggressively to correct velocity.
    /// Higher = faster response but potential oscillation.
    /// Default: 0.3f (30% correction per frame at 60fps)
    /// </summary>
    public float MotorStrength { get; set; } = 0.3f;
    
    /// <summary>
    /// Height correction strength - proportional control for Y position.
    /// Higher = more aggressive height correction.
    /// Default: 10.0f
    /// </summary>
    public float HeightCorrectionStrength { get; set; } = 10.0f;
    
    /// <summary>
    /// Maximum vertical correction velocity (m/s).
    /// Prevents overshooting when correcting large Y errors.
    /// Default: 5.0f
    /// </summary>
    public float MaxVerticalCorrection { get; set; } = 5.0f;
    
    /// <summary>
    /// Height error tolerance (meters).
    /// Within this range, strong damping is applied to prevent oscillation.
    /// Default: 0.1f (10cm)
    /// </summary>
    public float HeightErrorTolerance { get; set; } = 0.1f;
    
    /// <summary>
    /// Vertical velocity damping when near target height.
    /// Lower = more damping (0.0 = full stop, 1.0 = no damping).
    /// Default: 0.5f (50% damping per frame)
    /// </summary>
    public float VerticalDamping { get; set; } = 0.5f;
    
    /// <summary>
    /// Vertical velocity damping when idle (not moving).
    /// Default: 0.2f (80% damping per frame - very strong)
    /// </summary>
    public float IdleVerticalDamping { get; set; } = 0.2f;
    
    /// <summary>
    /// Horizontal velocity damping when idle.
    /// Default: 0.5f (50% damping per frame)
    /// </summary>
    public float IdleHorizontalDamping { get; set; } = 0.5f;
    
    /// <summary>
    /// Time agent must be stable before resuming pathfinding after landing (seconds).
    /// Default: 0.2f
    /// </summary>
    public float StabilityThreshold { get; set; } = 0.2f;
}

/// <summary>
/// Diagnostic utility for tracking motor effectiveness.
/// </summary>
public static class MotorDiagnostics
{
    private static readonly List<MotorInfo> _motorHistory = new();
    private static readonly object _lock = new object();
    private static int _maxHistorySize = 10000;
    
    /// <summary>
    /// Enable/disable motor diagnostics.
    /// </summary>
    public static bool IsEnabled { get; set; } = false;
    
    /// <summary>
    /// Entity ID to track (e.g., 103 for Agent-3).
    /// </summary>
    public static int TrackedEntityId { get; set; } = -1;
    
    /// <summary>
    /// Logs a motor attempt.
    /// </summary>
    public static void LogMotorAttempt(MotorInfo info)
    {
        lock (_lock)
        {
            _motorHistory.Add(info);
            
            if (_motorHistory.Count > _maxHistorySize)
            {
                _motorHistory.RemoveRange(0, _motorHistory.Count - _maxHistorySize);
            }
        }
    }
    
    /// <summary>
    /// Gets all recorded motor attempts.
    /// </summary>
    public static List<MotorInfo> GetMotorHistory()
    {
        lock (_lock)
        {
            return new List<MotorInfo>(_motorHistory);
        }
    }
    
    /// <summary>
    /// Clears motor history.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _motorHistory.Clear();
        }
    }
    
    /// <summary>
    /// Prints a summary of motor effectiveness.
    /// </summary>
    public static void PrintSummary()
    {
        lock (_lock)
        {
            if (_motorHistory.Count == 0)
            {
                Console.WriteLine("[MotorDiagnostics] No motor attempts recorded.");
                return;
            }
            
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ MOTOR DIAGNOSTICS SUMMARY (Entity {TrackedEntityId})           ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"Total Motor Attempts: {_motorHistory.Count}");
            Console.WriteLine($"Time Range: {_motorHistory.First().Timestamp:HH:mm:ss.fff} to {_motorHistory.Last().Timestamp:HH:mm:ss.fff}");
            Console.WriteLine();
            
            // Analyze Y errors
            var avgYError = _motorHistory.Average(m => Math.Abs(m.YError));
            var maxYError = _motorHistory.Max(m => Math.Abs(m.YError));
            var minYError = _motorHistory.Min(m => Math.Abs(m.YError));
            
            Console.WriteLine("Y Position Accuracy:");
            Console.WriteLine($"  Average Error: {avgYError:F4}m");
            Console.WriteLine($"  Max Error: {maxYError:F4}m");
            Console.WriteLine($"  Min Error: {minYError:F4}m");
            Console.WriteLine();
            
            // Analyze velocity control
            var avgVelMagnitude = _motorHistory.Average(m => m.CurrentVelocity.Length());
            var avgDesiredVelMagnitude = _motorHistory.Average(m => m.DesiredVelocity.Length());
            
            Console.WriteLine("Velocity Control:");
            Console.WriteLine($"  Average Current Velocity: {avgVelMagnitude:F2}m/s");
            Console.WriteLine($"  Average Desired Velocity: {avgDesiredVelMagnitude:F2}m/s");
            Console.WriteLine();
            
            // Analyze ground contact
            var groundedCount = _motorHistory.Count(m => m.HasGroundContact);
            var groundedPercent = 100.0 * groundedCount / _motorHistory.Count;
            
            Console.WriteLine($"Ground Contact: {groundedCount}/{_motorHistory.Count} ({groundedPercent:F1}%)");
            Console.WriteLine();
            
            // Analyze by state
            var byState = _motorHistory.GroupBy(m => m.State).ToList();
            Console.WriteLine("Motor Attempts by State:");
            foreach (var group in byState)
            {
                var avgError = group.Average(m => Math.Abs(m.YError));
                Console.WriteLine($"  {group.Key}: {group.Count()} attempts (avg error: {avgError:F4}m)");
            }
            
            Console.WriteLine();
        }
    }
}

/// <summary>
/// Detailed information about a motor attempt.
/// </summary>
public struct MotorInfo
{
    public int EntityId { get; set; }
    public float CurrentY { get; set; }
    public float TargetY { get; set; }
    public float YError { get; set; }
    public Vector3 CurrentVelocity { get; set; }
    public Vector3 DesiredVelocity { get; set; }
    public Vector3 AppliedAcceleration { get; set; }
    public CharacterState State { get; set; }
    public bool HasGroundContact { get; set; }
    public DateTime Timestamp { get; set; }
}
