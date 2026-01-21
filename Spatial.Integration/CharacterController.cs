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
public class CharacterController
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
    /// Applies grounding force to keep agent stable on ground.
    /// Call this when agent is GROUNDED and following a path.
    /// </summary>
    /// <param name="entity">Entity to apply grounding to</param>
    /// <param name="moveDirection">Current movement direction</param>
    /// <param name="targetY">Target Y position (navmesh surface + half-height)</param>
    /// <param name="agentHalfHeight">Half-height of agent capsule (length/2 + radius)</param>
    public void ApplyGroundingForce(PhysicsEntity entity, Vector3 moveDirection, float targetY, float agentHalfHeight)
    {
        if (!IsGrounded(entity))
            return;
        
        var currentPos = _physicsWorld.GetEntityPosition(entity);
        var velocity = _physicsWorld.GetEntityVelocity(entity);
        
        // FIXED: Actively correct Y position to prevent sinking into ground
        // Calculate Y error (how far from expected position)
        float yError = targetY - currentPos.Y;
        
        // If agent has sunk significantly (more than tolerance), apply corrective upward force
        if (yError > 0.05f) // More than 5cm below expected
        {
            // Apply strong upward impulse to correct position
            float correctionForce = yError * _config.GroundingForce * 2.0f; // Proportional to error
            var upwardImpulse = new Vector3(0, correctionForce * 0.016f, 0);
            _physicsWorld.ApplyLinearImpulse(entity, upwardImpulse);
        }
        else if (yError < -0.05f) // More than 5cm above expected
        {
            // Apply gentle downward force to settle
            var downwardImpulse = new Vector3(0, yError * _config.GroundingForce * 0.5f * 0.016f, 0);
            _physicsWorld.ApplyLinearImpulse(entity, downwardImpulse);
        }
        
        // CRITICAL: Cancel ANY upward velocity when grounded to prevent collision displacement
        // This prevents agents from being launched upward during agent-agent collisions
        if (velocity.Y > 0.01f) // Any upward motion > 1cm/s
        {
            var clampedVelocity = new Vector3(velocity.X, 0, velocity.Z);
            _physicsWorld.SetEntityVelocity(entity, clampedVelocity);
        }
        
        // Also clamp excessive downward velocity
        else if (velocity.Y < -0.5f)
        {
            var clampedVelocity = new Vector3(velocity.X, -0.5f, velocity.Z);
            _physicsWorld.SetEntityVelocity(entity, clampedVelocity);
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
    /// </summary>
    public bool HasDeviatedFromNavmesh(PhysicsEntity entity, float navmeshY, float agentHalfHeight)
    {
        var position = _physicsWorld.GetEntityPosition(entity);
        float expectedY = navmeshY + agentHalfHeight;
        float yDelta = Math.Abs(position.Y - expectedY);
        
        return yDelta > _config.MaxNavmeshDeviation;
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
