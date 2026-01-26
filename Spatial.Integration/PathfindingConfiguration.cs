namespace Spatial.Integration;

/// <summary>
/// Configuration options for pathfinding and movement behavior.
/// Allows tuning of performance and behavior characteristics.
/// </summary>
public class PathfindingConfiguration
{
    /// <summary>
    /// How often to validate paths (seconds).
    /// Lower values = more frequent checks, higher CPU usage.
    /// Default: 0.5 seconds
    /// </summary>
    public float PathValidationInterval { get; set; } = 0.5f;
    
    /// <summary>
    /// Distance threshold for local avoidance (units).
    /// Entities within this radius will be avoided using steering.
    /// Default: 5.0 units
    /// </summary>
    public float LocalAvoidanceRadius { get; set; } = 5.0f;
    
    /// <summary>
    /// Minimum time between replans for same entity (seconds).
    /// Prevents excessive replanning.
    /// Default: 1.0 seconds
    /// </summary>
    public float ReplanCooldown { get; set; } = 1.0f;
    
    /// <summary>
    /// Maximum number of entities to consider for local avoidance.
    /// Lower values = better performance, less accurate avoidance.
    /// Default: 5 entities
    /// </summary>
    public int MaxAvoidanceNeighbors { get; set; } = 5;
    
    /// <summary>
    /// Distance threshold for reaching a waypoint (units).
    /// Lower values = more precise movement, but may cause stuttering.
    /// Default: 0.5 units
    /// </summary>
    public float WaypointReachedThreshold { get; set; } = 0.5f;
    
    /// <summary>
    /// Distance threshold for reaching final destination (units).
    /// Can be smaller than waypoint threshold for more precision.
    /// Default: 0.3 units
    /// </summary>
    public float DestinationReachedThreshold { get; set; } = 0.3f;
    
    /// <summary>
    /// Whether to enable local avoidance.
    /// Disable for better performance if avoidance isn't needed.
    /// Default: true
    /// </summary>
    public bool EnableLocalAvoidance { get; set; } = true;
    
    /// <summary>
    /// Whether to enable automatic replanning when path is blocked.
    /// Disable if you want to handle replanning manually.
    /// Default: true
    /// </summary>
    public bool EnableAutomaticReplanning { get; set; } = true;
    
    /// <summary>
    /// Strength of avoidance steering force.
    /// Higher values = stronger avoidance, but may cause jittery movement.
    /// Default: 2.0
    /// </summary>
    public float AvoidanceStrength { get; set; } = 2.0f;
    
    /// <summary>
    /// Radius for separation behavior (units).
    /// Entities closer than this will repel each other.
    /// Default: 2.0 units
    /// </summary>
    public float SeparationRadius { get; set; } = 2.0f;
    
    /// <summary>
    /// Whether to use local avoidance before replanning for temporary obstacles.
    /// Default: true
    /// </summary>
    public bool TryLocalAvoidanceFirst { get; set; } = true;
    
    /// <summary>
    /// Search extents for finding nearest navmesh polygons (X/Z horizontal).
    /// Used when pathfinding to find valid start/end positions.
    /// Default: 5.0 units
    /// </summary>
    public float PathfindingSearchExtentsHorizontal { get; set; } = 5.0f;
    
    /// <summary>
    /// Search extents for finding nearest navmesh polygons (Y vertical).
    /// Used when pathfinding to find valid start/end positions.
    /// Default: 10.0 units (allows targets up to 10 units above/below navmesh)
    /// </summary>
    public float PathfindingSearchExtentsVertical { get; set; } = 10.0f;
    
    /// <summary>
    /// Vertical search extent for finding navmesh surfaces (units above/below search point).
    /// Used in downward-priority search to find walkable surfaces in vertical column.
    /// Larger values = can find surfaces farther from search point (more forgiving).
    /// Default: 5.0 units
    /// </summary>
    public float VerticalSearchExtent { get; set; } = 5.0f;
    
    /// <summary>
    /// Horizontal search extent for finding nearest navmesh polygon (X/Z radius).
    /// Used when snapping positions to navmesh.
    /// Default: 2.0 units
    /// </summary>
    public float HorizontalSearchExtent { get; set; } = 2.0f;
    
    /// <summary>
    /// Distance ahead to check for edges when moving (units).
    /// Agent checks this far ahead to prevent walking off cliffs.
    /// Value is multiplied by agent radius: checkDistance = radius * this
    /// Default: 2.5 (checks 2.5x agent radius ahead)
    /// </summary>
    public float EdgeCheckDistanceMultiplier { get; set; } = 2.5f;
    
    /// <summary>
    /// Maximum drop distance before considering it a dangerous edge (units).
    /// If ground ahead is this much lower, agent stops.
    /// Default: 2.0 units
    /// </summary>
    public float MaxSafeDropDistance { get; set; } = 2.0f;
    
    /// <summary>
    /// Tolerance for floor-level matching (units).
    /// Agents within this vertical distance are considered on same floor.
    /// Must be large enough for slopes but small enough to detect floor changes.
    /// Default: 3.0 units
    /// </summary>
    public float FloorLevelTolerance { get; set; } = 3.0f;
    
    /// <summary>
    /// Maximum vertical climb distance per path segment (units).
    /// Paths with segments exceeding this vertical distance will be rejected.
    /// Should match the agent's physical climb capability.
    /// Default: 0.5 units (matches typical DotRecast MaxClimb setting)
    /// </summary>
    public float MaxPathSegmentClimb { get; set; } = 0.5f;
    
    /// <summary>
    /// Maximum slope angle for path segments (degrees).
    /// Paths with segments steeper than this will be rejected.
    /// Should match the agent's physical walkable slope limit.
    /// Default: 45.0 degrees (matches typical DotRecast MaxSlope setting)
    /// </summary>
    public float MaxPathSegmentSlope { get; set; } = 45.0f;
    
    /// <summary>
    /// Whether to enable path validation after pathfinding.
    /// When enabled, paths are checked for physical traversability constraints
    /// (MaxClimb, MaxSlope) before being accepted.
    /// Disable for performance if your navmesh is guaranteed valid.
    /// Default: true
    /// </summary>
    public bool EnablePathValidation { get; set; } = true;
    
    /// <summary>
    /// Whether to attempt automatic path fixing when validation fails.
    /// If enabled, the system will try to split violating segments with intermediate points.
    /// This is a best-effort approach and may not always succeed.
    /// Default: true (attempt to fix invalid paths before rejecting)
    /// </summary>
    public bool EnablePathAutoFix { get; set; } = true;
}
