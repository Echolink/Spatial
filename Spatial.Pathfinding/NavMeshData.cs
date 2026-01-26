using DotRecast.Detour;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Spatial.Pathfinding;

/// <summary>
/// Container for navigation mesh data.
/// The NavMesh is generated once and reused for multiple pathfinding queries.
/// </summary>
public class NavMeshData
{
    /// <summary>
    /// The Detour navigation mesh.
    /// This is what we query for paths.
    /// </summary>
    public DtNavMesh NavMesh { get; }
    
    /// <summary>
    /// Query object for finding paths on the NavMesh.
    /// </summary>
    public DtNavMeshQuery Query { get; }
    
    public NavMeshData(DtNavMesh navMesh, DtNavMeshQuery query)
    {
        NavMesh = navMesh;
        Query = query;
    }
    
    /// <summary>
    /// Exports the navigation mesh to an OBJ file for visualization and analysis.
    /// Each polygon is exported as a face with its actual vertices.
    /// </summary>
    public void ExportToObj(string filepath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Navigation Mesh Export");
        sb.AppendLine($"# Generated: {DateTime.Now}");
        sb.AppendLine();
        
        int vertexOffset = 1; // OBJ indices start at 1
        
        // Iterate through all tiles in the navmesh
        for (int i = 0; i < NavMesh.GetMaxTiles(); i++)
        {
            var tile = NavMesh.GetTile(i);
            if (tile == null || tile.data == null) continue;
            
            var data = tile.data;
            
            sb.AppendLine($"# Tile {i}: {data.header.polyCount} polygons");
            
            // Export vertices for this tile
            for (int v = 0; v < data.header.vertCount; v++)
            {
                float x = data.verts[v * 3];
                float y = data.verts[v * 3 + 1];
                float z = data.verts[v * 3 + 2];
                sb.AppendLine($"v {x:F6} {y:F6} {z:F6}");
            }
            
            // Export polygons as faces
            for (int p = 0; p < data.header.polyCount; p++)
            {
                var poly = data.polys[p];
                
                // Check if polygon is walkable (has flag 0x01)
                bool isWalkable = (poly.flags & 0x01) != 0;
                
                sb.Append($"f");
                for (int v = 0; v < poly.vertCount; v++)
                {
                    int vertIndex = poly.verts[v];
                    sb.Append($" {vertIndex + vertexOffset}");
                }
                sb.AppendLine($" # poly{p} area={poly.GetArea()} flags={poly.flags} {(isWalkable ? "WALKABLE" : "NON-WALKABLE")}");
            }
            
            vertexOffset += data.header.vertCount;
            sb.AppendLine();
        }
        
        File.WriteAllText(filepath, sb.ToString());
        Console.WriteLine($"[NavMeshData] Exported navmesh to: {filepath}");
    }
    
    /// <summary>
    /// Analyzes the slope of polygons in the navmesh by examining their detail triangles.
    /// Returns statistics about the walkable surfaces.
    /// </summary>
    public NavMeshSlopeAnalysis AnalyzeSlopes()
    {
        var slopes = new System.Collections.Generic.List<float>();
        var yValues = new System.Collections.Generic.List<float>();
        
        for (int i = 0; i < NavMesh.GetMaxTiles(); i++)
        {
            var tile = NavMesh.GetTile(i);
            if (tile == null || tile.data == null) continue;
            
            var data = tile.data;
            
            // Analyze each polygon
            for (int p = 0; p < data.header.polyCount; p++)
            {
                var poly = data.polys[p];
                
                // Only analyze walkable polygons
                if ((poly.flags & 0x01) == 0) continue;
                
                // Get polygon vertices to calculate Y range
                for (int v = 0; v < poly.vertCount; v++)
                {
                    int vertIndex = poly.verts[v];
                    float y = data.verts[vertIndex * 3 + 1];
                    yValues.Add(y);
                }
                
                // Calculate slope from detail triangles if available
                if (data.detailMeshes != null && data.detailTris != null && data.detailVerts != null)
                {
                    var detailMesh = data.detailMeshes[p];
                    int triBase = detailMesh.triBase;
                    int triCount = detailMesh.triCount;
                    
                    for (int t = 0; t < triCount; t++)
                    {
                        int triIndex = (triBase + t) * 4;
                        
                        // Get triangle vertices
                        var v0 = GetDetailVertex(data, poly, detailMesh, data.detailTris[triIndex]);
                        var v1 = GetDetailVertex(data, poly, detailMesh, data.detailTris[triIndex + 1]);
                        var v2 = GetDetailVertex(data, poly, detailMesh, data.detailTris[triIndex + 2]);
                        
                        // Calculate normal
                        var edge1 = new System.Numerics.Vector3(v1.X - v0.X, v1.Y - v0.Y, v1.Z - v0.Z);
                        var edge2 = new System.Numerics.Vector3(v2.X - v0.X, v2.Y - v0.Y, v2.Z - v0.Z);
                        var normal = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(edge1, edge2));
                        
                        // Calculate slope angle from vertical (up = Y+)
                        float slopeAngle = (float)(Math.Acos(Math.Abs(normal.Y)) * 180.0 / Math.PI);
                        slopes.Add(slopeAngle);
                    }
                }
            }
        }
        
        if (slopes.Count == 0)
        {
            return new NavMeshSlopeAnalysis
            {
                PolyCount = 0,
                MinSlope = 0,
                MaxSlope = 0,
                AvgSlope = 0,
                MinY = 0,
                MaxY = 0
            };
        }
        
        return new NavMeshSlopeAnalysis
        {
            PolyCount = slopes.Count,
            MinSlope = slopes.Min(),
            MaxSlope = slopes.Max(),
            AvgSlope = slopes.Average(),
            MinY = yValues.Min(),
            MaxY = yValues.Max()
        };
    }
    
    private System.Numerics.Vector3 GetDetailVertex(DtMeshData data, DtPoly poly, DtPolyDetail detailMesh, int vertIndex)
    {
        if (vertIndex < poly.vertCount)
        {
            // Reference to polygon vertex
            int polyVertIndex = poly.verts[vertIndex];
            return new System.Numerics.Vector3(
                data.verts[polyVertIndex * 3],
                data.verts[polyVertIndex * 3 + 1],
                data.verts[polyVertIndex * 3 + 2]
            );
        }
        else
        {
            // Reference to detail vertex
            int detailVertIndex = (detailMesh.vertBase + vertIndex - poly.vertCount) * 3;
            return new System.Numerics.Vector3(
                data.detailVerts[detailVertIndex],
                data.detailVerts[detailVertIndex + 1],
                data.detailVerts[detailVertIndex + 2]
            );
        }
    }
}

public class NavMeshSlopeAnalysis
{
    public int PolyCount { get; set; }
    public float MinSlope { get; set; }
    public float MaxSlope { get; set; }
    public float AvgSlope { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
}

