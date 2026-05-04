using DotRecast.Detour;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Spatial.Pathfinding;

/// <summary>
/// Container for navigation mesh data.
/// The NavMesh is generated once and reused for multiple pathfinding queries.
/// When <see cref="IsMultiTile"/> is true individual tiles can be rebuilt at runtime.
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
    /// Recreated automatically after tile updates via <see cref="InvalidateQuery"/>.
    /// </summary>
    public DtNavMeshQuery Query { get; private set; }

    /// <summary>
    /// True if the NavMesh was built with multi-tile support (EnableTileUpdates = true).
    /// </summary>
    public bool IsMultiTile { get; }

    /// <summary>
    /// World-space tile size used during multi-tile generation.
    /// Zero for monolithic (single-tile) meshes.
    /// </summary>
    public float TileSize { get; }

    /// <summary>
    /// Source vertex data (x,y,z triplets) used to build this tiled NavMesh.
    /// Non-null only when <see cref="IsMultiTile"/> is true.
    /// Used by <see cref="Integration.PathfindingService.RebuildNavMeshRegion"/> to restore
    /// tiles after a dynamic obstacle (e.g. a tree resource node) is removed.
    /// </summary>
    public float[]? SourceVertices { get; }

    /// <summary>
    /// Source triangle indices used to build this tiled NavMesh.
    /// Non-null only when <see cref="IsMultiTile"/> is true.
    /// </summary>
    public int[]? SourceIndices { get; }

    /// <summary>
    /// Tile configuration used when this NavMesh was generated.
    /// Required when calling <see cref="Integration.PathfindingService.RebuildNavMeshRegion"/>.
    /// Non-null only when <see cref="IsMultiTile"/> is true.
    /// </summary>
    public NavMeshConfiguration? NavConfig { get; }

    /// <summary>
    /// World-space X origin of the tiled NavMesh (the minimum X of the source geometry bounding box).
    /// Used to correctly map world positions to tile coordinates during runtime tile rebuilds.
    /// Zero for monolithic meshes.
    /// </summary>
    public float TileOriginX { get; }

    /// <summary>
    /// World-space Z origin of the tiled NavMesh (the minimum Z of the source geometry bounding box).
    /// </summary>
    public float TileOriginZ { get; }

    /// <summary>World Y floor used when building the tiled NavMesh (bmin.Y including padding).</summary>
    public float WorldBminY { get; }

    /// <summary>World Y ceiling used when building the tiled NavMesh (bmax.Y including padding).</summary>
    public float WorldBmaxY { get; }

    /// <summary>
    /// Off-mesh link definitions used when this tiled NavMesh was built.
    /// Re-baked into each tile during runtime tile rebuilds so links survive obstacle updates.
    /// Non-null only when <see cref="IsMultiTile"/> is true and links were provided.
    /// </summary>
    public IReadOnlyList<OffMeshLinkDef>? OffMeshLinks { get; }

    public NavMeshData(DtNavMesh navMesh, DtNavMeshQuery query,
        bool isMultiTile = false, float tileSize = 0f,
        float[]? sourceVertices = null, int[]? sourceIndices = null,
        NavMeshConfiguration? navConfig = null,
        float tileOriginX = 0f, float tileOriginZ = 0f,
        float worldBminY = -1000f, float worldBmaxY = 1000f,
        IReadOnlyList<OffMeshLinkDef>? offMeshLinks = null)
    {
        NavMesh = navMesh;
        Query = query;
        IsMultiTile = isMultiTile;
        TileSize = tileSize;
        SourceVertices = sourceVertices;
        SourceIndices = sourceIndices;
        NavConfig = navConfig;
        TileOriginX = tileOriginX;
        TileOriginZ = tileOriginZ;
        WorldBminY = worldBminY;
        WorldBmaxY = worldBmaxY;
        OffMeshLinks = offMeshLinks;
    }

    /// <summary>
    /// Recreates the <see cref="DtNavMeshQuery"/> after a tile has been added or removed.
    /// Call this after every <see cref="DtNavMesh.AddTile"/> or <see cref="DtNavMesh.RemoveTile"/>.
    /// </summary>
    public void InvalidateQuery()
    {
        Query = new DtNavMeshQuery(NavMesh);
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

