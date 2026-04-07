using System.Numerics;
using Spatial.Pathfinding;

namespace Spatial.Integration;

/// <summary>
/// Reason a movement request could not be fulfilled as requested.
/// </summary>
public enum MovementFailureReason
{
    /// <summary>No failure — movement started successfully.</summary>
    None,

    /// <summary>The entity ID was not found in the physics world.</summary>
    EntityNotFound,

    /// <summary>The agent's current position has no navmesh surface nearby.</summary>
    AgentOffNavmesh,

    /// <summary>
    /// The target (and any nearby candidates) is on a disconnected navmesh island —
    /// no reachable path exists from the agent's current island.
    /// </summary>
    NoReachablePosition,
}

/// <summary>
/// Response from a movement request, providing detailed feedback about the movement operation.
/// </summary>
public class MovementResponse
{
    /// <summary>
    /// Whether the movement request was successful (fully or with adjusted target).
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
    /// Actual target position the unit is moving toward (may differ from requested target
    /// when <see cref="WasTargetAdjusted"/> is true).
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
    /// Failure reason when <see cref="Success"/> is false.
    /// </summary>
    public MovementFailureReason FailureReason { get; }

    /// <summary>
    /// True when the unit is moving to a position different from the player's original
    /// requested target (e.g., nearest reachable snap or furthest directional advance).
    /// The client can use this to show a different visual indicator (e.g., yellow instead
    /// of green destination marker).
    /// </summary>
    public bool WasTargetAdjusted { get; }

    /// <summary>
    /// Human-readable reason for the target adjustment, or null if not adjusted.
    /// Values: "NearestReachableNearTarget", "FurthestReachableTowardTarget".
    /// </summary>
    public string? AdjustmentReason { get; }

    /// <summary>
    /// Creates a successful movement response.
    /// </summary>
    public MovementResponse(
        Vector3 actualStartPosition,
        Vector3 actualTargetPosition,
        PathResult pathResult,
        float maxSpeed,
        bool wasTargetAdjusted = false,
        string? adjustmentReason = null)
    {
        Success = true;
        FailureReason = MovementFailureReason.None;
        WasTargetAdjusted = wasTargetAdjusted;
        AdjustmentReason = adjustmentReason;
        Message = wasTargetAdjusted
            ? $"Movement started with adjusted target ({adjustmentReason})"
            : "Movement request successful";
        ActualStartPosition = actualStartPosition;
        ActualTargetPosition = actualTargetPosition;
        PathResult = pathResult;
        EstimatedPathLength = pathResult.TotalLength;
        EstimatedTime = maxSpeed > 0 ? pathResult.TotalLength / maxSpeed : 0f;
    }

    /// <summary>
    /// Creates a failed movement response with a structured reason.
    /// </summary>
    public MovementResponse(MovementFailureReason reason, string message, Vector3 actualStartPosition, Vector3 requestedTargetPosition)
    {
        Success = false;
        FailureReason = reason;
        Message = message;
        ActualStartPosition = actualStartPosition;
        ActualTargetPosition = requestedTargetPosition;
        EstimatedPathLength = 0f;
        EstimatedTime = 0f;
        PathResult = null;
        WasTargetAdjusted = false;
        AdjustmentReason = null;
    }

    /// <summary>
    /// Creates a failed movement response (legacy overload — uses TargetUnreachable reason).
    /// </summary>
    public MovementResponse(string failureMessage, Vector3 actualStartPosition, Vector3 requestedTargetPosition)
        : this(MovementFailureReason.NoReachablePosition, failureMessage, actualStartPosition, requestedTargetPosition)
    {
    }
}
