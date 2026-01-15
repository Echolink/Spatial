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
    /// Creates a new movement controller.
    /// </summary>
    public MovementController(PhysicsWorld physicsWorld, Pathfinder pathfinder, PathfindingConfiguration? config = null)
    {
        _physicsWorld = physicsWorld;
        _pathfinder = pathfinder;
        _config = config ?? new PathfindingConfiguration();
        _pathValidator = new PathValidator(physicsWorld);
        _localAvoidance = new LocalAvoidance(physicsWorld, _config.LocalAvoidanceRadius);
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
        
        // Calculate desired velocity toward waypoint
        var desiredVelocity = CalculateDesiredVelocity(currentPosition, targetWaypoint, state.MaxSpeed);
        
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
                desiredVelocity = _localAvoidance.CalculateAvoidanceVelocity(
                    entity, 
                    desiredVelocity, 
                    nearbyEntities
                );
            }
        }
        
        // Preserve Y velocity (gravity)
        var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
        desiredVelocity = new Vector3(desiredVelocity.X, currentVelocity.Y, desiredVelocity.Z);
        
        // Apply velocity
        _physicsWorld.SetEntityVelocity(entity, desiredVelocity);
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
        
        // Stop horizontal movement (preserve Y velocity for gravity)
        var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
        _physicsWorld.SetEntityVelocity(entity, new Vector3(0, currentVelocity.Y, 0));
        
        // Remove from tracking
        _movementStates.Remove(entity.EntityId);
        
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
        var desiredVelocity = CalculateDesiredVelocity(currentPosition, waypoint, maxSpeed);
        
        // Preserve Y velocity (gravity)
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
            // Stop horizontal movement
            var currentVelocity = _physicsWorld.GetEntityVelocity(entity);
            _physicsWorld.SetEntityVelocity(entity, new Vector3(0, currentVelocity.Y, 0));
        }
        
        _movementStates.Remove(entityId);
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
    public List<Vector3> Waypoints { get; set; } = new();
    public int CurrentWaypointIndex { get; set; }
    public float LastValidationTime { get; set; }
    public DateTime LastReplanTime { get; set; }
    public DateTime StartTime { get; set; }
    public float TotalDistance { get; set; }
}
