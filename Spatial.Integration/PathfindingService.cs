using Spatial.Pathfinding;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Spatial.Integration;

/// <summary>
/// Service that wraps Spatial.Pathfinding for use by the game server.
/// Provides a simplified API for pathfinding operations.
/// 
/// IMPORTANT: Uses AgentConfig as the single source of truth for physical constraints.
/// This ensures alignment between:
/// - NavMesh generation (voxel-level MaxClimb)
/// - Path validation (segment-level MaxClimb)
/// - Movement execution (runtime MaxClimb enforcement)
/// </summary>
public class PathfindingService
{
    private readonly Pathfinder _pathfinder;
    private readonly PathSegmentValidator _pathValidator;
    private readonly PathfindingConfiguration _config;
    private readonly AgentConfig _agentConfig;
    
    /// <summary>
    /// Gets the pathfinder instance.
    /// </summary>
    public Pathfinder Pathfinder => _pathfinder;
    
    /// <summary>
    /// Gets the agent configuration (single source of truth for physical constraints).
    /// </summary>
    public AgentConfig AgentConfig => _agentConfig;
    
    /// <summary>
    /// Creates a new pathfinding service.
    /// </summary>
    /// <param name="pathfinder">The pathfinder instance (contains NavMesh built with AgentConfig)</param>
    /// <param name="agentConfig">Agent configuration (SINGLE SOURCE OF TRUTH for MaxClimb, MaxSlope, etc.)</param>
    /// <param name="config">Optional pathfinding behavior configuration</param>
    public PathfindingService(
        Pathfinder pathfinder,
        AgentConfig agentConfig,
        PathfindingConfiguration? config = null)
    {
        _pathfinder = pathfinder;
        _agentConfig = agentConfig;
        _pathValidator = new PathSegmentValidator();
        _config = config ?? new PathfindingConfiguration();
        
        // Validate configuration alignment
        ValidateConfigurationAlignment();
    }
    
    /// <summary>
    /// Ensures PathfindingConfiguration is aligned with AgentConfig.
    /// Warns if MaxPathSegmentClimb/MaxPathSegmentSlope differ from AgentConfig values.
    /// </summary>
    private void ValidateConfigurationAlignment()
    {
        const float tolerance = 0.001f;
        
        if (Math.Abs(_config.MaxPathSegmentClimb - _agentConfig.MaxClimb) > tolerance)
        {
            Console.WriteLine($"[PathfindingService] WARNING: Configuration mismatch!");
            Console.WriteLine($"  PathfindingConfiguration.MaxPathSegmentClimb = {_config.MaxPathSegmentClimb}");
            Console.WriteLine($"  AgentConfig.MaxClimb = {_agentConfig.MaxClimb}");
            Console.WriteLine($"  Using AgentConfig.MaxClimb as source of truth.");
        }
        
        if (Math.Abs(_config.MaxPathSegmentSlope - _agentConfig.MaxSlope) > tolerance)
        {
            Console.WriteLine($"[PathfindingService] WARNING: Configuration mismatch!");
            Console.WriteLine($"  PathfindingConfiguration.MaxPathSegmentSlope = {_config.MaxPathSegmentSlope}");
            Console.WriteLine($"  AgentConfig.MaxSlope = {_agentConfig.MaxSlope}");
            Console.WriteLine($"  Using AgentConfig.MaxSlope as source of truth.");
        }
    }
    
