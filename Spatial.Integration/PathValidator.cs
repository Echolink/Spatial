using System.Numerics;
using Spatial.Physics;

namespace Spatial.Integration;

/// <summary>
/// Detects when paths become invalid due to obstacles or other changes.
/// 
/// Key features:
/// - Check if waypoints are still reachable
/// - Detect new obstacles blocking path
/// - Throttled validation (not every frame)
/// - Classify blockage severity (temporary vs permanent)
/// </summary>
public class PathValidator
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly float _waypointCheckRadius = 0.5f; // Radius to check around waypoints
    
    public PathValidator(PhysicsWorld physicsWorld)
    {
        _physicsWorld = physicsWorld;
    }
    
    /// <summary>
    /// Validates a path and checks if it's still traversable.
    /// </summary>
    /// <param name="waypoints">The path waypoints</param>
    /// <param name="currentIndex">Current waypoint index</param>
    /// <param name="entityId">ID of the entity following the path (to ignore self)</param>
    /// <returns>Validation result</returns>
    public PathValidationResult ValidatePath(List<Vector3> waypoints, int currentIndex, int entityId)
    {
        if (currentIndex >= waypoints.Count)
        {
            return new PathValidationResult
            {
                IsValid = true,
                BlockageType = BlockageType.None
            };
        }
        
        // Check from current waypoint onwards
        for (int i = currentIndex; i < waypoints.Count - 1; i++)
        {
            var current = waypoints[i];
            var next = waypoints[i + 1];
            
            // Check if path segment is blocked
            var blockage = CheckPathSegment(current, next, entityId);
            
            if (blockage != BlockageType.None)
            {
                Console.WriteLine($"[PathValidator] Path blocked at waypoint {i} - blockage type: {blockage}");
                
                return new PathValidationResult
                {
                    IsValid = false,
                    BlockageType = blockage,
                    BlockedAtWaypointIndex = i,
                    BlockingObstaclePosition = null // Could be enhanced to return actual obstacle position
                };
            }
        }
        
        return new PathValidationResult
        {
            IsValid = true,
            BlockageType = BlockageType.None
        };
    }
    
    /// <summary>
    /// Checks if a specific waypoint is blocked by obstacles.
    /// </summary>
    public bool IsWaypointBlocked(Vector3 waypoint, Vector3 currentPos, int entityId)
    {
        // Get all entities near the waypoint
        var nearbyEntities = _physicsWorld.GetEntitiesInRadius(waypoint, _waypointCheckRadius);
        
        // Check if any blocking entities are present
        foreach (var entity in nearbyEntities)
        {
            // Ignore self
            if (entity.EntityId == entityId)
                continue;
            
            // Check if entity blocks movement
            if (IsBlockingEntity(entity))
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if a path segment between two points is blocked.
    /// </summary>
    private BlockageType CheckPathSegment(Vector3 start, Vector3 end, int entityId)
    {
        // Sample points along the path segment
        var direction = end - start;
        var distance = direction.Length();
        
        if (distance < 0.01f)
            return BlockageType.None;
        
        var normalizedDir = Vector3.Normalize(direction);
        var sampleCount = Math.Max(2, (int)(distance / 0.5f)); // Sample every 0.5 units
        
        for (int i = 0; i <= sampleCount; i++)
        {
            var t = i / (float)sampleCount;
            var samplePoint = Vector3.Lerp(start, end, t);
            
            // Check for obstacles at this point
            var nearbyEntities = _physicsWorld.GetEntitiesInRadius(samplePoint, _waypointCheckRadius);
            
            foreach (var entity in nearbyEntities)
            {
                // Ignore self
                if (entity.EntityId == entityId)
                    continue;
                
                // Check if entity blocks movement
                if (IsBlockingEntity(entity))
                {
                    // Determine if it's temporary or permanent
                    if (entity.EntityType == EntityType.TemporaryObstacle)
                    {
                        return BlockageType.Temporary;
                    }
                    else
                    {
                        return BlockageType.Permanent;
                    }
                }
            }
        }
        
        return BlockageType.None;
    }
    
    /// <summary>
    /// Checks if an entity blocks movement.
    /// </summary>
    private bool IsBlockingEntity(PhysicsEntity entity)
    {
        return entity.EntityType == EntityType.StaticObject ||
               entity.EntityType == EntityType.Obstacle ||
               entity.EntityType == EntityType.TemporaryObstacle;
    }
}

/// <summary>
/// Result of path validation.
/// </summary>
public class PathValidationResult
{
    /// <summary>
    /// Whether the path is still valid
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Type of blockage if path is invalid
    /// </summary>
    public BlockageType BlockageType { get; set; }
    
    /// <summary>
    /// Index of the waypoint where blockage was detected
    /// </summary>
    public int BlockedAtWaypointIndex { get; set; }
    
    /// <summary>
    /// Position of the blocking obstacle (if available)
    /// </summary>
    public Vector3? BlockingObstaclePosition { get; set; }
}

/// <summary>
/// Type of blockage detected.
/// </summary>
public enum BlockageType
{
    /// <summary>
    /// No blockage
    /// </summary>
    None,
    
    /// <summary>
    /// Temporary obstacle that may despawn soon
    /// </summary>
    Temporary,
    
    /// <summary>
    /// Permanent obstacle requiring replan
    /// </summary>
    Permanent
}
