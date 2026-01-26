using System;
using System.Collections.Generic;
using System.Numerics;

namespace Spatial.Integration;

/// <summary>
/// Validates paths returned by DotRecast to ensure they are physically traversable
/// by agents with specific movement constraints (MaxClimb, MaxSlope).
/// 
/// PROBLEM: DotRecast generates paths based on navmesh polygon connectivity,
/// but doesn't validate that straight-line segments between waypoints are physically
/// traversable. This can result in paths with:
/// - Large vertical jumps between waypoints (exceeding MaxClimb)
/// - Steep slopes on segment lines (exceeding MaxSlope)
/// - Cumulative vertical ascent that appears walkable per-polygon but not per-segment
/// 
/// SOLUTION: Post-process paths to validate segment constraints before accepting them.
/// </summary>
public class PathSegmentValidator
{
    /// <summary>
    /// Result of path validation.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether the path is valid and can be traversed.
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// Human-readable reason for rejection (if IsValid=false).
        /// </summary>
        public string? RejectionReason { get; set; }
        
        /// <summary>
        /// Index of first violating segment (if any).
        /// </summary>
        public int? ViolatingSegmentIndex { get; set; }
        
        /// <summary>
        /// Statistics about the path for diagnostics.
        /// </summary>
        public PathStatistics Statistics { get; set; } = new();
    }
    
    /// <summary>
    /// Statistics about a path.
    /// </summary>
    public class PathStatistics
    {
        public float TotalLength { get; set; }
        public float TotalVerticalChange { get; set; }
        public float MaxSegmentClimb { get; set; }
        public float MaxSegmentSlope { get; set; }
        public int SegmentCount { get; set; }
    }
    
    /// <summary>
    /// Validates that a path is physically traversable by an agent with given constraints.
    /// </summary>
    /// <param name="waypoints">Path waypoints from DotRecast</param>
    /// <param name="maxClimb">Maximum vertical distance agent can climb in one segment (units)</param>
    /// <param name="maxSlope">Maximum slope agent can walk on (degrees)</param>
    /// <param name="agentRadius">Agent radius for collision checks (units)</param>
    /// <returns>Validation result with details</returns>
    public ValidationResult ValidatePath(
        IReadOnlyList<Vector3> waypoints, 
        float maxClimb, 
        float maxSlope,
        float agentRadius = 0.5f)
    {
        var result = new ValidationResult 
        { 
            IsValid = true,
            Statistics = new PathStatistics()
        };
        
        if (waypoints == null || waypoints.Count < 2)
        {
            result.IsValid = false;
            result.RejectionReason = "Path has fewer than 2 waypoints";
            return result;
        }
        
        // Analyze each segment
        result.Statistics.SegmentCount = waypoints.Count - 1;
        
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var current = waypoints[i];
            var next = waypoints[i + 1];
            
            // Calculate segment properties
            var delta = next - current;
            float horizontalDistance = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
            float verticalDistance = delta.Y;
            float totalDistance = delta.Length();
            
            // Update statistics
            result.Statistics.TotalLength += totalDistance;
            result.Statistics.TotalVerticalChange += Math.Abs(verticalDistance);
            
            // Check 1: Maximum climb constraint (absolute vertical distance)
            float segmentClimb = Math.Abs(verticalDistance);
            if (segmentClimb > result.Statistics.MaxSegmentClimb)
            {
                result.Statistics.MaxSegmentClimb = segmentClimb;
            }
            
            if (segmentClimb > maxClimb)
            {
                result.IsValid = false;
                result.RejectionReason = 
                    $"Segment {i}→{i+1} exceeds MaxClimb: {segmentClimb:F2}m > {maxClimb:F2}m " +
                    $"(from Y={current.Y:F2} to Y={next.Y:F2})";
                result.ViolatingSegmentIndex = i;
                return result;
            }
            
            // Check 2: Maximum slope constraint (angle from horizontal)
            // Only check if there's meaningful horizontal distance (avoid division by zero)
            if (horizontalDistance > 0.01f)
            {
                float slopeRadians = MathF.Atan2(Math.Abs(verticalDistance), horizontalDistance);
                float slopeDegrees = slopeRadians * (180.0f / MathF.PI);
                
                if (slopeDegrees > result.Statistics.MaxSegmentSlope)
                {
                    result.Statistics.MaxSegmentSlope = slopeDegrees;
                }
                
                if (slopeDegrees > maxSlope)
                {
                    result.IsValid = false;
                    result.RejectionReason = 
                        $"Segment {i}→{i+1} exceeds MaxSlope: {slopeDegrees:F1}° > {maxSlope:F1}° " +
                        $"(vertical: {verticalDistance:F2}m, horizontal: {horizontalDistance:F2}m)";
                    result.ViolatingSegmentIndex = i;
                    return result;
                }
            }
            else
            {
                // Pure vertical segment - check if it's within climb limit
                // (already checked above, but flag as vertical)
                if (segmentClimb > maxClimb)
                {
                    result.IsValid = false;
                    result.RejectionReason = 
                        $"Segment {i}→{i+1} is pure vertical jump: {segmentClimb:F2}m > {maxClimb:F2}m";
                    result.ViolatingSegmentIndex = i;
                    return result;
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Attempts to fix an invalid path by splitting segments that violate constraints.
    /// This is a best-effort approach - not guaranteed to find a valid path.
    /// </summary>
    /// <param name="waypoints">Original path waypoints</param>
    /// <param name="maxClimb">Maximum vertical distance agent can climb</param>
    /// <param name="maxSlope">Maximum slope agent can walk on (degrees)</param>
    /// <returns>Modified path with intermediate waypoints added, or null if unfixable</returns>
    public List<Vector3>? TryFixPath(
        IReadOnlyList<Vector3> waypoints,
        float maxClimb,
        float maxSlope)
    {
        // Simple implementation: Try to insert intermediate waypoints
        // More sophisticated version would re-query navmesh for alternative routes
        
        var fixedPath = new List<Vector3>();
        
        if (waypoints.Count < 2)
            return null;
            
        fixedPath.Add(waypoints[0]);
        
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var current = waypoints[i];
            var next = waypoints[i + 1];
            var delta = next - current;
            
            float verticalDistance = Math.Abs(delta.Y);
            
            // If segment violates climb constraint, try to split it
            if (verticalDistance > maxClimb)
            {
                // Calculate how many intermediate points we need
                int splits = (int)Math.Ceiling(verticalDistance / maxClimb);
                
                // Add intermediate waypoints
                for (int j = 1; j < splits; j++)
                {
                    float t = (float)j / splits;
                    var intermediate = Vector3.Lerp(current, next, t);
                    fixedPath.Add(intermediate);
                }
            }
            
            fixedPath.Add(next);
        }
        
        // Validate the fixed path
        var validation = ValidatePath(fixedPath, maxClimb, maxSlope);
        
        return validation.IsValid ? fixedPath : null;
    }
}
