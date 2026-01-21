using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Integration.Events;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;

namespace Spatial.Integration;

/// <summary>
/// Controls entity movement using both physics and pathfinding.
/// 
/// Enhanced features:
/// - Event callbacks for destination reached, path blocked, replanning, etc.
/// - Path validation with automatic replanning
/// - Local avoidance integration for dynamic obstacles
/// - Configurable behavior parameters
/// 
/// Flow:
/// 1. Receives target position from game server
/// 2. Uses Spatial.Pathfinding to find valid path
/// 3. Applies movement to Spatial.Physics bodies along path
/// 4. Validates path periodically and replans if blocked
/// 5. Applies local avoidance for nearby entities
/// 6. Returns final position to game server via events
/// </summary>
public class MovementController
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly Pathfinder _pathfinder;
    private readonly PathValidator _pathValidator;
    private readonly LocalAvoidance _localAvoidance;
    private readonly PathfindingConfiguration _config;
    private readonly CharacterController _characterController;
    
    private readonly Dictionary<int, MovementState> _movementStates = new();
    
    /// <summary>
    /// Event fired when an entity reaches its destination
    /// </summary>
    public event Action<int, Vector3>? OnDestinationReached;
    
    /// <summary>
    /// Event fired when a path becomes blocked
    /// </summary>
    public event Action<int>? OnPathBlocked;
    
    /// <summary>
    /// Event fired when a path is replanned
    /// </summary>
    public event Action<int>? OnPathReplanned;
    
    /// <summary>
    /// Event fired to report movement progress
    /// </summary>
    public event Action<int, float>? OnMovementProgress;
    
    /// <summary>
    /// Event fired when movement starts
    /// </summary>
    public event Action<int, Vector3, Vector3>? OnMovementStarted;
    
    /// <summary>
    /// Gets the character state for an entity (for testing/debugging).
    /// </summary>
    public CharacterState GetCharacterState(PhysicsEntity entity)
    {
        return _characterController.GetState(entity);
    }
    
    /// <summary>
    /// Creates a new movement controller.
    /// </summary>
    public MovementController(PhysicsWorld physicsWorld, Pathfinder pathfinder, PathfindingConfiguration? config = null, CharacterControllerConfig? characterConfig = null)
    {
        _physicsWorld = physicsWorld;
        _pathfinder = pathfinder;
        _config = config ?? new PathfindingConfiguration();
        _pathValidator = new PathValidator(physicsWorld);
        _localAvoidance = new LocalAvoidance(physicsWorld, _config.LocalAvoidanceRadius);
        
        // Create character controller for physics-pathfinding integration
        _characterController = new CharacterController(physicsWorld, characterConfig);
        
        // Register ground contact callbacks with physics world
        // Note: This requires PhysicsWorld to be created with these callbacks for best performance
        // For now, we'll register them here (they may not work if PhysicsWorld was created earlier)
        physicsWorld.RegisterGroundContactCallbacks(
            onGroundContact: (dynamicEntity, groundEntity) => 
                _characterController.NotifyGroundContact(dynamicEntity, groundEntity),
            onGroundContactRemoved: (dynamicEntity, groundEntity) => 
                _characterController.NotifyGroundContactRemoved(dynamicEntity, groundEntity)
        );
    }
    
    /// <summary>
    /// Requests movement for an entity to a target position.
    /// Uses pathfinding to find a valid path, then applies physics-based movement.
    /// </summary>
    /// <param name="request">Movement request</param>
    /// <returns>True if movement was initiated successfully</returns>
    public bool RequestMovement(MovementRequest request)
    {
        var entity = _physicsWorld.EntityRegistry.GetEntityById(request.EntityId);
        if (entity == null)
            return false;
        
        // Get current position
        var currentPosition = _physicsWorld.GetEntityPosition(entity);
        
        Console.WriteLine($"[MovementController] Requesting movement for entity {request.EntityId}");
        Console.WriteLine($"[MovementController] Current position: ({currentPosition.X:F2}, {currentPosition.Y:F2}, {currentPosition.Z:F2})");
        Console.WriteLine($"[MovementController] Target position: ({request.TargetPosition.X:F2}, {request.TargetPosition.Y:F2}, {request.TargetPosition.Z:F2})");
        
        // Find path using pathfinding
        var extents = new Vector3(5.0f, 10.0f, 5.0f);
        var pathResult = _pathfinder.FindPath(currentPosition, request.TargetPosition, extents);
        
        Console.WriteLine($"[MovementController] Pathfinding result: Success={pathResult.Success}");
        if (pathResult.Success)
        {
            Console.WriteLine($"[MovementController] Path has {pathResult.Waypoints.Count} waypoints, length={pathResult.TotalLength:F2}");
        }
        
        if (!pathResult.Success)
            return false;
        
        // Store movement state
        var state = new MovementState
        {
            EntityId = request.EntityId,
            TargetPosition = request.TargetPosition,
            MaxSpeed = request.MaxSpeed,
            AgentHeight = request.AgentHeight,
            AgentRadius = request.AgentRadius,
            Waypoints = pathResult.Waypoints.ToList(),
            CurrentWaypointIndex = FindNextValidWaypoint(pathResult.Waypoints, currentPosition, 0),
            LastValidationTime = 0f,
            LastReplanTime = DateTime.MinValue,
            StartTime = DateTime.UtcNow,
            TotalDistance = pathResult.TotalLength
        };
        
        _movementStates[request.EntityId] = state;
        
        // Fire movement started event
        OnMovementStarted?.Invoke(request.EntityId, currentPosition, request.TargetPosition);
        
        // Start moving toward first valid waypoint
        if (state.CurrentWaypointIndex < state.Waypoints.Count)
        {
            MoveTowardWaypoint(entity, state.Waypoints[state.CurrentWaypointIndex], currentPosition, request.MaxSpeed);
        }
        
        return true;
    }
    
    /// <summary>
    /// Updates movement for all entities.
    /// Call this every frame to continue movement along paths.
    /// </summary>
    public void UpdateMovement(float deltaTime)
    {
        var entitiesToUpdate = _movementStates.Keys.ToList();
        
        foreach (var entityId in entitiesToUpdate)
        {
            var entity = _physicsWorld.EntityRegistry.GetEntityById(entityId);
            if (entity == null)
            {
                _movementStates.Remove(entityId);
                continue;
            }
            
            var state = _movementStates[entityId];
            UpdateEntityMovement(entity, state, deltaTime);
        }
    }
    
    /// <summary>
    /// Updates movement for a single entity.
    /// </summary>
    private void UpdateEntityMovement(PhysicsEntity entity, MovementState state, float deltaTime)
    {
        var currentPosition = _physicsWorld.GetEntityPosition(entity);
        
        // Calculate agent half-height once (used throughout)
        float agentHalfHeight = (state.AgentHeight * 0.5f) + state.AgentRadius;
        
        // Update character controller state (grounded/airborne detection)
        var previousState = _characterController.GetState(entity);
        _characterController.UpdateGroundedState(entity, deltaTime);
        var currentState = _characterController.GetState(entity);
        
        // Detect state transitions
        if (previousState == CharacterState.AIRBORNE && currentState == CharacterState.RECOVERING)
        {
            Console.WriteLine($"[MovementController] Entity {entity.EntityId} landed, recovering...");
        }
        
        // If movement is completed, only apply Y correction to prevent sinking
        if (state.IsCompleted)
        {
            // Keep applying Y correction even when stationary
            if (_characterController.IsGrounded(entity) && state.Waypoints.Count > 0)
            {
                var lastWaypoint = state.Waypoints[^1];
                float targetY = lastWaypoint.Y + agentHalfHeight;
                
                // Apply Y correction
                float yError = Math.Abs(currentPosition.Y - targetY);
                if (yError > 0.01f)
                {
                    _physicsWorld.SetEntityPosition(entity, new Vector3(currentPosition.X, targetY, currentPosition.Z));
                }
                
                // Keep velocity at zero
                _physicsWorld.SetEntityVelocity(entity, Vector3.Zero);
            }
            return;
        }
        
        // Check if we've completed the path
        if (state.CurrentWaypointIndex >= state.Waypoints.Count)
        {
            CompleteMovement(entity, state);
            return;
        }
        
        // Periodic path validation
        state.LastValidationTime += deltaTime;
        if (state.LastValidationTime >= _config.PathValidationInterval && _config.EnableAutomaticReplanning)
        {
            state.LastValidationTime = 0f;
            ValidateAndReplanIfNeeded(entity, state, currentPosition);
        }
        
        var targetWaypoint = state.Waypoints[state.CurrentWaypointIndex];
        
        // Check if agent has deviated too far from navmesh (fallen through, stuck)
        if (_characterController.HasDeviatedFromNavmesh(entity, targetWaypoint.Y, agentHalfHeight))
        {
            Console.WriteLine($"[MovementController] Agent {entity.EntityId} deviated from navmesh, replanning");
            ReplanPath(entity, state, currentPosition);
            return;
        }
        
        // COLLISION PREDICTION: Check if we're about to collide with another agent
        if (_config.EnableLocalAvoidance && _characterController.IsGrounded(entity))
        {
            var nearbyEntities = _localAvoidance.GetNearbyEntities(
                currentPosition,
                entity.EntityId,
                _config.MaxAvoidanceNeighbors
            );
            
            if (nearbyEntities.Count > 0)
            {
                var currentVel = _physicsWorld.GetEntityVelocity(entity);
                var predictions = _localAvoidance.PredictCollisions(
                    currentPosition,
                    currentVel,
                    targetWaypoint,
                    nearbyEntities
                );
                
                // Check if any prediction requires avoiding
                var criticalCollision = predictions.FirstOrDefault(p => p.ShouldReplan);
                if (criticalCollision != null)
                {
                    // PRIORITY SYSTEM: Lower entity ID takes detour, higher entity ID goes straight
                    // This prevents both agents from taking detours or both going straight
                    bool shouldTakeDetour = entity.EntityId < criticalCollision.OtherEntity.EntityId;
                    
                    if (shouldTakeDetour && !state.HasDetourWaypoint)
                    {
                        // This agent takes a DETOUR - add an offset waypoint to go around
                        var otherPos = _physicsWorld.GetEntityPosition(criticalCollision.OtherEntity);
                        var directionToOther = Vector3.Normalize(otherPos - currentPosition);
                        
                        // Calculate perpendicular offset (go around to the right)
                        var offsetDirection = new Vector3(directionToOther.Z, 0, -directionToOther.X);
                        var detourPoint = otherPos + offsetDirection * 3.0f; // 3 meters to the side
                        
                        // CRITICAL FIX: Use navmesh Y coordinate for detour, not agent's current Y
                        // This prevents agents from trying to reach elevated waypoints
                        detourPoint = new Vector3(detourPoint.X, targetWaypoint.Y, detourPoint.Z);
                        
                        Console.WriteLine($"[MovementController] Agent {entity.EntityId} taking DETOUR around Agent {criticalCollision.OtherEntity.EntityId}");
                        Console.WriteLine($"[MovementController] Detour point: ({detourPoint.X:F2}, {detourPoint.Y:F2}, {detourPoint.Z:F2})");
                        
                        // Insert detour waypoint before final destination
                        var finalDestination = state.Waypoints[^1];
                        state.Waypoints.Clear();
                        state.Waypoints.Add(detourPoint);
                        state.Waypoints.Add(finalDestination);
                        state.CurrentWaypointIndex = 0;
                        state.HasDetourWaypoint = true;
                        state.IsAvoidingCollision = false;
                        
                        return; // Recompute movement next frame with new waypoint
                    }
                    else if (!shouldTakeDetour)
                    {
                        // This agent has priority - continue but slow down slightly
                        state.IsAvoidingCollision = true;
                    }
                }
                else
                {
                    state.IsAvoidingCollision = false;
                    // If we passed the detour point, remove the flag
                    if (state.HasDetourWaypoint && state.CurrentWaypointIndex > 0)
                    {
                        state.HasDetourWaypoint = false;
                    }
                }
            }
        }
        
        // Check if we've reached the current waypoint (XZ distance only)
        var xzDistanceToWaypoint = CalculateXZDistance(currentPosition, targetWaypoint);
        
        var threshold = state.CurrentWaypointIndex == state.Waypoints.Count - 1 
            ? _config.DestinationReachedThreshold 
            : _config.WaypointReachedThreshold;
        
        if (xzDistanceToWaypoint < threshold)
        {
            // Find next valid waypoint
            int nextWaypointIndex = FindNextValidWaypoint(state.Waypoints, currentPosition, state.CurrentWaypointIndex + 1);
            state.CurrentWaypointIndex = nextWaypointIndex;
            
            // Report progress
            var progress = state.CurrentWaypointIndex / (float)state.Waypoints.Count;
            OnMovementProgress?.Invoke(entity.EntityId, progress);
            
            if (nextWaypointIndex < state.Waypoints.Count)
            {
                MoveTowardWaypoint(entity, state.Waypoints[nextWaypointIndex], currentPosition, state.MaxSpeed);
            }
            else
            {
                CompleteMovement(entity, state);
            }
            return;
        }
        
        // State-aware movement: only apply pathfinding when GROUNDED
        if (_characterController.IsGrounded(entity))
        {
            // Calculate desired velocity toward waypoint
            // If avoiding collision, reduce speed to 75% to allow smoother passing
            float effectiveSpeed = state.IsAvoidingCollision ? state.MaxSpeed * 0.75f : state.MaxSpeed;
            var desiredVelocity = CalculateDesiredVelocity(currentPosition, targetWaypoint, effectiveSpeed);
            
            // Apply local avoidance if enabled (but only for minor adjustments, not head-on collisions)
            // Head-on collisions are handled by collision prediction + replanning above
            if (_config.EnableLocalAvoidance)
            {
                var nearbyEntities = _localAvoidance.GetNearbyEntities(
                    currentPosition, 
                    entity.EntityId, 
                    _config.MaxAvoidanceNeighbors
                );
                
                // Only apply steering if there are no critical collisions predicted
                if (nearbyEntities.Count > 0)
                {
                    var currentVel = _physicsWorld.GetEntityVelocity(entity);
                    var predictions = _localAvoidance.PredictCollisions(
                        currentPosition,
                        currentVel,
                        targetWaypoint,
                        nearbyEntities
                    );
                    
                    bool hasCriticalCollision = predictions.Any(p => p.ShouldReplan);
                    
                    // Only use steering for non-critical situations
                    if (!hasCriticalCollision)
                    {
                        desiredVelocity = _localAvoidance.CalculateAvoidanceVelocity(
                            entity, 
                            desiredVelocity, 
                            nearbyEntities
                        );
                    }
                }
            }
            
            // Calculate target Y position for this waypoint
            float targetY = targetWaypoint.Y + agentHalfHeight;
            
            // CRITICAL FIX: Prevent both sinking AND unwanted vertical displacement
            // When agents collide, collision forces can push them upward (observed: Y jumped to 3.19)
            // We need to aggressively clamp vertical position and velocity
            var currentPos = _physicsWorld.GetEntityPosition(entity);
            float yError = currentPos.Y - targetY;
            
            // Check if agent is near other agents (collision might be causing displacement)
            bool nearOtherAgents = false;
            if (_config.EnableLocalAvoidance)
            {
                var nearbyEntities = _localAvoidance.GetNearbyEntities(
                    currentPos, 
                    entity.EntityId, 
                    _config.MaxAvoidanceNeighbors
                );
                nearOtherAgents = nearbyEntities.Count > 0;
            }
            
            // STRICT Y CONSTRAINT: When grounded, agents should NEVER deviate significantly from targetY
            // Maximum allowed deviation: 10cm (prevents the Y=3.19 jumping issue)
            float maxYDeviation = 0.10f; // 10cm maximum
            
            // Use even stricter thresholds when near other agents to prevent collision displacement
            float yTolerance = nearOtherAgents ? 0.005f : 0.01f; // 0.5cm when near others, 1cm normally
            
            if (Math.Abs(yError) > yTolerance)
            {
                // Clamp Y position to prevent excessive displacement
                float clampedY = targetY + Math.Clamp(yError, -maxYDeviation, maxYDeviation);
                
                // Directly set Y position (kinematic override) - this prevents displacement completely
                _physicsWorld.SetEntityPosition(entity, new Vector3(currentPos.X, clampedY, currentPos.Z));
                
                // CRITICAL: Zero out ALL vertical velocity when correcting position
                // This prevents collision forces from accumulating upward velocity
                var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
                desiredVelocity = new Vector3(desiredVelocity.X, 0, desiredVelocity.Z);
                _physicsWorld.SetEntityVelocity(entity, new Vector3(currentVelocity.X, 0, currentVelocity.Z));
            }
            else
            {
                // Position is within tolerance
                var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
                
                // CRITICAL: Aggressively clamp vertical velocity to prevent displacement
                // When near other agents, allow ZERO vertical velocity (prevents collision push)
                // Otherwise, allow minimal downward velocity for ground settling
                float maxVerticalVelocity = nearOtherAgents ? 0.0f : 0.1f;
                float clampedY = Math.Clamp(currentVelocity.Y, -0.5f, maxVerticalVelocity);
                desiredVelocity = new Vector3(desiredVelocity.X, clampedY, desiredVelocity.Z);
            }
            
            _physicsWorld.SetEntityVelocity(entity, desiredVelocity);
            
            // Apply gentle grounding force to maintain stability
            _characterController.ApplyGroundingForce(entity, desiredVelocity, targetY, agentHalfHeight);
        }
        else if (_characterController.IsRecovering(entity))
        {
            // CRITICAL FIX: Gently correct Y position while recovering
            // Apply upward impulse if agent is too low, don't teleport to avoid oscillation
            var currentPos = _physicsWorld.GetEntityPosition(entity);
            var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
            
            // Use current waypoint Y if available, otherwise use target position Y
            float waypointY = state.CurrentWaypointIndex < state.Waypoints.Count 
                ? state.Waypoints[state.CurrentWaypointIndex].Y 
                : state.TargetPosition.Y;
            
            float targetY = waypointY + agentHalfHeight;
            float yError = currentPos.Y - targetY;
            
            // Only apply correction if agent has settled (low vertical velocity)
            if (Math.Abs(currentVelocity.Y) < 0.1f && yError < -0.1f) // Sunk more than 10cm
            {
                // Apply gentle upward correction force, don't teleport
                float correctionForce = Math.Abs(yError) * 20.0f; // Proportional force
                _physicsWorld.ApplyLinearImpulse(entity, new Vector3(0, correctionForce * 0.016f, 0));
            }
            
            // Wait for stability before resuming pathfinding
            if (_characterController.IsStable(entity))
            {
                // Final position check and correction before resuming
                currentPos = _physicsWorld.GetEntityPosition(entity);
                yError = currentPos.Y - targetY;
                
                if (Math.Abs(yError) > 0.2f) // Still significantly off after recovery
                {
                    // Direct teleport as last resort
                    _physicsWorld.SetEntityPosition(entity, new Vector3(currentPos.X, targetY, currentPos.Z));
                    _physicsWorld.SetEntityVelocity(entity, new Vector3(currentVelocity.X, 0, currentVelocity.Z));
                }
                
                // Replan path from current position
                ReplanPath(entity, state, currentPosition);
                _characterController.SetGrounded(entity);
            }
            // else: continue waiting for stability
        }
        // else AIRBORNE - do nothing, let physics handle it completely!
    }
    
    /// <summary>
    /// Validates the current path and replans if blocked.
    /// </summary>
    private void ValidateAndReplanIfNeeded(PhysicsEntity entity, MovementState state, Vector3 currentPosition)
    {
        var validationResult = _pathValidator.ValidatePath(
            state.Waypoints, 
            state.CurrentWaypointIndex, 
            entity.EntityId
        );
        
        if (!validationResult.IsValid)
        {
            Console.WriteLine($"[MovementController] Path blocked for entity {entity.EntityId}: {validationResult.BlockageType}");
            OnPathBlocked?.Invoke(entity.EntityId);
            
            // Check if we can use local avoidance for temporary obstacles
            if (validationResult.BlockageType == BlockageType.Temporary && _config.TryLocalAvoidanceFirst)
            {
                var nearbyEntities = _localAvoidance.GetNearbyEntities(
                    currentPosition, 
                    entity.EntityId, 
                    _config.MaxAvoidanceNeighbors
                );
                
                if (_localAvoidance.CanAvoidLocally(currentPosition, state.TargetPosition, nearbyEntities))
                {
                    Console.WriteLine($"[MovementController] Using local avoidance for temporary obstacle");
                    return; // Let local avoidance handle it
                }
            }
            
            // Need to replan - check cooldown
            var timeSinceLastReplan = (float)(DateTime.UtcNow - state.LastReplanTime).TotalSeconds;
            if (timeSinceLastReplan >= _config.ReplanCooldown)
            {
                ReplanPath(entity, state, currentPosition);
            }
        }
    }
    
    /// <summary>
    /// Replans the path from current position to target.
    /// </summary>
    private void ReplanPath(PhysicsEntity entity, MovementState state, Vector3 currentPosition)
    {
        Console.WriteLine($"[MovementController] Replanning path for entity {entity.EntityId}");
        
        var extents = new Vector3(5.0f, 10.0f, 5.0f);
        var pathResult = _pathfinder.FindPath(currentPosition, state.TargetPosition, extents);
        
        if (pathResult.Success)
        {
            state.Waypoints = pathResult.Waypoints.ToList();
            state.CurrentWaypointIndex = FindNextValidWaypoint(state.Waypoints, currentPosition, 0);
            state.LastReplanTime = DateTime.UtcNow;
            
            Console.WriteLine($"[MovementController] Replan successful: {state.Waypoints.Count} waypoints");
            OnPathReplanned?.Invoke(entity.EntityId);
        }
        else
        {
            Console.WriteLine($"[MovementController] Replan failed - stopping movement");
            StopMovement(entity.EntityId);
        }
    }
    
    /// <summary>
    /// Completes movement for an entity.
    /// </summary>
    private void CompleteMovement(PhysicsEntity entity, MovementState state)
    {
        var finalPosition = _physicsWorld.GetEntityPosition(entity);
        
        // FIXED: Keep agent in tracking to continue applying Y correction
        // Mark as completed so we stop pathfinding but continue height correction
        state.IsCompleted = true;
        
        // Stop all velocity
        _physicsWorld.SetEntityVelocity(entity, Vector3.Zero);
        
        // Fire completion event
        Console.WriteLine($"[MovementController] Entity {entity.EntityId} reached destination");
        OnDestinationReached?.Invoke(entity.EntityId, finalPosition);
        OnMovementProgress?.Invoke(entity.EntityId, 1.0f);
    }
    
    /// <summary>
    /// Calculates desired velocity toward a target.
    /// </summary>
    private Vector3 CalculateDesiredVelocity(Vector3 currentPos, Vector3 targetPos, float maxSpeed)
    {
        // For ground-based movement, only move in XZ plane
        var targetXZ = new Vector3(targetPos.X, currentPos.Y, targetPos.Z);
        var direction = targetXZ - currentPos;
        var distance = direction.Length();
        
        if (distance < 0.001f)
            return Vector3.Zero;
        
        return Vector3.Normalize(direction) * maxSpeed;
    }
    
    /// <summary>
    /// Moves entity toward a specific waypoint.
    /// </summary>
    private void MoveTowardWaypoint(PhysicsEntity entity, Vector3 waypoint, Vector3 currentPosition, float maxSpeed)
    {
        // Only apply movement if grounded
        if (!_characterController.IsGrounded(entity))
            return;
        
        var desiredVelocity = CalculateDesiredVelocity(currentPosition, waypoint, maxSpeed);
        
        // Preserve Y velocity for gravity (don't zero it out!)
        var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
        desiredVelocity = new Vector3(desiredVelocity.X, currentVelocity.Y, desiredVelocity.Z);
        
        _physicsWorld.SetEntityVelocity(entity, desiredVelocity);
    }
    
    /// <summary>
    /// Stops movement for an entity.
    /// </summary>
    public void StopMovement(int entityId)
    {
        var entity = _physicsWorld.EntityRegistry.GetEntityById(entityId);
        if (entity != null)
        {
            // Stop horizontal movement but preserve Y velocity (gravity)
            var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
            _physicsWorld.SetEntityVelocity(entity, new Vector3(0, currentVelocity.Y, 0));
        }
        
        _movementStates.Remove(entityId);
        _characterController.RemoveEntity(entityId);
    }
    
    /// <summary>
    /// Makes an entity jump if it's grounded.
    /// </summary>
    /// <param name="entityId">Entity to jump</param>
    /// <param name="jumpForce">Upward impulse force (default: 5.0f)</param>
    /// <returns>True if jump was successful (entity was grounded)</returns>
    public bool Jump(int entityId, float jumpForce = 5.0f)
    {
        var entity = _physicsWorld.EntityRegistry.GetEntityById(entityId);
        if (entity == null)
            return false;
        
        if (_characterController.IsGrounded(entity))
        {
            var impulse = new Vector3(0, jumpForce, 0);
            _physicsWorld.ApplyLinearImpulse(entity, impulse);
            _characterController.SetAirborne(entity); // Immediately transition to AIRBORNE
            Console.WriteLine($"[MovementController] Entity {entityId} jumped with force {jumpForce}");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Applies knockback to an entity (e.g., from being hit).
    /// Forces entity into AIRBORNE state and pauses pathfinding.
    /// Note: Knockback works via direct impulse application, not physics collision forces.
    /// </summary>
    /// <param name="entityId">Entity to knock back</param>
    /// <param name="direction">Direction of knockback (will be normalized)</param>
    /// <param name="force">Knockback force magnitude</param>
    public void Knockback(int entityId, Vector3 direction, float force)
    {
        var entity = _physicsWorld.EntityRegistry.GetEntityById(entityId);
        if (entity == null)
            return;
        
        var normalizedDirection = Vector3.Normalize(direction);
        var impulse = normalizedDirection * force;
        _physicsWorld.ApplyLinearImpulse(entity, impulse);
        _characterController.SetAirborne(entity); // Force transition to AIRBORNE
        Console.WriteLine($"[MovementController] Entity {entityId} knocked back with force {force} in direction ({direction.X:F2}, {direction.Y:F2}, {direction.Z:F2})");
    }
    
    /// <summary>
    /// Applies a push to an entity (e.g., from a skill or explosion).
    /// Unlike Knockback, this can be used while grounded and doesn't force AIRBORNE state.
    /// Optionally makes the entity temporarily pushable to allow other agents to push it.
    /// </summary>
    /// <param name="entityId">Entity to push</param>
    /// <param name="direction">Direction of push (will be normalized)</param>
    /// <param name="force">Push force magnitude</param>
    /// <param name="makePushable">If true, temporarily marks entity as pushable so other agents can push it</param>
    /// <param name="pushableDuration">How long to keep entity pushable (in seconds)</param>
    public void Push(int entityId, Vector3 direction, float force, bool makePushable = false, float pushableDuration = 1.0f)
    {
        var entity = _physicsWorld.EntityRegistry.GetEntityById(entityId);
        if (entity == null)
            return;
        
        var normalizedDirection = Vector3.Normalize(direction);
        var impulse = normalizedDirection * force;
        _physicsWorld.ApplyLinearImpulse(entity, impulse);
        
        // Optionally make entity pushable for a short duration
        if (makePushable)
        {
            _physicsWorld.SetEntityPushable(entity, true);
            
            // TODO: Add a timer system to automatically revert pushable state after duration
            // For now, game logic should manually call SetEntityPushable(entity, false) after the duration
            Console.WriteLine($"[MovementController] Entity {entityId} is now pushable for {pushableDuration}s");
        }
        
        Console.WriteLine($"[MovementController] Entity {entityId} pushed with force {force} in direction ({direction.X:F2}, {direction.Y:F2}, {direction.Z:F2})");
    }
    
    /// <summary>
    /// Finds the next waypoint that has a different XZ position from the current position.
    /// </summary>
    private int FindNextValidWaypoint(IReadOnlyList<Vector3> waypoints, Vector3 currentPosition, int startIndex)
    {
        for (int i = startIndex; i < waypoints.Count; i++)
        {
            var waypoint = waypoints[i];
            var xzDistance = CalculateXZDistance(currentPosition, waypoint);
            
            if (xzDistance > 0.1f)
                return i;
        }
        
        return waypoints.Count;
    }
    
    /// <summary>
    /// Calculates XZ distance between two points (ignoring Y axis).
    /// </summary>
    private float CalculateXZDistance(Vector3 a, Vector3 b)
    {
        return MathF.Sqrt(
            (b.X - a.X) * (b.X - a.X) +
            (b.Z - a.Z) * (b.Z - a.Z)
        );
    }
}

    /// <summary>
    /// Internal state for tracking entity movement.
    /// </summary>
    internal class MovementState
    {
        public int EntityId { get; set; }
        public Vector3 TargetPosition { get; set; }
        public float MaxSpeed { get; set; }
        public float AgentHeight { get; set; }
        public float AgentRadius { get; set; }
        public List<Vector3> Waypoints { get; set; } = new();
        public int CurrentWaypointIndex { get; set; }
        public float LastValidationTime { get; set; }
        public DateTime LastReplanTime { get; set; }
        public DateTime StartTime { get; set; }
        public float TotalDistance { get; set; }
        public bool IsCompleted { get; set; } = false;
        public bool IsAvoidingCollision { get; set; } = false;
        public bool HasDetourWaypoint { get; set; } = false;
    }
