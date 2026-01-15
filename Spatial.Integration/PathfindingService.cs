using Spatial.Pathfinding;
using System.Numerics;

namespace Spatial.Integration;

/// <summary>
/// Service that wraps Spatial.Pathfinding for use by the game server.
/// Provides a simplified API for pathfinding operations.
/// </summary>
public class PathfindingService
{
    private readonly Pathfinder _pathfinder;
    
    /// <summary>
    /// Creates a new pathfinding service.
    /// </summary>
    public PathfindingService(Pathfinder pathfinder)
    {
        _pathfinder = pathfinder;
    }
    
    /// <summary>
    /// Finds a path from start to end position.
    /// </summary>
    public PathResult FindPath(Vector3 start, Vector3 end)
    {
        var extents = new Vector3(2.0f, 2.0f, 2.0f);
        return _pathfinder.FindPath(start, end, extents);
    }
    
    /// <summary>
    /// Checks if a position is valid (on the navigation mesh).
    /// </summary>
    public bool IsValidPosition(Vector3 position)
    {
        var extents = new Vector3(2.0f, 2.0f, 2.0f);
        return _pathfinder.IsValidPosition(position, extents);
    }
}

