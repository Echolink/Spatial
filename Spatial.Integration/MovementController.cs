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
    private readonly PathfindingService _pathfindingService;
    // private readonly PathValidator _pathValidator;  // TODO: Implement dynamic path validation
    private readonly LocalAvoidance _localAvoidance;
    private readonly PathfindingConfiguration _config;
    private readonly ICharacterController _characterController;
    
    private readonly Dictionary<int, MovementState> _movementStates = new();
    
    /// <summary>
    /// Event fired when an entity reaches its destination
    /// </summary>
    public event Action<int, Vector3>? OnDestinationReached;
    
    /// <summary>
    /// Event fired when a path becomes blocked
    /// </summary>
#pragma warning disable CS0067 // Event is never used (reserved for future path validation)
    public event Action<int>? OnPathBlocked;
#pragma warning restore CS0067
    
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
    /// Gets the current waypoints for an entity (for visualization/debugging).
    /// Returns null if entity has no active movement state.
    /// </summary>
    public List<Vector3>? GetWaypoints(int entityId)
    {
        if (_movementStates.TryGetValue(entityId, out var state))
        {
            return new List<Vector3>(state.Waypoints);
        }
        return null;
    }
    
    /// <summary>
    /// Gets the original requested target position for an entity (before navmesh snapping).
    /// Returns null if entity has no active movement state.
    /// </summary>
    public Vector3? GetOriginalTargetPosition(int entityId)
    {
        if (_movementStates.TryGetValue(entityId, out var state))
            return state.OriginalTargetPosition;
        return null;
    }

    /// <summary>
    /// Gets the actual (navmesh-snapped) target position for an entity.
    /// Returns null if entity has no active movement state.
    /// </summary>
    public Vector3? GetActualTargetPosition(int entityId)
    {
        if (_movementStates.TryGetValue(entityId, out var state))
            return state.TargetPosition;
        return null;
    }

    /// <summary>
    /// Gets the current waypoint index for an entity (for visualization/debugging).
    /// Returns -1 if entity has no active movement state.
    /// </summary>
    public int GetCurrentWaypointIndex(int entityId)
    {
        if (_movementStates.TryGetValue(entityId, out var state))
        {
            return state.CurrentWaypointIndex;
        }
        return -1;
    }
    
    /// <summary>
    /// Creates a new movement controller with velocity-based character controller.
    /// 
    /// ⚠️ LEGACY: This constructor creates velocity-based controller (backup only).
    /// 
    /// ✅ PRODUCTION: Use constructor with MotorCharacterController instead:
    /// <code>
    /// var pathfindingService = new PathfindingService(pathfinder, agentConfig, config);
    /// var motorController = new MotorCharacterController(physicsWorld);
    /// var movementController = new MovementController(
    ///     physicsWorld, pathfindingService, agentConfig, config, motorController
    /// );
    /// </code>
    /// 
    /// See PRODUCTION_ARCHITECTURE.md for complete guide.
    /// </summary>
    /// <param name="physicsWorld">Physics world instance</param>
    /// <param name="pathfinder">Pathfinder instance (should use same AgentConfig as this controller)</param>
    /// <param name="agentConfig">Agent configuration (SINGLE SOURCE OF TRUTH for MaxClimb, MaxSlope, etc.)</param>
    /// <param name="config">Optional pathfinding behavior configuration</param>
    /// <param name="characterConfig">Optional character controller configuration</param>
    public MovementController(
        PhysicsWorld physicsWorld, 
        Pathfinder pathfinder, 
        AgentConfig agentConfig,
        PathfindingConfiguration? config = null, 
        CharacterControllerConfig? characterConfig = null)
        : this(physicsWorld, new PathfindingService(pathfinder, agentConfig, config ?? new PathfindingConfiguration()), 
               agentConfig, config, new CharacterController(physicsWorld, characterConfig))
    {
    }
    
    /// <summary>
    /// Creates a new movement controller with explicit PathfindingService and velocity-based character controller.
    /// </summary>
    public MovementController(
        PhysicsWorld physicsWorld,
        PathfindingService pathfindingService,
        AgentConfig agentConfig,
        PathfindingConfiguration? config = null,
        CharacterController? characterController = null)
        : this(physicsWorld, pathfindingService, agentConfig, config, 
               (ICharacterController)(characterController ?? new CharacterController(physicsWorld)))
    {
    }
    
    /// <summary>
    /// Creates a new movement controller with motor-based character controller.
    /// </summary>
    public MovementController(
        PhysicsWorld physicsWorld,
        PathfindingService pathfindingService,
        AgentConfig agentConfig,
        PathfindingConfiguration? config = null,
        MotorCharacterController? motorController = null)
        : this(physicsWorld, pathfindingService, agentConfig, config,
               (ICharacterController)(motorController ?? new MotorCharacterController(physicsWorld)))
    {
    }
    
    /// <summary>
    /// Core constructor - accepts any character controller implementation.
    /// </summary>
    private MovementController(
        PhysicsWorld physicsWorld,
        PathfindingService pathfindingService,
        AgentConfig agentConfig,
        PathfindingConfiguration? config,
        ICharacterController characterController)
    {
        _physicsWorld = physicsWorld;
        _pathfinder = pathfindingService.Pathfinder;
        _pathfindingService = pathfindingService;
        _config = config ?? new PathfindingConfiguration();
        _localAvoidance = new LocalAvoidance(physicsWorld, _config.LocalAvoidanceRadius);
        _characterController = characterController;
        
        // Register ground contact callbacks with physics world
        physicsWorld.RegisterGroundContactCallbacks(
            onGroundContact: (dynamicEntity, groundEntity) => 
                _characterController.NotifyGroundContact(dynamicEntity, groundEntity),
            onGroundContactRemoved: (dynamicEntity, groundEntity) => 
                _characterController.NotifyGroundContactRemoved(dynamicEntity, groundEntity)
        );
    }
    
    /// <summary>
    /// Creates a new movement controller (legacy overload for backwards compatibility).
    /// Uses default AgentConfig values. Prefer the overload that accepts AgentConfig.
    /// </summary>
    [Obsolete("Use constructor with AgentConfig parameter for proper configuration alignment")]
    public MovementController(PhysicsWorld physicsWorld, Pathfinder pathfinder, PathfindingConfiguration? config = null, CharacterControllerConfig? characterConfig = null)
        : this(physicsWorld, pathfinder, new AgentConfig(), config, characterConfig)
    {
        Console.WriteLine("[MovementController] WARNING: Using legacy constructor without AgentConfig.");
        Console.WriteLine("[MovementController] Consider using constructor with AgentConfig for proper alignment.");
    }
    
    /// <summary>
    /// Requests movement for an entity to a target position.
    /// Uses pathfinding to find a valid path, then applies physics-based movement.
    /// Automatically snaps positions to navmesh using downward-priority vertical search.
    /// </summary>
    /// <param name="request">Movement request</param>
    /// <returns>MovementResponse with detailed feedback about the movement operation</returns>
    public MovementResponse RequestMovement(MovementRequest request)
    {
        var entity = _physicsWorld.EntityRegistry.GetEntityById(request.EntityId);
        if (entity == null)
        {
            return new MovementResponse(
                $"Entity {request.EntityId} not found",
                Vector3.Zero,
                request.TargetPosition
            );
        }
        
        // Get current position
        var currentPosition = _physicsWorld.GetEntityPosition(entity);
        
        Console.WriteLine($"[MovementController] Requesting movement for entity {request.EntityId}");
        Console.WriteLine($"[MovementController] Current position: ({currentPosition.X:F2}, {currentPosition.Y:F2}, {currentPosition.Z:F2})");
        Console.WriteLine($"[MovementController] Target position: ({request.TargetPosition.X:F2}, {request.TargetPosition.Y:F2}, {request.TargetPosition.Z:F2})");
        
        // Determine search extents (use request override or config defaults)
        var searchExtents = request.SearchExtents ?? new Vector3(
            _config.HorizontalSearchExtent,
            _config.VerticalSearchExtent,
            _config.HorizontalSearchExtent
        );
        
        // CRITICAL FIX: For grounded agents, use ground contact point to validate navmesh
        // Don't re-snap start position - agent is already settled on valid ground
        // Only check if the ground beneath the agent is on navmesh
        
        float agentHalfHeight = (request.AgentHeight / 2.0f) + request.AgentRadius;
        
        // Calculate agent's ground contact point (bottom of capsule)
        float groundY = currentPosition.Y - agentHalfHeight;
        var groundContactPoint = new Vector3(currentPosition.X, groundY, currentPosition.Z);
        
        Console.WriteLine($"[MovementController] Agent center: Y={currentPosition.Y:F2}");
        Console.WriteLine($"[MovementController] Ground contact: Y={groundY:F2}");
        
        // Check if ground contact point is near a navmesh surface
        // CRITICAL FIX: Use much larger search extents to handle cases where agent spawns
        // far from the nearest walkable navmesh polygon (e.g., off-mesh, in non-walkable areas)
        // Horizontal: 50m should cover most reasonable spawn distances
        // Vertical: 10m should handle multi-level scenarios
        var smallSearchExtents = new Vector3(50.0f, 10.0f, 50.0f);
        var nearestNavmesh = _pathfindingService.FindNearestValidPosition(groundContactPoint, smallSearchExtents);
        
        Vector3 snappedStart;
        if (nearestNavmesh != null)
        {
            // Calculate 3D distance (not just Y distance) to nearest navmesh point
            float horizontalDist = MathF.Sqrt(
                (nearestNavmesh.Value.X - currentPosition.X) * (nearestNavmesh.Value.X - currentPosition.X) +
                (nearestNavmesh.Value.Z - currentPosition.Z) * (nearestNavmesh.Value.Z - currentPosition.Z)
            );
            float verticalDist = Math.Abs(nearestNavmesh.Value.Y - groundY);
            float totalDist = MathF.Sqrt(horizontalDist * horizontalDist + verticalDist * verticalDist);
            
            Console.WriteLine($"[MovementController] Nearest navmesh: ({nearestNavmesh.Value.X:F2}, {nearestNavmesh.Value.Y:F2}, {nearestNavmesh.Value.Z:F2})");
            Console.WriteLine($"[MovementController]   Horizontal distance: {horizontalDist:F2}m, Vertical distance: {verticalDist:F2}m, Total: {totalDist:F2}m");
            
            // CRITICAL FIX: If agent is far from navmesh, snap to nearest valid position
            // This handles cases where agents spawn in non-walkable areas or off-mesh
            if (horizontalDist > 2.0f)
            {
                Console.WriteLine($"[MovementController] Agent spawned off navmesh, teleporting to nearest valid position");
                var teleportPos = new Vector3(nearestNavmesh.Value.X, nearestNavmesh.Value.Y + agentHalfHeight, nearestNavmesh.Value.Z);
                _physicsWorld.SetEntityPosition(entity, teleportPos);
                snappedStart = teleportPos;
                Console.WriteLine($"[MovementController]   Teleported to: ({snappedStart.X:F2}, {snappedStart.Y:F2}, {snappedStart.Z:F2})");
            }
            else
            {
                // Agent is close to navmesh horizontally, just align Y coordinate
                snappedStart = new Vector3(currentPosition.X, nearestNavmesh.Value.Y + agentHalfHeight, currentPosition.Z);
                Console.WriteLine($"[MovementController] Agent near valid navmesh, aligning Y coordinate");
            }
        }
        else
        {
            return new MovementResponse(
                $"No navmesh found near agent's ground contact point (Y={groundY:F2})",
                currentPosition,
                request.TargetPosition
            );
        }

        // Snap target position to navmesh (use provided Y as search starting point)
        var snappedTarget = _pathfindingService.FindNearestValidPosition(request.TargetPosition, searchExtents);
        if (snappedTarget == null)
        {
            return new MovementResponse(
                $"Target position not on navmesh (no walkable surface found within search extents)",
                snappedStart,
                request.TargetPosition
            );
        }

        Console.WriteLine($"[MovementController] Start position: ({snappedStart.X:F2}, {snappedStart.Y:F2}, {snappedStart.Z:F2})");
        Console.WriteLine($"[MovementController] Target position: ({snappedTarget.Value.X:F2}, {snappedTarget.Value.Y:F2}, {snappedTarget.Value.Z:F2})");

        // Find path using ground-level start (navmesh surface Y, not capsule-center Y).
        // Passing capsule-center Y to DotRecast causes it to pick the nearest polygon by
        // 3D distance, which may be a ledge or upper terrace polygon instead of the ground
        // polygon the agent is actually standing on.
        var pathfindingExtents = new Vector3(
            _config.PathfindingSearchExtentsHorizontal,
            _config.PathfindingSearchExtentsVertical,
            _config.PathfindingSearchExtentsHorizontal
        );
        var pathfindStart = new Vector3(snappedStart.X, nearestNavmesh.Value.Y, snappedStart.Z);
        var pathResult = _pathfindingService.FindPath(pathfindStart, snappedTarget.Value, pathfindingExtents);
        
        Console.WriteLine($"[MovementController] Pathfinding result: Success={pathResult.Success}");
        if (pathResult.Success)
        {
            Console.WriteLine($"[MovementController] Path has {pathResult.Waypoints.Count} waypoints, length={pathResult.TotalLength:F2}");
        }
        
        if (!pathResult.Success)
        {
            return new MovementResponse(
                $"No valid path found from start to target",
                snappedStart,
                snappedTarget.Value
            );
        }
        
        // Store movement state (use snapped target, not original request)
        var state = new MovementState
        {
            EntityId = request.EntityId,
            OriginalTargetPosition = request.TargetPosition,
            TargetPosition = snappedTarget.Value, // Use snapped position
            MaxSpeed = request.MaxSpeed,
            AgentHeight = request.AgentHeight,
            AgentRadius = request.AgentRadius,
            Waypoints = pathResult.Waypoints.ToList(),
            CurrentWaypointIndex = FindNextValidWaypoint(pathResult.Waypoints, snappedStart, 0),
            LastValidationTime = 0f,
            LastReplanTime = DateTime.MinValue,
            StartTime = DateTime.UtcNow,
            TotalDistance = pathResult.TotalLength
        };
        
        _movementStates[request.EntityId] = state;
        
        // Fire movement started event (use snapped positions)
        OnMovementStarted?.Invoke(request.EntityId, snappedStart, snappedTarget.Value);
        
        // Start moving toward first valid waypoint
        if (state.CurrentWaypointIndex < state.Waypoints.Count)
        {
            MoveTowardWaypoint(entity, state.Waypoints[state.CurrentWaypointIndex], snappedStart, request.MaxSpeed);
        }
        
        // Return success response with actual positions
        return new MovementResponse(
            snappedStart,
            snappedTarget.Value,
            pathResult,
            request.MaxSpeed
        );
    }
    
    /// <summary>
    /// Updates movement for all entities.
    /// Call this every frame to continue movement along paths.
    /// Also updates grounded state for ALL agents even without active movement.
    /// </summary>
    public void UpdateMovement(float deltaTime)
    {
        // First, update grounded state for ALL agents (even those without movement states)
        // This prevents idle agents from sinking through the ground
        var allAgents = _physicsWorld.EntityRegistry.GetAllEntities()
            .Where(e => !e.IsStatic && (e.EntityType == EntityType.Player || 
                                        e.EntityType == EntityType.NPC || 
                                        e.EntityType == EntityType.Enemy))
            .ToList();
        
        foreach (var agent in allAgents)
        {
            _characterController.UpdateGroundedState(agent, deltaTime);
            
            // Apply idle grounding to agents without active movement states
            if (!_movementStates.ContainsKey(agent.EntityId))
            {
                _characterController.ApplyIdleGrounding(agent);
            }
        }
        
        // Then update entities with active movement states
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
        
        // If movement is completed, stop all movement
        if (state.IsCompleted)
        {
            // Keep velocity at zero
            _physicsWorld.SetEntityVelocity(entity, Vector3.Zero);
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
        
        // Check if agent is on completely wrong floor (multi-level aware)
        // Calculate once for both floor check and slope handling
        float heightDiff = Math.Abs(targetWaypoint.Y - (currentPosition.Y - agentHalfHeight));
        float horizontalDist = CalculateXZDistance(currentPosition, targetWaypoint);
        bool isOnSlope = heightDiff > 0.5f && horizontalDist > 0.1f;
        
        // Use larger tolerance on slopes to account for physics settling
        float floorTolerance = isOnSlope ? _config.FloorLevelTolerance * 2.0f : _config.FloorLevelTolerance;
        
        if (!IsOnCorrectFloor(currentPosition, targetWaypoint, agentHalfHeight, floorTolerance))
        {
            // Accumulate time on wrong floor before replanning.
            // Replanning every frame (125 FPS) when falling creates a replan storm that
            // wastes CPU without helping — the agent is still airborne on the next frame.
            state.WrongFloorAccumulator += deltaTime;
            const float wrongFloorReplanDelay = 0.5f;

            if (state.WrongFloorAccumulator >= wrongFloorReplanDelay)
            {
                float currentGroundY = currentPosition.Y - agentHalfHeight;
                float targetGroundY = targetWaypoint.Y;
                float floorDiff = Math.Abs(currentGroundY - targetGroundY);
                Console.WriteLine($"[MovementController] Agent {entity.EntityId} on wrong floor for {state.WrongFloorAccumulator:F2}s - replanning");
                Console.WriteLine($"  CurrentGroundY={currentGroundY:F2}, TargetGroundY={targetGroundY:F2}, Diff={floorDiff:F2}, Tolerance={floorTolerance:F2}");
                state.WrongFloorAccumulator = 0f;
                ReplanPath(entity, state, currentPosition);
            }
            return;
        }
        else
        {
            state.WrongFloorAccumulator = 0f;
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
            // Calculate desired XZ velocity toward waypoint
            float effectiveSpeed = state.IsAvoidingCollision ? state.MaxSpeed * 0.75f : state.MaxSpeed;
            var desiredVelocity = CalculateDesiredVelocity(currentPosition, targetWaypoint, effectiveSpeed);
            
            // Apply local avoidance if enabled
            if (_config.EnableLocalAvoidance)
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
                    
                    bool hasCriticalCollision = predictions.Any(p => p.ShouldReplan);
                    
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
            
            // Check for edge before applying movement
            // CRITICAL: Only stop if edge is NOT part of the planned path
            // If the path ahead requires going down/up (ramp/slope), trust the pathfinding
            // OPTIMIZATION: Only check edge every 10 frames to reduce navmesh query overhead
            state.EdgeCheckFrameCounter++;
            if (state.EdgeCheckFrameCounter >= 10)
            {
                state.EdgeCheckFrameCounter = 0;
                
                bool isExpectedElevationChange = false;
                if (state.CurrentWaypointIndex < state.Waypoints.Count)
                {
                    // Check elevation change from current position to target waypoint
                    float elevationChange = Math.Abs(targetWaypoint.Y - (currentPosition.Y - agentHalfHeight));
                    isExpectedElevationChange = elevationChange > 2.0f; // Path expects significant elevation change
                    
                    // Also check if final destination requires elevation change
                    var finalWaypoint = state.Waypoints[^1];
                    float totalElevationChange = Math.Abs(finalWaypoint.Y - (currentPosition.Y - agentHalfHeight));
                    if (totalElevationChange > 3.0f)
                        isExpectedElevationChange = true;
                }
                
                if (!isExpectedElevationChange && WouldFallOffEdge(entity, currentPosition, desiredVelocity, state.AgentRadius))
                {
                    Console.WriteLine($"[MovementController] Agent {entity.EntityId} at unexpected edge - stopping");
                    _physicsWorld.SetEntityVelocity(entity, Vector3.Zero);
                    ReplanPath(entity, state, currentPosition);
                    return;
                }
            }
            
            // CRITICAL: Handle slope/ramp navigation properly
            // We need to actively keep the agent at the correct Y position
            // (heightDiff, horizontalDist, isOnSlope already calculated above for floor check)
            
            // Always move horizontally (XZ only)
            var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
            var finalVelocity = new Vector3(desiredVelocity.X, currentVelocity.Y, desiredVelocity.Z);
            _physicsWorld.SetEntityVelocity(entity, finalVelocity);
            
            // CRITICAL FIX: Query navmesh at agent's current XZ position
            // This gives us the actual surface height where the agent is standing
            // rather than interpolating between sparse waypoints
            
            // SLOPE-AWARE: Use different tolerance and behavior on slopes vs flat ground
            // On slopes, allow more natural deviation to prevent struggle
            float heightTolerance = isOnSlope ? 0.15f : 0.05f; // 15cm on slopes, 5cm on flat
            
            float targetY;
            float currentGroundY = currentPosition.Y - agentHalfHeight;
            
            // On steep slopes, reduce grounding frequency to allow natural physics
            // Only apply correction every few frames on slopes
            if (isOnSlope)
            {
                state.SlopeGroundingFrameCounter++;
                if (state.SlopeGroundingFrameCounter % 5 != 0)
                {
                    // Skip grounding this frame - let physics handle slope naturally
                    return;
                }
            }
            
            // Query navmesh at current XZ position with small search extents
            var currentXZ = new Vector3(currentPosition.X, currentPosition.Y, currentPosition.Z);
            var smallSearchExtents = new Vector3(1.0f, 2.0f, 1.0f); // Small horizontal, larger vertical
            var surfaceAtCurrentPos = _pathfindingService.FindNearestValidPosition(currentXZ, smallSearchExtents);
            
            if (surfaceAtCurrentPos != null)
            {
                // Use the actual navmesh surface Y at this XZ position
                targetY = surfaceAtCurrentPos.Value.Y + agentHalfHeight;
                
                // DAMPING: Only apply grounding if we're far enough from target
                float heightError = Math.Abs(currentPosition.Y - targetY);
                if (heightError < heightTolerance)
                {
                    // Already close enough - don't apply grounding force
                    // This prevents constant micro-adjustments and stuttering
                    return;
                }
            }
            else
            {
                // Fallback: interpolate between waypoints if navmesh query fails
                Vector3 prevPos;
                if (state.CurrentWaypointIndex > 0)
                {
                    prevPos = state.Waypoints[state.CurrentWaypointIndex - 1];
                }
                else
                {
                    prevPos = new Vector3(currentPosition.X, currentPosition.Y - agentHalfHeight, currentPosition.Z);
                }
                
                float totalHorizontalDist = new Vector2(
                    targetWaypoint.X - prevPos.X,
                    targetWaypoint.Z - prevPos.Z
                ).Length();
                
                float coveredHorizontalDist = new Vector2(
                    currentPosition.X - prevPos.X,
                    currentPosition.Z - prevPos.Z
                ).Length();
                
                float progress = totalHorizontalDist > 0.1f ? coveredHorizontalDist / totalHorizontalDist : 0.0f;
                progress = Math.Clamp(progress, 0.0f, 1.0f);
                
                float interpolatedGroundY = prevPos.Y + (targetWaypoint.Y - prevPos.Y) * progress;
                targetY = interpolatedGroundY + agentHalfHeight;
                
                // Apply damping to interpolation too
                float heightError = Math.Abs(currentPosition.Y - targetY);
                if (heightError < heightTolerance)
                {
                    return;
                }
            }
            
            // Apply grounding force only if height error is significant
            _characterController.ApplyGroundingForce(entity, desiredVelocity, targetY, agentHalfHeight);
        }
        else if (_characterController.IsRecovering(entity))
        {
            // CRITICAL: Apply grounding even when recovering to prevent sinking
            // Query navmesh at current XZ position to get actual surface height
            const float heightTolerance = 0.05f; // 5cm tolerance
            
            var currentXZ = new Vector3(currentPosition.X, currentPosition.Y, currentPosition.Z);
            var smallSearchExtents = new Vector3(1.0f, 2.0f, 1.0f);
            var surfaceAtCurrentPos = _pathfindingService.FindNearestValidPosition(currentXZ, smallSearchExtents);
            
            if (surfaceAtCurrentPos != null)
            {
                // Use actual navmesh surface Y
                float targetY = surfaceAtCurrentPos.Value.Y + agentHalfHeight;
                
                // Apply damping - only correct if error is significant
                float heightError = Math.Abs(currentPosition.Y - targetY);
                if (heightError >= heightTolerance)
                {
                    _characterController.ApplyGroundingForce(entity, Vector3.Zero, targetY, agentHalfHeight);
                }
            }
            else if (state.CurrentWaypointIndex < state.Waypoints.Count)
            {
                // Fallback: interpolate between waypoints
                Vector3 recoverPrevPos;
                if (state.CurrentWaypointIndex > 0)
                {
                    recoverPrevPos = state.Waypoints[state.CurrentWaypointIndex - 1];
                }
                else
                {
                    recoverPrevPos = new Vector3(currentPosition.X, currentPosition.Y - agentHalfHeight, currentPosition.Z);
                }
                
                var recoverTargetWaypoint = state.Waypoints[state.CurrentWaypointIndex];
                
                float totalHorizontalDist = new Vector2(
                    recoverTargetWaypoint.X - recoverPrevPos.X,
                    recoverTargetWaypoint.Z - recoverPrevPos.Z
                ).Length();
                
                float coveredHorizontalDist = new Vector2(
                    currentPosition.X - recoverPrevPos.X,
                    currentPosition.Z - recoverPrevPos.Z
                ).Length();
                
                float progress = totalHorizontalDist > 0.1f ? coveredHorizontalDist / totalHorizontalDist : 0.0f;
                progress = Math.Clamp(progress, 0.0f, 1.0f);
                
                float interpolatedGroundY = recoverPrevPos.Y + (recoverTargetWaypoint.Y - recoverPrevPos.Y) * progress;
                float targetY = interpolatedGroundY + agentHalfHeight;
                
                // Apply damping
                float heightError = Math.Abs(currentPosition.Y - targetY);
                if (heightError >= heightTolerance)
                {
                    _characterController.ApplyGroundingForce(entity, Vector3.Zero, targetY, agentHalfHeight);
                }
            }
            
            // Wait for stability after landing
            if (_characterController.IsStable(entity))
            {
                // Replan from current position
                ReplanPath(entity, state, currentPosition);
                _characterController.SetGrounded(entity);
            }
            // else: continue waiting, but with grounding applied
        }
        else if (_characterController.IsAirborne(entity))
        {
            // AIRBORNE RECOVERY: corrects spurious AIRBORNE states caused by missed/lost contact
            // manifolds at terrain edges — without interfering with jumps or knockback.
            //
            // The key distinction is VERTICAL VELOCITY:
            //   • Spurious AIRBORNE (bug): contact lost for one frame while the agent is
            //     essentially standing still — vertical velocity is tiny (~0 to -1 m/s).
            //   • Legitimate AIRBORNE (jump/knockback): agent was intentionally launched —
            //     vertical velocity is large (|velY| > recoveryVelocityThreshold).
            //
            // If velY is large we let physics handle the arc completely.
            // Only when velY is small do we look for a surface to snap back to.
            const float recoveryVelocityThreshold = 2.0f; // m/s — below this = spurious, above = real physics
            const float fallRecoveryThreshold     = 3.0f; // m   — max distance above surface to attempt snap

            var vel = _physicsWorld.GetEntityVelocity(entity);

            // Skip recovery for jumps (vel.Y > 0, moving up) or fast falls / knockback (|vel.Y| > threshold).
            if (Math.Abs(vel.Y) > recoveryVelocityThreshold)
                goto airborneRecoveryDone;

            float agentFeetY = currentPosition.Y - agentHalfHeight;

            // Use physics raycast first — the physics mesh Y may differ from navmesh Y by ~0.2 m
            // due to voxelisation. Snapping to the wrong Y causes a constant bounce loop.
            float groundSurfaceY;
            bool foundSurface = false;

            var rayStart = new Vector3(currentPosition.X, currentPosition.Y + 0.1f, currentPosition.Z);
            var rayEnd   = new Vector3(currentPosition.X, currentPosition.Y - (fallRecoveryThreshold + agentHalfHeight + 0.5f), currentPosition.Z);
            if (_physicsWorld.Raycast(rayStart, rayEnd, out var rayHit, out _))
            {
                groundSurfaceY = rayHit.Y;
                foundSurface = true;
            }
            else
            {
                // Fallback: navmesh query (handles cases where physics mesh has no geometry directly below)
                var navSurface = _pathfindingService.FindNearestValidPosition(
                    new Vector3(currentPosition.X, agentFeetY, currentPosition.Z),
                    new Vector3(1.0f, 5.0f, 1.0f));
                if (navSurface != null)
                {
                    groundSurfaceY = navSurface.Value.Y;
                    foundSurface = true;
                }
                else
                {
                    groundSurfaceY = 0f;
                }
            }

            if (foundSurface)
            {
                float distAboveSurface = agentFeetY - groundSurfaceY;
                if (distAboveSurface <= fallRecoveryThreshold && distAboveSurface >= -agentHalfHeight)
                {
                    float targetCenterY = groundSurfaceY + agentHalfHeight;
                    float yDelta = Math.Abs(currentPosition.Y - targetCenterY);

                    // Only teleport when correction is significant enough to matter.
                    // Teleporting every frame clears BepuPhysics contact manifolds,
                    // creating a constant AIRBORNE→snap→AIRBORNE cycle.
                    if (yDelta > 0.05f)
                    {
                        var snappedCenter = new Vector3(currentPosition.X, targetCenterY, currentPosition.Z);
                        _physicsWorld.SetEntityPosition(entity, snappedCenter);
                        _physicsWorld.SetEntityVelocity(entity, new Vector3(vel.X, 0f, vel.Z));

                        Console.WriteLine(
                            $"[MovementController] Entity {entity.EntityId} fall-recovery snap: " +
                            $"Y {currentPosition.Y:F2} -> {targetCenterY:F2} " +
                            $"(physics surface={groundSurfaceY:F2}, dist={distAboveSurface:F2}m)");
                    }

                    // Always force GROUNDED when near the surface, even without a position snap.
                    // This re-establishes grounded state while physics contacts regenerate.
                    _characterController.SetGrounded(entity);
                }
            }

            airborneRecoveryDone:;
        }
        // else AIRBORNE with high velocity — jump or knockback, let physics handle the arc fully
    }
    
    /// <summary>
    /// Validates the current path and replans if the agent is stuck or upcoming waypoints
    /// are no longer on the NavMesh (e.g., after a dynamic tile rebuild).
    /// Called every PathValidationInterval seconds per entity.
    /// </summary>
    private void ValidateAndReplanIfNeeded(PhysicsEntity entity, MovementState state, Vector3 currentPosition)
    {
        // Respect replan cooldown — avoid thrashing
        var timeSinceLastReplan = (float)(DateTime.UtcNow - state.LastReplanTime).TotalSeconds;
        if (timeSinceLastReplan < _config.ReplanCooldown) return;

        // ── Check 1: Stuck detection ────────────────────────────────────────
        // If the agent hasn't moved horizontally beyond the threshold over multiple
        // validation intervals, it is likely stuck against geometry or another agent.
        float horizontalMoved = Vector2.Distance(
            new Vector2(currentPosition.X, currentPosition.Z),
            new Vector2(state.LastValidationPosition.X, state.LastValidationPosition.Z));

        state.LastValidationPosition = currentPosition;

        if (horizontalMoved < _config.StuckDetectionThreshold)
        {
            state.ValidationStuckCount++;
            if (state.ValidationStuckCount >= _config.StuckDetectionCount)
            {
                Console.WriteLine($"[MovementController] Entity {entity.EntityId} stuck (moved {horizontalMoved:F2}m over {state.ValidationStuckCount} intervals), replanning");
                state.ValidationStuckCount = 0;
                ReplanPath(entity, state, currentPosition);
                return;
            }
        }
        else
        {
            state.ValidationStuckCount = 0;
        }

        // ── Check 2: Waypoint look-ahead ────────────────────────────────────
        // Verify the next N waypoints are still valid NavMesh positions.
        // In a static NavMesh this is a no-op; it becomes meaningful after
        // dynamic tile rebuilds (Step 5).
        if (_config.PathValidationLookaheadWaypoints > 0 && state.Waypoints.Count > 0)
        {
            int lookaheadEnd = Math.Min(
                state.CurrentWaypointIndex + _config.PathValidationLookaheadWaypoints,
                state.Waypoints.Count);

            // Tight snap extents: accept the waypoint only if it's within 2m horizontally
            var snapExtents = new Vector3(2f, 4f, 2f);

            for (int i = state.CurrentWaypointIndex; i < lookaheadEnd; i++)
            {
                if (!_pathfinder.IsValidPosition(state.Waypoints[i], snapExtents))
                {
                    Console.WriteLine($"[MovementController] Waypoint {i} for entity {entity.EntityId} is no longer on NavMesh, replanning");
                    state.ValidationStuckCount = 0;
                    ReplanPath(entity, state, currentPosition);
                    return;
                }
            }
        }
    }
    
    /// <summary>
    /// Replans the path from current position to target.
    /// </summary>
    private void ReplanPath(PhysicsEntity entity, MovementState state, Vector3 currentPosition)
    {
        Console.WriteLine($"[MovementController] Replanning path for entity {entity.EntityId}");

        var extents = new Vector3(
            _config.PathfindingSearchExtentsHorizontal,
            _config.PathfindingSearchExtentsVertical,
            _config.PathfindingSearchExtentsHorizontal
        );

        // Use ground-level Y for the replan start so DotRecast selects the polygon the
        // agent is actually standing on, not a ledge polygon closer to the capsule center.
        float halfHeight = (state.AgentHeight / 2.0f) + state.AgentRadius;
        var groundStart = new Vector3(currentPosition.X, currentPosition.Y - halfHeight, currentPosition.Z);
        var pathResult = _pathfindingService.FindPath(groundStart, state.TargetPosition, extents);
        
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
    /// Checks if agent is on the correct floor level for current waypoint.
    /// Allows physics variance (±tolerance), but detects wrong floor entirely.
    /// Used to distinguish between natural physics settling vs falling through floor.
    /// </summary>
    private bool IsOnCorrectFloor(Vector3 currentPos, Vector3 targetWaypoint, float agentHalfHeight, float tolerance = 3.0f)
    {
        float currentGroundY = currentPos.Y - agentHalfHeight;
        float targetGroundY = targetWaypoint.Y;
        float floorDifference = Math.Abs(currentGroundY - targetGroundY);
        
        // Within tolerance = same floor (accounts for slopes, physics variance)
        // Beyond tolerance = wrong floor (fell through or on different level)
        return floorDifference <= tolerance;
    }
    
    /// <summary>
    /// Checks if moving in a direction would cause agent to fall off an edge.
    /// Uses navmesh query to check if ground exists ahead.
    /// </summary>
    private bool WouldFallOffEdge(PhysicsEntity entity, Vector3 currentPos, Vector3 desiredVelocity, float agentRadius)
    {
        if (desiredVelocity.Length() < 0.01f)
            return false;
        
        // Check position slightly ahead in movement direction
        Vector3 checkPos = currentPos + Vector3.Normalize(desiredVelocity) * (agentRadius * _config.EdgeCheckDistanceMultiplier);
        
        // CRITICAL FIX: Use much larger horizontal search extents to reliably find navmesh ahead
        // Agent radius (0.5m) is too small for sparse navmesh polygons or moving between edges
        // Query navmesh at check position
        var searchExtents = new Vector3(
            3.0f, // Increased from agentRadius to 3.0m
            5.0f, // Search down 5m to find ground
            3.0f  // Increased from agentRadius to 3.0m
        );
        
        var groundAhead = _pathfindingService.FindNearestValidPosition(checkPos, searchExtents);
        
        if (groundAhead == null)
            return true; // No ground ahead - would fall!
        
        // Check if ground ahead is significantly lower (cliff/drop)
        float dropDistance = currentPos.Y - groundAhead.Value.Y;
        return dropDistance > _config.MaxSafeDropDistance;
    }
    
    /// <summary>
    /// Calculates desired velocity toward a target.
    /// Always returns horizontal (XZ) velocity - Y is handled by physics.
    /// </summary>
    private Vector3 CalculateDesiredVelocity(Vector3 currentPos, Vector3 targetPos, float maxSpeed)
    {
        // ALWAYS use horizontal movement (XZ plane only)
        // This ensures agent follows navmesh path precisely
        // Y positioning is handled by physics naturally
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
        
        // Frame counter for reducing edge check frequency
        public int EdgeCheckFrameCounter { get; set; } = 0;
        
        // Frame counter for slope grounding frequency
        public int SlopeGroundingFrameCounter { get; set; } = 0;

        // Stuck detection: track position at last validation tick
        public Vector3 LastValidationPosition { get; set; }
        public int ValidationStuckCount { get; set; } = 0;

        // Target tracking: original requested position vs navmesh-snapped position
        public Vector3 OriginalTargetPosition { get; set; }

        // Accumulates time spent in a wrong-floor condition before triggering a replan.
        // Prevents the per-frame replan storm when an agent is falling.
        public float WrongFloorAccumulator { get; set; } = 0f;
    }
