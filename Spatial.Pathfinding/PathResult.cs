using System.Numerics;
using System.Collections.Generic;

namespace Spatial.Pathfinding;

/// <summary>
/// Result of a pathfinding query.
/// Contains the calculated path waypoints.
/// </summary>
public class PathResult
{
    /// <summary>
    /// Whether a valid path was found.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// True when the path could not reach the target (disconnected navmesh island or blocked).
    /// The waypoints still form the best corridor toward the target — the last waypoint
    /// is the furthest reachable point in the target direction.
    /// Only meaningful when <see cref="Success"/> is true.
    /// </summary>
    public bool IsPartial { get; }

    /// <summary>
    /// List of waypoints from start to end.
    /// Each waypoint is a position in world space.
    /// </summary>
    public IReadOnlyList<Vector3> Waypoints { get; }

    /// <summary>
    /// Total length of the path.
    /// </summary>
    public float TotalLength { get; }

    public PathResult(bool success, IReadOnlyList<Vector3> waypoints, float totalLength, bool isPartial = false)
    {
        Success = success;
        IsPartial = isPartial;
        Waypoints = waypoints;
        TotalLength = totalLength;
    }

    /// <summary>
    /// Creates a failed path result.
    /// </summary>
    public static PathResult Failed => new PathResult(false, Array.Empty<Vector3>(), 0f);
}

