using System.Numerics;

namespace Spatial.Integration.Events;

/// <summary>
/// Event arguments for when an entity reaches its destination.
/// </summary>
public class DestinationReachedEventArgs
{
    public int EntityId { get; }
    public Vector3 Position { get; }
    public float TotalDistance { get; }
    public float TotalTime { get; }
    
    public DestinationReachedEventArgs(int entityId, Vector3 position, float totalDistance, float totalTime)
    {
        EntityId = entityId;
        Position = position;
        TotalDistance = totalDistance;
        TotalTime = totalTime;
    }
}

/// <summary>
/// Event arguments for when a path becomes blocked.
/// </summary>
public class PathBlockedEventArgs
{
    public int EntityId { get; }
    public Vector3 CurrentPosition { get; }
    public Vector3 BlockedWaypoint { get; }
    public bool IsTemporary { get; }
    
    public PathBlockedEventArgs(int entityId, Vector3 currentPosition, Vector3 blockedWaypoint, bool isTemporary)
    {
        EntityId = entityId;
        CurrentPosition = currentPosition;
        BlockedWaypoint = blockedWaypoint;
        IsTemporary = isTemporary;
    }
}

/// <summary>
/// Event arguments for when a path is replanned.
/// </summary>
public class PathReplannedEventArgs
{
    public int EntityId { get; }
    public Vector3 CurrentPosition { get; }
    public Vector3 TargetPosition { get; }
    public int NewWaypointCount { get; }
    public string Reason { get; }
    
    public PathReplannedEventArgs(int entityId, Vector3 currentPosition, Vector3 targetPosition, int newWaypointCount, string reason)
    {
        EntityId = entityId;
        CurrentPosition = currentPosition;
        TargetPosition = targetPosition;
        NewWaypointCount = newWaypointCount;
        Reason = reason;
    }
}

/// <summary>
/// Event arguments for movement progress updates.
/// </summary>
public class MovementProgressEventArgs
{
    public int EntityId { get; }
    public float PercentComplete { get; }
    public int CurrentWaypointIndex { get; }
    public int TotalWaypoints { get; }
    
    public MovementProgressEventArgs(int entityId, float percentComplete, int currentWaypointIndex, int totalWaypoints)
    {
        EntityId = entityId;
        PercentComplete = percentComplete;
        CurrentWaypointIndex = currentWaypointIndex;
        TotalWaypoints = totalWaypoints;
    }
}

/// <summary>
/// Event arguments for when movement starts.
/// </summary>
public class MovementStartedEventArgs
{
    public int EntityId { get; }
    public Vector3 StartPosition { get; }
    public Vector3 TargetPosition { get; }
    public float EstimatedTime { get; }
    
    public MovementStartedEventArgs(int entityId, Vector3 startPosition, Vector3 targetPosition, float estimatedTime)
    {
        EntityId = entityId;
        StartPosition = startPosition;
        TargetPosition = targetPosition;
        EstimatedTime = estimatedTime;
    }
}
