using DotRecast.Detour;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using System.Numerics;
using System.Collections.Generic;

namespace Spatial.Pathfinding;

    /// <summary>
    /// Provides pathfinding queries on a navigation mesh.
    /// Uses DotRecast's DtNavMeshQuery to find paths.
    /// </summary>
    public class Pathfinder
    {
        private readonly NavMeshData _navMeshData;
        
        /// <summary>
        /// Gets the navigation mesh data (exposed for spawn validation).
        /// </summary>
        public NavMeshData NavMeshData => _navMeshData;
        
        /// <summary>
        /// Creates a new pathfinder using the provided navigation mesh.
        /// </summary>
        public Pathfinder(NavMeshData navMeshData)
        {
            _navMeshData = navMeshData;
        }
    
    /// <summary>
    /// Finds a path from start to end position.
    /// </summary>
    /// <param name="start">Start position in world space</param>
    /// <param name="end">End position in world space</param>
    /// <param name="extents">Search extents (half-size) for finding nearest polygons</param>
    /// <returns>Path result containing waypoints if successful</returns>
    public PathResult FindPath(Vector3 start, Vector3 end, Vector3 extents)
    {
        var query = _navMeshData.Query;
        
        // Convert Vector3 to RcVec3f for DotRecast API
        var startVec = new RcVec3f(start.X, start.Y, start.Z);
        var endVec = new RcVec3f(end.X, end.Y, end.Z);
        var extentsVec = new RcVec3f(extents.X, extents.Y, extents.Z);
        
        // Find nearest polygons for start and end positions
        // Configure filter to accept walkable polygons (flag 0x01)
        var filter = new DtQueryDefaultFilter();
        filter.SetIncludeFlags(0x01); // Include walkable flag
        filter.SetExcludeFlags(0); // No exclusions
        
        query.FindNearestPoly(startVec, extentsVec, filter, out var startRef, out var startNearestPt, out var startPolyFound);
        query.FindNearestPoly(endVec, extentsVec, filter, out var endRef, out var endNearestPt, out var endPolyFound);
        
        Console.WriteLine($"[Pathfinder] Finding path from ({start.X:F2}, {start.Y:F2}, {start.Z:F2}) to ({end.X:F2}, {end.Y:F2}, {end.Z:F2})");
        Console.WriteLine($"[Pathfinder] Search extents: ({extents.X:F2}, {extents.Y:F2}, {extents.Z:F2})");
        Console.WriteLine($"[Pathfinder] Start poly found: {startPolyFound}, ref: {startRef}");
        if (startPolyFound)
        {
            Console.WriteLine($"[Pathfinder] Start nearest point: ({startNearestPt.X:F2}, {startNearestPt.Y:F2}, {startNearestPt.Z:F2})");
        }
        Console.WriteLine($"[Pathfinder] End poly found: {endPolyFound}, ref: {endRef}");
        if (endPolyFound)
        {
            Console.WriteLine($"[Pathfinder] End nearest point: ({endNearestPt.X:F2}, {endNearestPt.Y:F2}, {endNearestPt.Z:F2})");
        }
        
        if (!startPolyFound || !endPolyFound)
        {
            Console.WriteLine($"[Pathfinder] Pathfinding failed: start found={startPolyFound}, end found={endPolyFound}");
            return PathResult.Failed;
        }
        
        // Find path between polygons using Span for path buffer
        var pathBuffer = new long[256];
        var pathSpan = new System.Span<long>(pathBuffer);
        var pathStatus = query.FindPath(startRef, endRef, startNearestPt, endNearestPt, filter, pathSpan, out int pathCount, maxPath: 256);
        
        // Check if pathfinding succeeded (DtStatus - use bitwise AND check)
        // Note: DtStatus is a struct, comparison may need different approach
        var successFlag = pathStatus & DtStatus.DT_SUCCESS;
        if (successFlag.Value == 0 || pathCount == 0)
        {
            return PathResult.Failed;
        }
        
        // Extract the actual path
        var path = new long[pathCount];
        pathSpan.Slice(0, pathCount).CopyTo(path);
        
        // Straighten the path and get waypoints
        var straightPathBuffer = new DtStraightPath[256];
        var straightPathSpan = new System.Span<DtStraightPath>(straightPathBuffer);
        var straightPathStatus = query.FindStraightPath(
            startNearestPt, endNearestPt, path, pathCount,
            straightPathSpan, out int straightPathCount,
            maxStraightPath: 256, options: 0
        );
        
        var straightSuccessFlag = straightPathStatus & DtStatus.DT_SUCCESS;
        if (straightSuccessFlag.Value == 0 || straightPathCount == 0)
        {
            return PathResult.Failed;
        }
        
        // Convert DtStraightPath waypoints to Vector3
        var waypoints = new List<Vector3>();
        float totalLength = 0f;
        
        for (int i = 0; i < straightPathCount; i++)
        {
            var straightPath = straightPathBuffer[i];
            var waypoint = straightPath.pos;
            waypoints.Add(new Vector3(waypoint.X, waypoint.Y, waypoint.Z));
            
            // Calculate path length
            if (i > 0)
            {
                var prev = straightPathBuffer[i - 1].pos;
                var dx = waypoint.X - prev.X;
                var dy = waypoint.Y - prev.Y;
                var dz = waypoint.Z - prev.Z;
                totalLength += (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }
        
        return new PathResult(true, waypoints, totalLength);
    }
    
    /// <summary>
    /// Checks if a position is on the navigation mesh.
    /// </summary>
    /// <param name="position">Position to check</param>
    /// <param name="extents">Search extents (half-size) for finding nearest polygon</param>
    /// <returns>True if position is valid (on or near the navigation mesh)</returns>
    public bool IsValidPosition(Vector3 position, Vector3 extents)
    {
        var query = _navMeshData.Query;
        
        // Convert Vector3 to RcVec3f
        var posVec = new RcVec3f(position.X, position.Y, position.Z);
        var extentsVec = new RcVec3f(extents.X, extents.Y, extents.Z);
        
        // Find nearest polygon
        var filter = new DtQueryDefaultFilter();
        query.FindNearestPoly(posVec, extentsVec, filter, out var polyRef, out var nearestPt, out var found);
        
        return found;
    }
}
