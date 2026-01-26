using Spatial.Physics;
using Spatial.Pathfinding;
using System.Numerics;
using System.Text;
using DotRecast.Detour;

namespace Spatial.Integration.Diagnostics;

/// <summary>
/// Diagnostic utility to compare how BepuPhysics and DotRecast interpret mesh data.
/// Used to identify discrepancies between physics collision and navmesh representations.
/// </summary>
public class MeshDiagnostics
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly Pathfinder _pathfinder;
    
    public MeshDiagnostics(PhysicsWorld physicsWorld, Pathfinder pathfinder)
    {
        _physicsWorld = physicsWorld;
        _pathfinder = pathfinder;
    }
    
    /// <summary>
    /// Analyzes mesh representation in a specific area (used for Agent-3's path).
    /// </summary>
    public MeshAnalysisReport AnalyzeArea(Vector3 center, float radius, string areaName = "Unknown")
    {
        var report = new MeshAnalysisReport
        {
            AreaName = areaName,
            Center = center,
            Radius = radius,
            AnalysisTime = DateTime.UtcNow
        };
        
        // Analyze physics collision mesh in this area
        report.PhysicsTriangles = AnalyzePhysicsMesh(center, radius);
        
        // Analyze navmesh polygons in this area
        report.NavMeshPolygons = AnalyzeNavMesh(center, radius);
        
        // Compare and identify discrepancies
        report.Discrepancies = FindDiscrepancies(report.PhysicsTriangles, report.NavMeshPolygons);
        
        return report;
    }
    
    /// <summary>
    /// Analyzes physics collision mesh triangles in a specific area.
    /// </summary>
    private List<PhysicsTriangleInfo> AnalyzePhysicsMesh(Vector3 center, float radius)
    {
        var triangles = new List<PhysicsTriangleInfo>();
        
        // Get all static entities (terrain)
        var staticEntities = _physicsWorld.EntityRegistry.GetAllEntities()
            .Where(e => e.IsStatic)
            .ToList();
        
        foreach (var entity in staticEntities)
        {
            var meshData = _physicsWorld.GetMeshData(entity.EntityId);
            if (meshData == null) continue;
            
            var (vertices, indices, navMeshArea) = meshData.Value;
            
            // Check each triangle if it's within the analysis radius
            for (int i = 0; i < indices.Length; i += 3)
            {
                var idx0 = indices[i];
                var idx1 = indices[i + 1];
                var idx2 = indices[i + 2];
                
                var v0 = vertices[idx0];
                var v1 = vertices[idx1];
                var v2 = vertices[idx2];
                
                // Calculate triangle center
                var triCenter = (v0 + v1 + v2) / 3.0f;
                
                // Check if within radius
                if (Vector3.Distance(triCenter, center) <= radius)
                {
                    // Calculate normal
                    var edge1 = v1 - v0;
                    var edge2 = v2 - v0;
                    var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
                    
                    // Calculate slope (angle from vertical)
                    var slopeDegrees = MathF.Acos(Vector3.Dot(normal, Vector3.UnitY)) * (180.0f / MathF.PI);
                    
                    triangles.Add(new PhysicsTriangleInfo
                    {
                        Vertex0 = v0,
                        Vertex1 = v1,
                        Vertex2 = v2,
                        Normal = normal,
                        SlopeDegrees = slopeDegrees,
                        Center = triCenter,
                        EntityId = entity.EntityId
                    });
                }
            }
        }
        
        return triangles;
    }
    
    /// <summary>
    /// Analyzes navmesh polygons in a specific area.
    /// </summary>
    private List<NavMeshPolygonInfo> AnalyzeNavMesh(Vector3 center, float radius)
    {
        var polygons = new List<NavMeshPolygonInfo>();
        
        var query = _pathfinder.NavMeshData.Query;
        var mesh = _pathfinder.NavMeshData.NavMesh;
        
        // Sample points in a grid around the center
        int sampleGridSize = 20;
        float step = (radius * 2.0f) / sampleGridSize;
        
        for (int x = 0; x < sampleGridSize; x++)
        {
            for (int z = 0; z < sampleGridSize; z++)
            {
                var samplePoint = new Vector3(
                    center.X - radius + (x * step),
                    center.Y,
                    center.Z - radius + (z * step)
                );
                
                // Find nearest poly
                var searchExtents = new DotRecast.Core.Numerics.RcVec3f(2.0f, 10.0f, 2.0f);
                var testPoint = new DotRecast.Core.Numerics.RcVec3f(samplePoint.X, samplePoint.Y, samplePoint.Z);
                var filter = new DotRecast.Detour.DtQueryDefaultFilter();
                filter.SetIncludeFlags(0x01);
                
                var found = query.FindNearestPoly(
                    testPoint,
                    searchExtents,
                    filter,
                    out var polyRef,
                    out var nearestPt,
                    out var polyFound
                );
                
                if (polyFound && polyRef != 0)
                {
                    // Check if we've already added this polygon
                    if (polygons.Any(p => p.PolyRef == polyRef))
                        continue;
                    
                    // Get polygon data
                    var tile = mesh.GetTileByRef(polyRef);
                    if (tile != null)
                    {
                        int polyIndex = DotRecast.Detour.DtDetour.DecodePolyIdPoly(polyRef);
                        if (polyIndex >= 0 && polyIndex < tile.data.polys.Length)
                        {
                            var poly = tile.data.polys[polyIndex];
                            
                            // Get vertices
                            var vertices = new List<Vector3>();
                            for (int i = 0; i < poly.vertCount; i++)
                            {
                                var vIdx = poly.verts[i] * 3;
                                vertices.Add(new Vector3(
                                    tile.data.verts[vIdx],
                                    tile.data.verts[vIdx + 1],
                                    tile.data.verts[vIdx + 2]
                                ));
                            }
                            
                            // Calculate center
                            var polyCenter = vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / vertices.Count;
                            
                            // Calculate approximate normal (from first triangle)
                            Vector3 normal = Vector3.UnitY; // Default
                            if (vertices.Count >= 3)
                            {
                                var edge1 = vertices[1] - vertices[0];
                                var edge2 = vertices[2] - vertices[0];
                                normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
                            }
                            
                            var slopeDegrees = MathF.Acos(Vector3.Dot(normal, Vector3.UnitY)) * (180.0f / MathF.PI);
                            
                            polygons.Add(new NavMeshPolygonInfo
                            {
                                PolyRef = polyRef,
                                Vertices = vertices,
                                Normal = normal,
                                SlopeDegrees = slopeDegrees,
                                Center = polyCenter,
                                AreaType = poly.GetArea()
                            });
                        }
                    }
                }
            }
        }
        
        return polygons;
    }
    
    /// <summary>
    /// Finds discrepancies between physics and navmesh representations.
    /// </summary>
    private List<MeshDiscrepancy> FindDiscrepancies(
        List<PhysicsTriangleInfo> physicsTriangles,
        List<NavMeshPolygonInfo> navMeshPolygons)
    {
        var discrepancies = new List<MeshDiscrepancy>();
        
        // Check 1: Compare coverage
        float physicsMinY = physicsTriangles.Any() ? physicsTriangles.Min(t => Math.Min(t.Vertex0.Y, Math.Min(t.Vertex1.Y, t.Vertex2.Y))) : 0;
        float physicsMaxY = physicsTriangles.Any() ? physicsTriangles.Max(t => Math.Max(t.Vertex0.Y, Math.Max(t.Vertex1.Y, t.Vertex2.Y))) : 0;
        
        float navMeshMinY = navMeshPolygons.Any() ? navMeshPolygons.Min(p => p.Vertices.Min(v => v.Y)) : 0;
        float navMeshMaxY = navMeshPolygons.Any() ? navMeshPolygons.Max(p => p.Vertices.Max(v => v.Y)) : 0;
        
        if (Math.Abs(physicsMinY - navMeshMinY) > 0.1f || Math.Abs(physicsMaxY - navMeshMaxY) > 0.1f)
        {
            var severity = Math.Abs(physicsMaxY - navMeshMaxY) > 5.0f 
                ? DiscrepancySeverity.Critical 
                : DiscrepancySeverity.Warning;
                
            discrepancies.Add(new MeshDiscrepancy
            {
                Type = DiscrepancyType.VerticalRangeMismatch,
                Severity = severity,
                Description = $"Vertical range mismatch: Physics=[{physicsMinY:F2}, {physicsMaxY:F2}], NavMesh=[{navMeshMinY:F2}, {navMeshMaxY:F2}]. " +
                             $"NavMesh may be missing {(physicsMaxY - navMeshMaxY):F2}m of vertical coverage!"
            });
        }
        
        // Check 2: Compare slope interpretations
        var physicsSlopeAvg = physicsTriangles.Any() ? physicsTriangles.Average(t => t.SlopeDegrees) : 0;
        var navMeshSlopeAvg = navMeshPolygons.Any() ? navMeshPolygons.Average(p => p.SlopeDegrees) : 0;
        
        if (Math.Abs(physicsSlopeAvg - navMeshSlopeAvg) > 20.0f)
        {
            // Large slope difference is CRITICAL - indicates navmesh doesn't match physics reality
            discrepancies.Add(new MeshDiscrepancy
            {
                Type = DiscrepancyType.SlopeInterpretationDifference,
                Severity = DiscrepancySeverity.Critical,
                Description = $"SEVERE slope mismatch: Physics={physicsSlopeAvg:F1}° (steep/vertical), NavMesh={navMeshSlopeAvg:F1}° (walkable). " +
                             $"NavMesh is treating vertical geometry as walkable slopes - paths may be impossible!"
            });
        }
        else if (Math.Abs(physicsSlopeAvg - navMeshSlopeAvg) > 5.0f)
        {
            discrepancies.Add(new MeshDiscrepancy
            {
                Type = DiscrepancyType.SlopeInterpretationDifference,
                Severity = DiscrepancySeverity.Warning,
                Description = $"Average slope differs: Physics={physicsSlopeAvg:F1}°, NavMesh={navMeshSlopeAvg:F1}°"
            });
        }
        
        // Check 3: Double-sided vs single-sided
        var physicsTriangleCount = physicsTriangles.Count;
        var navMeshPolyCount = navMeshPolygons.Count;
        
        // If physics has roughly 2x triangles, might be double-sided issue
        if (physicsTriangleCount > navMeshPolyCount * 1.8f)
        {
            discrepancies.Add(new MeshDiscrepancy
            {
                Type = DiscrepancyType.DoubleSidedMesh,
                Severity = DiscrepancySeverity.Info,
                Description = $"Physics may be using double-sided mesh: {physicsTriangleCount} triangles vs {navMeshPolyCount} navmesh polygons"
            });
        }
        
        return discrepancies;
    }
    
    /// <summary>
    /// Prints a detailed report to console.
    /// </summary>
    public static void PrintReport(MeshAnalysisReport report)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║ MESH DIAGNOSTICS REPORT: {report.AreaName,-32} ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Analysis Center: ({report.Center.X:F2}, {report.Center.Y:F2}, {report.Center.Z:F2})");
        Console.WriteLine($"Analysis Radius: {report.Radius:F2}m");
        Console.WriteLine($"Analysis Time: {report.AnalysisTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();
        
        // Physics mesh summary
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine("BEPUPHYSICS COLLISION MESH");
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Total Triangles: {report.PhysicsTriangles.Count}");
        
        if (report.PhysicsTriangles.Any())
        {
            var minY = report.PhysicsTriangles.Min(t => Math.Min(t.Vertex0.Y, Math.Min(t.Vertex1.Y, t.Vertex2.Y)));
            var maxY = report.PhysicsTriangles.Max(t => Math.Max(t.Vertex0.Y, Math.Max(t.Vertex1.Y, t.Vertex2.Y)));
            var avgSlope = report.PhysicsTriangles.Average(t => t.SlopeDegrees);
            
            Console.WriteLine($"  Y Range: [{minY:F2}, {maxY:F2}]");
            Console.WriteLine($"  Average Slope: {avgSlope:F2}°");
            Console.WriteLine($"  Slope Range: [{report.PhysicsTriangles.Min(t => t.SlopeDegrees):F2}°, {report.PhysicsTriangles.Max(t => t.SlopeDegrees):F2}°]");
        }
        Console.WriteLine();
        
        // NavMesh summary
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine("DOTRECAST NAVIGATION MESH");
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Total Polygons: {report.NavMeshPolygons.Count}");
        
        if (report.NavMeshPolygons.Any())
        {
            var minY = report.NavMeshPolygons.Min(p => p.Vertices.Min(v => v.Y));
            var maxY = report.NavMeshPolygons.Max(p => p.Vertices.Max(v => v.Y));
            var avgSlope = report.NavMeshPolygons.Average(p => p.SlopeDegrees);
            
            Console.WriteLine($"  Y Range: [{minY:F2}, {maxY:F2}]");
            Console.WriteLine($"  Average Slope: {avgSlope:F2}°");
            Console.WriteLine($"  Slope Range: [{report.NavMeshPolygons.Min(p => p.SlopeDegrees):F2}°, {report.NavMeshPolygons.Max(p => p.SlopeDegrees):F2}°]");
        }
        Console.WriteLine();
        
        // Discrepancies
        if (report.Discrepancies.Any())
        {
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            Console.WriteLine("DISCREPANCIES DETECTED");
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            
            foreach (var discrepancy in report.Discrepancies)
            {
                var severitySymbol = discrepancy.Severity switch
                {
                    DiscrepancySeverity.Critical => "🔴",
                    DiscrepancySeverity.Warning => "⚠️",
                    DiscrepancySeverity.Info => "ℹ️",
                    _ => "  "
                };
                
                Console.WriteLine($"  {severitySymbol} [{discrepancy.Severity}] {discrepancy.Type}");
                Console.WriteLine($"     {discrepancy.Description}");
            }
        }
        else
        {
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            Console.WriteLine("✅ NO SIGNIFICANT DISCREPANCIES DETECTED");
            Console.WriteLine("─────────────────────────────────────────────────────────────");
        }
        
        Console.WriteLine();
    }
}

