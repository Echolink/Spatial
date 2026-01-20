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
    /// Agent height (total capsule height) for proper Y positioning.
    /// Used to calculate capsule center position relative to navmesh surface.
    /// </summary>
    public float AgentHeight { get; }
    
    public MovementRequest(int entityId, Vector3 targetPosition, float maxSpeed = 5.0f, float agentHeight = 2.0f)
    {
        EntityId = entityId;
        TargetPosition = targetPosition;
        MaxSpeed = maxSpeed;
        AgentHeight = agentHeight;
    }
}