    /// <summary>
    /// Finds a path from start to end position.
    /// If path validation is enabled, returned paths are guaranteed to satisfy
    /// MaxClimb and MaxSlope constraints per segment (using AgentConfig as source of truth).
    /// </summary>
    /// <param name="extents">Optional search extents. If not provided, uses production defaults (5.0, 10.0, 5.0)</param>
    public PathResult FindPath(Vector3 start, Vector3 end, Vector3? extents = null)
    {
        var searchExtents = extents ?? new Vector3(5.0f, 10.0f, 5.0f);
        var pathResult = _pathfinder.FindPath(start, end, searchExtents);
        
        // If path validation is disabled or path failed, return as-is
        if (!_config.EnablePathValidation || !pathResult.Success || pathResult.Waypoints.Count < 2)
        {
            return pathResult;
        }
        
        // Validate path against physical constraints (use AgentConfig as source of truth)
        var validation = _pathValidator.ValidatePath(
            pathResult.Waypoints,
            _agentConfig.MaxClimb,    // ← Use AgentConfig
            _agentConfig.MaxSlope,    // ← Use AgentConfig
            _agentConfig.Radius       // ← Use AgentConfig
        );
        
        if (validation.IsValid)
        {
            // Path is valid - return it
            return pathResult;
        }
        
        // Path validation failed
        Console.WriteLine($"[PathfindingService] Path validation FAILED: {validation.RejectionReason}");
        Console.WriteLine($"[PathfindingService] Path statistics: " +
            $"Segments={validation.Statistics.SegmentCount}, " +
            $"Length={validation.Statistics.TotalLength:F2}m, " +
            $"MaxClimb={validation.Statistics.MaxSegmentClimb:F2}m, " +
            $"MaxSlope={validation.Statistics.MaxSegmentSlope:F1}°");
        
        // Try to fix the path if auto-fix is enabled
        if (_config.EnablePathAutoFix)
        {
            Console.WriteLine($"[PathfindingService] Attempting to auto-fix path...");
            var fixedWaypoints = _pathValidator.TryFixPath(
                pathResult.Waypoints,
                _agentConfig.MaxClimb,    // ← Use AgentConfig
                _agentConfig.MaxSlope     // ← Use AgentConfig
            );
            
            if (fixedWaypoints != null)
            {
                Console.WriteLine($"[PathfindingService] Path auto-fix SUCCEEDED " +
                    $"(original: {pathResult.Waypoints.Count} waypoints, fixed: {fixedWaypoints.Count} waypoints)");
                
                // Calculate new path length
                float newLength = 0;
                for (int i = 1; i < fixedWaypoints.Count; i++)
                {
                    newLength += Vector3.Distance(fixedWaypoints[i - 1], fixedWaypoints[i]);
                }
                
                return new PathResult(true, fixedWaypoints, newLength);
            }
            
            Console.WriteLine($"[PathfindingService] Path auto-fix FAILED");
        }
        
        // Return failure - path cannot be traversed safely
        return PathResult.Failed;
    }
    
    /// <summary>
    /// Checks if a position is valid (on the navigation mesh).
    /// </summary>
    /// <param name="extents">Optional search extents. If not provided, uses production defaults (5.0, 10.0, 5.0)</param>
    public bool IsValidPosition(Vector3 position, Vector3? extents = null)
    {
        var searchExtents = extents ?? new Vector3(5.0f, 10.0f, 5.0f);
        return _pathfinder.IsValidPosition(position, searchExtents);
    }
    
