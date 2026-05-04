using System.Numerics;
using System.Collections.Generic;

namespace Spatial.Pathfinding;

/// <summary>
/// Result of a pathfinding query.
/// </summary>
public class PathResult
{
    public bool Success { get; }

    /// <summary>
    /// True when the path could not reach the target (disconnected navmesh island or blocked).
    /// </summary>
    public bool IsPartial { get; }

    public IReadOnlyList<Vector3> Waypoints { get; }

    public float TotalLength { get; }

    /// <summary>
    /// Per-waypoint off-mesh link type. null = normal waypoint; non-null = link entry of that type.
    /// Parallel to <see cref="Waypoints"/>.
    /// </summary>
    public IReadOnlyList<OffMeshLinkType?> OffMeshLinkTypes { get; }

    public PathResult(bool success, IReadOnlyList<Vector3> waypoints, float totalLength,
        bool isPartial = false, IReadOnlyList<OffMeshLinkType?>? offMeshLinkTypes = null)
    {
        Success = success;
        IsPartial = isPartial;
        Waypoints = waypoints;
        TotalLength = totalLength;
        OffMeshLinkTypes = offMeshLinkTypes ?? Array.Empty<OffMeshLinkType?>();
    }

    public static PathResult Failed => new PathResult(false, Array.Empty<Vector3>(), 0f);
}
