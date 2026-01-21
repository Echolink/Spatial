using Spatial.Physics;

namespace Spatial.Integration;

/// <summary>
/// Information about a predicted collision between two entities.
/// </summary>
public class CollisionPrediction
{
    /// <summary>
    /// The other entity we might collide with.
    /// </summary>
    public PhysicsEntity OtherEntity { get; set; } = null!;
    
    /// <summary>
    /// Estimated time until collision (in seconds).
    /// </summary>
    public float TimeToCollision { get; set; }
    
    /// <summary>
    /// Current distance to the other entity.
    /// </summary>
    public float CollisionDistance { get; set; }
    
    /// <summary>
    /// Whether this is a head-on collision (both moving toward each other).
    /// </summary>
    public bool IsHeadOn { get; set; }
    
    /// <summary>
    /// Whether the entity should replan its path to avoid this collision.
    /// </summary>
    public bool ShouldReplan { get; set; }
}