    /// <summary>
    /// Finds nearest valid navmesh position using downward-priority search.
    /// Searches in vertical column at XZ, preferring surfaces BELOW searchPoint.Y.
    /// This handles multi-layer navigation correctly (bridges, multi-floor buildings).
    /// 
    /// MULTI-LEVEL NAVIGATION STRATEGY:
    /// 
    /// When multiple navmesh surfaces exist at same XZ coordinate (bridges, buildings),
    /// this method uses DOWNWARD PRIORITY to select which surface:
    /// 
    /// 1. Search BELOW searchPoint.Y first (gravity-aligned)
    /// 2. Return closest surface BELOW (highest Y among surfaces below)
    /// 3. If no surface below, search ABOVE
    /// 4. Return closest surface ABOVE (lowest Y among surfaces above)
    /// 
    /// CONSISTENCY REQUIREMENT:
    /// Game server MUST use this same method when selecting movement targets.
    /// This ensures pathfinding and movement use same floor-level selection logic.
    /// 
    /// Example: Bridge at Y=5, Ground at Y=0
    /// - searchPoint.Y = 3 → Returns Y=0 (ground, below search point)
    /// - searchPoint.Y = 6 → Returns Y=5 (bridge, below search point)
    /// - searchPoint.Y = -2 → Returns Y=0 (ground, above search point, fallback)
    /// </summary>
    /// <param name="searchPoint">Search starting point (Y is search height/intention hint)</param>
    /// <param name="searchExtents">Search extents (±X, ±Y, ±Z from search point)</param>
    /// <returns>
    /// Corrected position on navmesh, or null if no valid surface found.
    /// Priority: First surface BELOW searchPoint.Y, else first surface ABOVE.
    /// </returns>
    public Vector3? FindNearestValidPosition(Vector3 searchPoint, Vector3 searchExtents)
    {
        Console.WriteLine($"[PathfindingService] FindNearestValidPosition at ({searchPoint.X:F2}, {searchPoint.Y:F2}, {searchPoint.Z:F2})");
        Console.WriteLine($"[PathfindingService]   Search extents: ({searchExtents.X:F2}, {searchExtents.Y:F2}, {searchExtents.Z:F2})");
        
        var query = _pathfinder.NavMeshData.Query;
        var filter = new DotRecast.Detour.DtQueryDefaultFilter();
        filter.SetIncludeFlags(0x01); // Include walkable flag
        filter.SetExcludeFlags(0); // No exclusions
        
        // Convert to DotRecast types
        var extentsVec = new DotRecast.Core.Numerics.RcVec3f(searchExtents.X, searchExtents.Y, searchExtents.Z);
        
        // Strategy: Search at multiple Y levels to find all surfaces in vertical column
        // Then select based on downward priority
        
        var surfacesBelow = new List<(float y, DotRecast.Core.Numerics.RcVec3f point)>();
        var surfacesAbove = new List<(float y, DotRecast.Core.Numerics.RcVec3f point)>();
        
        // Search downward first (priority)
        // Sample at intervals from searchPoint.Y downward
        float sampleStep = 0.5f; // Sample every 0.5 units
        float yBelow = searchPoint.Y;
        float minY = searchPoint.Y - searchExtents.Y;
        
        Console.WriteLine($"[PathfindingService]   Searching downward from Y={yBelow:F2} to Y={minY:F2}");
        
        // Search downward
        while (yBelow >= minY)
        {
            var testPoint = new DotRecast.Core.Numerics.RcVec3f(searchPoint.X, yBelow, searchPoint.Z);
            var found = query.FindNearestPoly(
                testPoint, 
                extentsVec, 
                filter, 
                out var polyRef, 
                out var nearestPt, 
                out var polyFound);
            
            if (polyFound)
            {
                // Found a surface at this Y level
                float surfaceY = nearestPt.Y;
                
                // Check if this is a new surface (not already found)
                bool isNewSurface = true;
                foreach (var existing in surfacesBelow)
                {
                    if (Math.Abs(existing.y - surfaceY) < 0.1f) // Within 10cm = same surface
                    {
                        isNewSurface = false;
                        break;
                    }
                }
                
                if (isNewSurface)
                {
                    surfacesBelow.Add((surfaceY, nearestPt));
                    // Found closest surface below - this is our priority result
                    // But continue searching to find all surfaces for fallback
                }
            }
            
            yBelow -= sampleStep;
        }
        
        // If we found surfaces below, return the closest one (highest Y among below)
        if (surfacesBelow.Count > 0)
        {
            Console.WriteLine($"[PathfindingService]   Found {surfacesBelow.Count} surface(s) below");
            // Sort by Y descending (highest Y = closest to search point)
            surfacesBelow.Sort((a, b) => b.y.CompareTo(a.y));
            var bestSurface = surfacesBelow[0];
            Console.WriteLine($"[PathfindingService]   → Returning surface at Y={bestSurface.y:F2}");
            return new Vector3(bestSurface.point.X, bestSurface.point.Y, bestSurface.point.Z);
        }
        
        Console.WriteLine($"[PathfindingService]   No surfaces found below, searching upward...");
        // No surfaces below, search upward as fallback
        float yAbove = searchPoint.Y + sampleStep;
        float maxY = searchPoint.Y + searchExtents.Y;
        
        while (yAbove <= maxY)
        {
            var testPoint = new DotRecast.Core.Numerics.RcVec3f(searchPoint.X, yAbove, searchPoint.Z);
            var found = query.FindNearestPoly(
                testPoint, 
                extentsVec, 
                filter, 
                out var polyRef, 
                out var nearestPt, 
                out var polyFound);
            
            if (polyFound)
            {
                float surfaceY = nearestPt.Y;
                
                // Check if this is a new surface
                bool isNewSurface = true;
                foreach (var existing in surfacesAbove)
                {
                    if (Math.Abs(existing.y - surfaceY) < 0.1f)
                    {
                        isNewSurface = false;
                        break;
                    }
                }
                
                if (isNewSurface)
                {
                    surfacesAbove.Add((surfaceY, nearestPt));
                    // Found closest surface above - return immediately
                    return new Vector3(nearestPt.X, nearestPt.Y, nearestPt.Z);
                }
            }
            
            yAbove += sampleStep;
        }
        
        // No surfaces found at all
        Console.WriteLine($"[PathfindingService]   → No navmesh found anywhere in search extents");
        return null;
    }
}

