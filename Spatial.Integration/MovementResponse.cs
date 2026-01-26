using System.Numerics;
using Spatial.Pathfinding;

namespace Spatial.Integration;

/// <summary>
/// Response from a movement request, providing detailed feedback about the movement operation.
/// </summary>
public class MovementResponse
{
    /// <summary>
    /// Whether the movement request was successful.
    /// </summary>
    public bool Success { get; }
    
    /// <summary>
    /// Human-readable message explaining the result (success or failure reason).
    /// </summary>
    public string Message { get; }
    
    /// <summary>
    /// Actual start position snapped to navmesh (may differ from entity's current position).
    /// </summary>
    public Vector3 ActualStartPosition { get; }
    
    /// <summary>
    /// Actual target position snapped to navmesh (may differ from requested target).
    /// </summary>
    public Vector3 ActualTargetPosition { get; }
    
    /// <summary>
    /// Estimated path length in world units.
    /// </summary>
    public float EstimatedPathLength { get; }
    
    /// <summary>
    /// Estimated time to reach destination in seconds (based on max speed).
    /// </summary>
    public float EstimatedTime { get; }
    
    /// <summary>
    /// Full pathfinding result if successful, null otherwise.
    /// </summary>
    public PathResult? PathResult { get; }
    
    /// <summary>
    /// Creates a successful movement response.
    /// </summary>
    public MovementResponse(
        Vector3 actualStartPosition,
        Vector3 actualTargetPosition,
        PathResult pathResult,
        float maxSpeed)
    {
        Success = true;
        Message = "Movement request successful";
        ActualStartPosition = actualStartPosition;
        ActualTargetPosition = actualTargetPosition;
        PathResult = pathResult;
        EstimatedPathLength = pathResult.TotalLength;
        EstimatedTime = maxSpeed > 0 ? pathResult.TotalLength / maxSpeed : 0f;
    }
    
    /// <summary>
    /// Creates a failed movement response.
    /// </summary>
    public MovementResponse(string failureMessage, Vector3 actualStartPosition, Vector3 requestedTargetPosition)
    {
        Success = false;
        Message = failureMessage;
        ActualStartPosition = actualStartPosition;
        ActualTargetPosition = requestedTargetPosition;
        EstimatedPathLength = 0f;
        EstimatedTime = 0f;
        PathResult = null;
    }
}
