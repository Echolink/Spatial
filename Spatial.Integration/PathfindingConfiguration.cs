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
}
