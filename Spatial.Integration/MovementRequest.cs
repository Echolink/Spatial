using System.Numerics;

namespace Spatial.Integration;

/// <summary>
/// Request for entity movement.
/// </summary>
public class MovementRequest
{
    /// <summary>
    /// Entity ID to move
    /// </summary>
    public int EntityId { get; }
    
    /// <summary>
    /// Target position
    /// </summary>
    public Vector3 TargetPosition { get; }
    
    /// <summary>
    /// Maximum movement speed
    /// </summary>
    public float MaxSpeed { get; }
    
    /// <summary>
    /// Agent height (capsule cylinder length) for proper Y positioning.
    /// Used to calculate capsule center position relative to navmesh surface.
    /// </summary>
    public float AgentHeight { get; }
    
    /// <summary>
    /// Agent radius (capsule radius) for proper Y positioning.
    /// Used to calculate capsule center position relative to navmesh surface.
    /// </summary>
    public float AgentRadius { get; }
    
    /// <summary>
    /// Optional search extents override for finding navmesh surfaces.
    /// If null, uses default extents from PathfindingConfiguration.
    /// X/Z: Horizontal search radius, Y: Vertical search range (above/below search point).
    /// </summary>
    public Vector3? SearchExtents { get; }
    
    public MovementRequest(
        int entityId, 
        Vector3 targetPosition, 
        float maxSpeed = 5.0f, 
        float agentHeight = 2.0f, 
        float agentRadius = 0.4f,
        Vector3? searchExtents = null)
    {
        EntityId = entityId;
        TargetPosition = targetPosition;
        MaxSpeed = maxSpeed;
        AgentHeight = agentHeight;
        AgentRadius = agentRadius;
        SearchExtents = searchExtents;
    }
}

