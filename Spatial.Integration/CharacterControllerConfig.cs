namespace Spatial.Integration;

/// <summary>
/// Configuration for character controller behavior.
/// Controls how agents interact with physics while following paths.
/// </summary>
public class CharacterControllerConfig
{
    /// <summary>
    /// Downward force to apply when grounded (Newtons).
    /// Higher = more stable on slopes, but harder to jump.
    /// Default: 5.0f
    /// </summary>
    public float GroundingForce { get; set; } = 5.0f;
    
    /// <summary>
    /// Velocity threshold for considering agent "grounded" (m/s).
    /// If vertical velocity is below this, agent is considered grounded.
    /// Default: 0.5f
    /// </summary>
    public float GroundedVelocityThreshold { get; set; } = 0.5f;
    
    /// <summary>
    /// Time agent must be stable before resuming pathfinding after landing (seconds).
    /// Prevents immediate replanning while agent is still settling.
    /// Default: 0.2f
    /// </summary>
    public float StabilityThreshold { get; set; } = 0.2f;
    
    /// <summary>
    /// Enable raycast-based ground detection (more accurate but expensive).
    /// If false, uses velocity-based detection.
    /// Default: false
    /// </summary>
    public bool UseRaycastGroundDetection { get; set; } = false;
    
    /// <summary>
    /// Raycast distance for ground detection (units).
    /// Only used if UseRaycastGroundDetection is true.
    /// Default: 0.2f
    /// </summary>
    public float GroundRaycastDistance { get; set; } = 0.2f;
    
    /// <summary>
    /// Maximum deviation from navmesh Y before replanning (units).
    /// If agent deviates more than this vertically, trigger replan.
    /// Default: 2.0f
    /// </summary>
    public float MaxNavmeshDeviation { get; set; } = 2.0f;
}
