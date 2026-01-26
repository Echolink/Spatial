using Spatial.Physics;
using System.Numerics;
using System.Collections.Generic;

namespace Spatial.Integration;

/// <summary>
/// Character controller that coordinates pathfinding and physics.
/// Manages agent states (GROUNDED/AIRBORNE/RECOVERING) and applies grounding forces.
/// 
/// This enables Minecraft-style behavior: agents can follow paths while also
/// responding to gravity, knockback, falling, and collisions.
/// </summary>
public class CharacterController : ICharacterController
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly CharacterControllerConfig _config;
    
    // Track state for each entity
    private readonly Dictionary<int, CharacterState> _entityStates = new();
    
    // Track ground contacts for each entity
    private readonly Dictionary<int, HashSet<int>> _groundContacts = new(); // entityId -> set of ground entity IDs
    
    // Track recovery timers
    private readonly Dictionary<int, float> _recoveryTimers = new(); // entityId -> time since landing
    
    /// <summary>
    /// Creates a new character controller.
    /// </summary>
    public CharacterController(PhysicsWorld physicsWorld, CharacterControllerConfig? config = null)
    {
        _physicsWorld = physicsWorld;
        _config = config ?? new CharacterControllerConfig();
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
    /// Updates the grounded state for an entity based on physics.
    /// Call this every frame for each entity.
    /// </summary>
    public void UpdateGroundedState(PhysicsEntity entity, float deltaTime)
    {
        var velocity = _physicsWorld.GetEntityVelocity(entity);
        var position = _physicsWorld.GetEntityPosition(entity);
        
        var currentState = GetState(entity);
        
        // Check if grounded using velocity-based detection
        bool isGroundedByVelocity = Math.Abs(velocity.Y) < _config.GroundedVelocityThreshold;
        
        // Check if we have ground contacts
        bool hasGroundContacts = _groundContacts.TryGetValue(entity.EntityId, out var contacts) && contacts.Count > 0;
        
        // Determine if agent is actually on ground
        bool isOnGround = isGroundedByVelocity && hasGroundContacts;
        
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
                }
                break;
        }
    }
    
    /// <summary>
    /// Checks if an entity is stable (ready to resume pathfinding after landing).
    /// </summary>
    public bool IsStable(PhysicsEntity entity)
    {
        if (!IsRecovering(entity))
            return false;
        
        if (!_recoveryTimers.TryGetValue(entity.EntityId, out var recoveryTime))
            return false;
        
        return recoveryTime >= _config.StabilityThreshold;
    }
    
    /// <summary>
    /// Applies grounding force to keep agent at target Y position.
    /// Used for slope navigation to keep agent pressed against terrain.
    /// 
    /// CRITICAL FIX: Uses direct position correction with aggressive clamping.
    /// Always forces agent to exact target Y to prevent any sinking.
    /// Works in ALL states (GROUNDED, RECOVERING) to prevent fall-through.
    /// </summary>
    /// <param name="entity">Entity to apply grounding to</param>
    /// <param name="moveDirection">Current movement direction (unused but kept for API compat)</param>
    /// <param name="targetY">Target Y position (navmesh surface + half-height)</param>
    /// <param name="agentHalfHeight">Half-height of agent capsule (length/2 + radius)</param>
    public void ApplyGroundingForce(PhysicsEntity entity, Vector3 moveDirection, float targetY, float agentHalfHeight)
    {
        // CRITICAL: Do NOT early return for non-grounded states!
        // We need to apply grounding even when recovering to prevent further sinking
        var state = GetState(entity);
        if (state == CharacterState.AIRBORNE)
            return; // Only skip if truly airborne (no ground contact)
        
        var currentPos = _physicsWorld.GetEntityPosition(entity);
        var velocity = _physicsWorld.GetEntityVelocity(entity);
        
        // Calculate Y error (how far from target position)
        float yError = targetY - currentPos.Y;
        
        // DIAGNOSTIC LOGGING: Track grounding effectiveness for Agent-3
        if (GroundingDiagnostics.IsEnabled && entity.EntityId == GroundingDiagnostics.TrackedEntityId)
        {
            GroundingDiagnostics.LogGroundingAttempt(new GroundingInfo
            {
                EntityId = entity.EntityId,
                CurrentY = currentPos.Y,
                TargetY = targetY,
                YError = yError,
                YVelocity = velocity.Y,
                State = state,
                WasCorrected = Math.Abs(yError) > 0.01f,
                Timestamp = DateTime.UtcNow
            });
        }
        
        // AGGRESSIVE FIX: Always snap to exact Y position every frame
        // This prevents ANY sinking from gravity between frames
        if (Math.Abs(yError) > 0.01f) // More than 1cm off - correct immediately
        {
            // Directly set Y position to target (keep X and Z unchanged)
            var correctedPosition = new Vector3(currentPos.X, targetY, currentPos.Z);
            _physicsWorld.SetEntityPosition(entity, correctedPosition);
            
            // CRITICAL: Zero out Y velocity completely
            // This prevents gravity from accumulating downward velocity
            _physicsWorld.SetEntityVelocity(entity, new Vector3(velocity.X, 0, velocity.Z));
        }
        else
        {
            // Even if position is correct, zero Y velocity to prevent future sinking
            if (Math.Abs(velocity.Y) > 0.01f)
            {
                _physicsWorld.SetEntityVelocity(entity, new Vector3(velocity.X, 0, velocity.Z));
            }
        }
    }
    
    /// <summary>
    /// Applies idle grounding to keep stationary agent at current Y position.
    /// Prevents sinking through ground when not actively moving.
    /// Should be called every frame for agents without active movement.
    /// </summary>
    /// <param name="entity">Entity to apply idle grounding to</param>
    public void ApplyIdleGrounding(PhysicsEntity entity)
    {
        var state = GetState(entity);
        if (state == CharacterState.AIRBORNE)
            return; // Don't interfere with falling agents
        
        var currentPos = _physicsWorld.GetEntityPosition(entity);
        var velocity = _physicsWorld.GetEntityVelocity(entity);
        
        // Keep agent at current Y by zeroing vertical velocity and preventing any Y drift
        if (Math.Abs(velocity.Y) > 0.01f)
        {
            // Zero Y velocity to prevent sinking
            _physicsWorld.SetEntityVelocity(entity, new Vector3(velocity.X, 0, velocity.Z));
            
            // DIAGNOSTIC LOGGING: Track idle grounding for Agent-3
            if (GroundingDiagnostics.IsEnabled && entity.EntityId == GroundingDiagnostics.TrackedEntityId)
            {
                GroundingDiagnostics.LogGroundingAttempt(new GroundingInfo
                {
                    EntityId = entity.EntityId,
                    CurrentY = currentPos.Y,
                    TargetY = currentPos.Y,
                    YError = 0,
                    YVelocity = velocity.Y,
                    State = state,
                    WasCorrected = true,
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
    }
    
    /// <summary>
    /// Notifies the controller that an entity lost ground contact.
    /// Called from CollisionHandler when ground collision ends.
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
    /// Checks if agent has deviated too far from navmesh vertically.
    /// Used to detect falling through or getting stuck.
    /// DEPRECATED: Use MovementController.IsOnCorrectFloor instead.
    /// </summary>
    [Obsolete("Use MovementController.IsOnCorrectFloor with PathfindingConfiguration.FloorLevelTolerance instead")]
    public bool HasDeviatedFromNavmesh(PhysicsEntity entity, float navmeshY, float agentHalfHeight)
    {
        var position = _physicsWorld.GetEntityPosition(entity);
        float expectedY = navmeshY + agentHalfHeight;
        float yDelta = Math.Abs(position.Y - expectedY);
        
        #pragma warning disable CS0618 // Type or member is obsolete
        return yDelta > _config.MaxNavmeshDeviation;
        #pragma warning restore CS0618 // Type or member is obsolete
    }
    
    /// <summary>
    /// Cleans up state for a removed entity.
    /// </summary>
    public void RemoveEntity(int entityId)
    {
        _entityStates.Remove(entityId);
        _groundContacts.Remove(entityId);
        _recoveryTimers.Remove(entityId);
    }
}

/// <summary>
/// Diagnostic utility for tracking grounding force effectiveness.
/// Enable this to measure how well grounding corrections work vs gravity.
/// </summary>
public static class GroundingDiagnostics
{
    private static readonly List<GroundingInfo> _groundingHistory = new();
    private static readonly object _lock = new object();
    private static int _maxHistorySize = 10000;
    
    /// <summary>
    /// Enable/disable grounding diagnostics.
    /// </summary>
    public static bool IsEnabled { get; set; } = false;
    
    /// <summary>
    /// Entity ID to track (e.g., 103 for Agent-3).
    /// </summary>
    public static int TrackedEntityId { get; set; } = -1;
    
    /// <summary>
    /// Logs a grounding attempt.
    /// </summary>
    public static void LogGroundingAttempt(GroundingInfo info)
    {
        lock (_lock)
        {
            _groundingHistory.Add(info);
            
            // Trim history if it gets too large
            if (_groundingHistory.Count > _maxHistorySize)
            {
                _groundingHistory.RemoveRange(0, _groundingHistory.Count - _maxHistorySize);
            }
        }
    }
    
    /// <summary>
    /// Gets all recorded grounding attempts.
    /// </summary>
    public static List<GroundingInfo> GetGroundingHistory()
    {
        lock (_lock)
        {
            return new List<GroundingInfo>(_groundingHistory);
        }
    }
    
    /// <summary>
    /// Clears grounding history.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _groundingHistory.Clear();
        }
    }
    
    /// <summary>
    /// Prints a summary of grounding effectiveness.
    /// </summary>
    public static void PrintSummary()
    {
        lock (_lock)
        {
            if (_groundingHistory.Count == 0)
            {
                Console.WriteLine("[GroundingDiagnostics] No grounding attempts recorded.");
                return;
            }
            
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ GROUNDING DIAGNOSTICS SUMMARY (Entity {TrackedEntityId})       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"Total Grounding Attempts: {_groundingHistory.Count}");
            Console.WriteLine($"Time Range: {_groundingHistory.First().Timestamp:HH:mm:ss.fff} to {_groundingHistory.Last().Timestamp:HH:mm:ss.fff}");
            Console.WriteLine();
            
            // Analyze corrections
            var corrected = _groundingHistory.Where(g => g.WasCorrected).ToList();
            var noCorrectionNeeded = _groundingHistory.Where(g => !g.WasCorrected).ToList();
            
            Console.WriteLine($"Position Corrections Made: {corrected.Count} ({100.0 * corrected.Count / _groundingHistory.Count:F1}%)");
            Console.WriteLine($"No Correction Needed: {noCorrectionNeeded.Count} ({100.0 * noCorrectionNeeded.Count / _groundingHistory.Count:F1}%)");
            Console.WriteLine();
            
            if (corrected.Any())
            {
                Console.WriteLine("Correction Statistics:");
                Console.WriteLine($"  Average Y Error: {corrected.Average(g => Math.Abs(g.YError)):F4}m");
                Console.WriteLine($"  Max Y Error: {corrected.Max(g => Math.Abs(g.YError)):F4}m");
                Console.WriteLine($"  Average Y Velocity (before correction): {corrected.Average(g => g.YVelocity):F4}m/s");
                Console.WriteLine($"  Min Y Velocity: {corrected.Min(g => g.YVelocity):F4}m/s");
                
                // Check if errors are increasing over time (sign of losing battle with gravity)
                var firstHalf = corrected.Take(corrected.Count / 2).ToList();
                var secondHalf = corrected.Skip(corrected.Count / 2).ToList();
                
                if (firstHalf.Any() && secondHalf.Any())
                {
                    var firstHalfAvgError = firstHalf.Average(g => Math.Abs(g.YError));
                    var secondHalfAvgError = secondHalf.Average(g => Math.Abs(g.YError));
                    var errorIncrease = secondHalfAvgError - firstHalfAvgError;
                    
                    Console.WriteLine();
                    if (errorIncrease > 0.001f)
                    {
                        Console.WriteLine($"⚠️ Y Error Increasing Over Time:");
                        Console.WriteLine($"   First half average: {firstHalfAvgError:F4}m");
                        Console.WriteLine($"   Second half average: {secondHalfAvgError:F4}m");
                        Console.WriteLine($"   Increase: +{errorIncrease:F4}m ({errorIncrease / firstHalfAvgError * 100:F1}%)");
                        Console.WriteLine($"   ⚠️ This suggests grounding is losing the fight against gravity!");
                    }
                    else
                    {
                        Console.WriteLine($"✅ Y Error Stable or Decreasing:");
                        Console.WriteLine($"   First half average: {firstHalfAvgError:F4}m");
                        Console.WriteLine($"   Second half average: {secondHalfAvgError:F4}m");
                    }
                }
            }
            
            // Analyze by state
            var byState = _groundingHistory.GroupBy(g => g.State).ToList();
            Console.WriteLine();
            Console.WriteLine("Grounding Attempts by State:");
            foreach (var group in byState)
            {
                var correctionRate = group.Count(g => g.WasCorrected) / (float)group.Count() * 100;
                Console.WriteLine($"  {group.Key}: {group.Count()} attempts ({correctionRate:F1}% required correction)");
            }
            
            Console.WriteLine();
        }
    }
}

/// <summary>
/// Detailed information about a grounding attempt.
/// </summary>
public struct GroundingInfo
{
    public int EntityId { get; set; }
    public float CurrentY { get; set; }
    public float TargetY { get; set; }
    public float YError { get; set; }
    public float YVelocity { get; set; }
    public CharacterState State { get; set; }
    public bool WasCorrected { get; set; }
    public DateTime Timestamp { get; set; }
}