/// <summary>
/// Report containing mesh analysis results.
/// </summary>
public class MeshAnalysisReport
{
    public string AreaName { get; set; } = string.Empty;
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
    public DateTime AnalysisTime { get; set; }
    public List<PhysicsTriangleInfo> PhysicsTriangles { get; set; } = new();
    public List<NavMeshPolygonInfo> NavMeshPolygons { get; set; } = new();
    public List<MeshDiscrepancy> Discrepancies { get; set; } = new();
}

/// <summary>
/// Information about a physics collision triangle.
/// </summary>
public class PhysicsTriangleInfo
{
    public Vector3 Vertex0 { get; set; }
    public Vector3 Vertex1 { get; set; }
    public Vector3 Vertex2 { get; set; }
    public Vector3 Normal { get; set; }
    public float SlopeDegrees { get; set; }
    public Vector3 Center { get; set; }
    public int EntityId { get; set; }
}

/// <summary>
/// Information about a navmesh polygon.
/// </summary>
public class NavMeshPolygonInfo
{
    public long PolyRef { get; set; }
    public List<Vector3> Vertices { get; set; } = new();
    public Vector3 Normal { get; set; }
    public float SlopeDegrees { get; set; }
    public Vector3 Center { get; set; }
    public int AreaType { get; set; }
}

/// <summary>
/// Represents a discrepancy between physics and navmesh.
/// </summary>
public class MeshDiscrepancy
{
    public DiscrepancyType Type { get; set; }
    public DiscrepancySeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
}

public enum DiscrepancyType
{
    VerticalRangeMismatch,
    SlopeInterpretationDifference,
    DoubleSidedMesh,
    NormalDirectionMismatch,
    CoverageMismatch
}

public enum DiscrepancySeverity
{
    Info,
    Warning,
    Critical
}
