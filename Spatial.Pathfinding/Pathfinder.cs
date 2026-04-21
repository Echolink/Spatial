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
        private readonly NavMeshGenerator _generator = new();

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

        // Detect partial result: DT_PARTIAL_RESULT detail bit = 7 (lower 28 bits).
        // Set when the target polygon is unreachable — the corridor terminates at the
        // boundary of the connected component, giving the furthest reachable point.
        const uint PartialResultDetail = 7u;
        bool isPartial = (pathStatus.Value & 0x0fffffffu) == PartialResultDetail;
        
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
        
        if (isPartial)
            Console.WriteLine($"[Pathfinder] Partial path returned ({waypoints.Count} waypoints) — target unreachable, last waypoint is furthest reachable point");

        return new PathResult(true, waypoints, totalLength, isPartial);
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

    /// <summary>
    /// Rebuilds the tile at position (<paramref name="tileX"/>, <paramref name="tileZ"/>) using
    /// the supplied geometry for that tile's world region.
    ///
    /// Only works when the NavMesh was generated with <see cref="NavMeshData.IsMultiTile"/> = true.
    /// Callers should follow up with path revalidation (e.g., via
    /// <see cref="Integration.PathfindingService.RebuildNavMeshRegion"/>) so active paths
    /// through the rebuilt tile are rechecked.
    /// </summary>
    /// <param name="tileX">Tile column index (world X / TileSize, floored).</param>
    /// <param name="tileZ">Tile row index (world Z / TileSize, floored).</param>
    /// <param name="tileVertices">Vertex positions (x,y,z triplets) for geometry in this tile.</param>
    /// <param name="tileIndices">Triangle indices into <paramref name="tileVertices"/>.</param>
    /// <param name="agentConfig">Agent constraints used when building the replacement tile.</param>
    /// <param name="navConfig">Tile configuration used when the NavMesh was originally created.</param>
    /// <returns>True if the tile was rebuilt successfully.</returns>
    public bool RebuildTile(int tileX, int tileZ,
        float[] tileVertices, int[] tileIndices,
        AgentConfig agentConfig, NavMeshConfiguration navConfig)
    {
        if (!_navMeshData.IsMultiTile)
        {
            Console.WriteLine("[Pathfinder] RebuildTile: NavMesh was not built with EnableTileUpdates=true. Skipping.");
            return false;
        }

        if (tileVertices.Length == 0 || tileIndices.Length == 0)
        {
            Console.WriteLine($"[Pathfinder] RebuildTile ({tileX},{tileZ}): no geometry provided, removing tile only.");
        }

        // Remove existing tile (if any)
        long existingRef = _navMeshData.NavMesh.GetTileRefAt(tileX, tileZ, 0);
        if (existingRef != 0)
        {
            _navMeshData.NavMesh.RemoveTile(existingRef);
            Console.WriteLine($"[Pathfinder] RebuildTile ({tileX},{tileZ}): removed old tile");
        }

        if (tileVertices.Length == 0 || tileIndices.Length == 0)
        {
            _navMeshData.InvalidateQuery();
            return true; // Tile removed, nothing to add
        }

        // Build new tile
        float cellSize = agentConfig.Radius / 2.0f;
        float cellHeight = cellSize / 2.0f;

        var (bmin, bmax) = CalculateTileBounds(tileX, tileZ, navConfig.TileSize, agentConfig,
            _navMeshData.TileOriginX, _navMeshData.TileOriginZ);
        var geomProvider = new SimpleInputGeomProvider(tileVertices, tileIndices);
        var walkableAreaMod = new DotRecast.Recast.RcAreaModification(0x3f);

        var config = new DotRecast.Recast.RcConfig(
            DotRecast.Recast.RcPartition.WATERSHED, cellSize, cellHeight,
            agentConfig.MaxSlope, agentConfig.Height, agentConfig.Radius, agentConfig.MaxClimb,
            1, 4, agentConfig.Radius * 8.0f, 1.3f, 6,
            cellSize * 6.0f, cellHeight,
            true, true, true, walkableAreaMod, true);

        var builderConfig = new DotRecast.Recast.RcBuilderConfig(config, bmin, bmax);
        var builder = new DotRecast.Recast.RcBuilder();
        var buildResult = builder.Build(geomProvider, builderConfig, keepInterResults: false);

        if (buildResult?.Mesh == null || buildResult.Mesh.npolys == 0)
        {
            Console.WriteLine($"[Pathfinder] RebuildTile ({tileX},{tileZ}): build produced 0 polygons");
            _navMeshData.InvalidateQuery();
            return true; // Tile is now empty (geometry was removed)
        }

        var tileData = _generator.BuildTileData(buildResult, config, agentConfig, tileX, tileZ);
        if (tileData == null)
        {
            Console.WriteLine($"[Pathfinder] RebuildTile ({tileX},{tileZ}): BuildTileData returned null");
            _navMeshData.InvalidateQuery();
            return false;
        }

        _navMeshData.NavMesh.AddTile(tileData, 0, 0, out _);
        _navMeshData.InvalidateQuery();

        Console.WriteLine($"[Pathfinder] RebuildTile ({tileX},{tileZ}): success ({buildResult.Mesh.npolys} polys)");
        return true;
    }

    /// <summary>
    /// Converts tile coordinates to the Recast bounding box used during generation.
    /// </summary>
    private static (RcVec3f bmin, RcVec3f bmax) CalculateTileBounds(
        int tileX, int tileZ, float tileSize, AgentConfig agentConfig,
        float originX = 0f, float originZ = 0f)
    {
        // Tile bounds are relative to the navmesh origin (bmin of the source geometry).
        // Without the origin offset, rebuild bounds would be wrong for meshes not centered at (0,0).
        var bmin = new RcVec3f(originX + tileX * tileSize, -1000f, originZ + tileZ * tileSize);
        var bmax = new RcVec3f(originX + (tileX + 1) * tileSize, 1000f, originZ + (tileZ + 1) * tileSize);
        bmin.Y -= agentConfig.CellHeight;
        bmax.Y += agentConfig.Height * 2f;
        return (bmin, bmax);
    }
}
