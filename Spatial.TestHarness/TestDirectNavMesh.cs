using Spatial.Pathfinding;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;
using System;
using System.Numerics;
using System.IO;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using DotRecast.Detour;
using DotRecast.Core.Numerics;

namespace Spatial.TestHarness;

/// <summary>
/// Test direct navmesh generation from OBJ file using DotRecast's recommended approach.
/// This bypasses our physics system to compare results with DotRecast Demo.
/// </summary>
public static class TestDirectNavMesh
{
    /// <summary>
    /// Generates navmesh directly from OBJ file using DotRecast.
    /// This follows the recommended approach from DotRecast Demo.
    /// </summary>
    public static void Run(string objFilePath, string outputPath)
    {
        Console.WriteLine("=== DIRECT NAVMESH GENERATION TEST ===");
        Console.WriteLine($"Input: {objFilePath}");
        Console.WriteLine($"Output: {outputPath}\n");
        
        try
        {
            // Step 1: Load OBJ file
            Console.WriteLine("1. Loading OBJ file...");
            var meshLoader = new MeshLoader();
            var worldData = meshLoader.LoadWorld(objFilePath);
            
            if (worldData.Meshes.Count == 0)
            {
                Console.WriteLine("   ✗ No meshes found in OBJ file");
                return;
            }
            
            Console.WriteLine($"   ✓ Loaded {worldData.Meshes.Count} mesh(es)");
            foreach (var mesh in worldData.Meshes)
            {
                Console.WriteLine($"      - {mesh.Name}: {mesh.Vertices.Count} vertices, {mesh.TriangleCount} triangles");
            }
            Console.WriteLine();
            
            // Step 2: Convert mesh data to DotRecast format
            Console.WriteLine("2. Converting to DotRecast format...");
            var (vertices, indices) = ConvertMeshToArrays(worldData);
            Console.WriteLine($"   ✓ Total vertices: {vertices.Length / 3}");
            Console.WriteLine($"   ✓ Total triangles: {indices.Length / 3}\n");
            
            // Step 3: Create DotRecast input geometry
            Console.WriteLine("3. Creating input geometry provider...");
            var geomProvider = new SimpleInputGeomProvider(vertices, indices);
            Console.WriteLine($"   ✓ Bounds: min=({geomProvider.GetMeshBoundsMin().X:F2}, {geomProvider.GetMeshBoundsMin().Y:F2}, {geomProvider.GetMeshBoundsMin().Z:F2})");
            Console.WriteLine($"          max=({geomProvider.GetMeshBoundsMax().X:F2}, {geomProvider.GetMeshBoundsMax().Y:F2}, {geomProvider.GetMeshBoundsMax().Z:F2})\n");
            
            // Step 4: Configure navmesh generation (DotRecast recommended settings)
            Console.WriteLine("4. Configuring navmesh generation (DotRecast recommended)...");
            var agentHeight = 2.0f;
            var agentRadius = 0.4f;
            var agentMaxClimb = 0.5f;
            var agentMaxSlope = 45.0f;
            
            // Cell size: agent radius / 2 (recommended for outdoor)
            var cellSize = agentRadius / 2.0f;
            // Cell height: half of cell size (recommended)
            var cellHeight = cellSize / 2.0f;
            
            Console.WriteLine($"   Agent: height={agentHeight}, radius={agentRadius}, maxClimb={agentMaxClimb}, maxSlope={agentMaxSlope}");
            Console.WriteLine($"   Voxel: cellSize={cellSize}, cellHeight={cellHeight}");
            
            var bmin = geomProvider.GetMeshBoundsMin();
            var bmax = geomProvider.GetMeshBoundsMax();
            
            // Add padding to bounds (DotRecast recommended)
            bmin = new RcVec3f(bmin.X, bmin.Y - cellHeight, bmin.Z);
            bmax = new RcVec3f(bmax.X, bmax.Y + agentHeight * 2, bmax.Z);
            
            // Create RcConfig with DotRecast recommended settings
            var walkableAreaMod = new RcAreaModification(0x3f); // Walkable area
            
            var config = new RcConfig(
                RcPartition.WATERSHED,              // partitionType (recommended for most cases)
                cellSize,                            // cellSize
                cellHeight,                          // cellHeight
                agentMaxSlope,                       // agentMaxSlope
                agentHeight,                         // agentHeight
                agentRadius,                         // agentRadius
                agentMaxClimb,                       // agentMaxClimb
                1,                                   // regionMinSize (small geometry)
                4,                                   // regionMergeSize
                agentRadius * 8,                     // edgeMaxLen (recommended)
                1.3f,                                // edgeMaxError (recommended)
                6,                                   // vertsPerPoly
                cellSize * 6,                        // detailSampleDist
                cellHeight,                          // detailSampleMaxError
                true,                                // filterLowHangingObstacles
                true,                                // filterLedgeSpans
                true,                                // filterWalkableLowHeightSpans
                walkableAreaMod,                     // walkableAreaMod
                true                                 // buildMeshDetail
            );
            
            Console.WriteLine($"   ✓ Configuration created\n");
            
            // Step 5: Build navmesh using DotRecast RcBuilder
            Console.WriteLine("5. Building navmesh with DotRecast RcBuilder...");
            var builderConfig = new RcBuilderConfig(config, bmin, bmax);
            var builder = new RcBuilder();
            
            var buildResult = builder.Build(geomProvider, builderConfig, keepInterResults: true);
            
            if (buildResult == null)
            {
                Console.WriteLine("   ✗ Build failed");
                return;
            }
            
            Console.WriteLine($"   ✓ Build completed:");
            Console.WriteLine($"      - SolidHeightfield: {(buildResult.SolidHeightfiled != null ? "present" : "null")}");
            Console.WriteLine($"      - CompactHeightfield: {(buildResult.CompactHeightfield != null ? "present" : "null")}");
            Console.WriteLine($"      - ContourSet: {(buildResult.ContourSet?.conts?.Count ?? 0)} contours");
            Console.WriteLine($"      - Mesh: {(buildResult.Mesh != null ? $"{buildResult.Mesh.nverts} verts, {buildResult.Mesh.npolys} polys" : "null")}");
            Console.WriteLine();
            
            if (buildResult.Mesh == null || buildResult.Mesh.npolys == 0)
            {
                Console.WriteLine("   ⚠️  No walkable polygons generated");
                Console.WriteLine("   This usually means:");
                Console.WriteLine("      - No horizontal walkable surfaces found");
                Console.WriteLine("      - All surfaces are too steep or too small");
                Console.WriteLine("      - Cell size/height too large for geometry\n");
                return;
            }
            
            // Step 6: Create Detour navmesh
            Console.WriteLine("6. Creating Detour navmesh...");
            var navMesh = CreateDetourNavMesh(buildResult, config, agentHeight, agentRadius, agentMaxClimb);
            Console.WriteLine($"   ✓ NavMesh created with {navMesh.GetMaxTiles()} max tiles\n");
            
            // Step 7: Export navmesh to OBJ
            Console.WriteLine("7. Exporting navmesh to OBJ...");
            ExportNavMeshToObj(navMesh, outputPath);
            Console.WriteLine($"   ✓ Exported to: {Path.GetFullPath(outputPath)}\n");
            
            Console.WriteLine("✅ DIRECT NAVMESH GENERATION COMPLETED");
            Console.WriteLine("\nCompare this output with the physics-based approach to identify differences.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Converts WorldData mesh to flat vertex and index arrays.
    /// </summary>
    private static (float[] vertices, int[] indices) ConvertMeshToArrays(WorldData worldData)
    {
        var vertices = new System.Collections.Generic.List<float>();
        var indices = new System.Collections.Generic.List<int>();
        int vertexOffset = 0;
        
        foreach (var mesh in worldData.Meshes)
        {
            // Add vertices
            foreach (var vertex in mesh.Vertices)
            {
                vertices.Add(vertex.X);
                vertices.Add(vertex.Y);
                vertices.Add(vertex.Z);
            }
            
            // Add indices with offset
            for (int i = 0; i < mesh.Indices.Count; i++)
            {
                indices.Add(mesh.Indices[i] + vertexOffset);
            }
            
            vertexOffset += mesh.Vertices.Count;
        }
        
        return (vertices.ToArray(), indices.ToArray());
    }
    
    /// <summary>
    /// Creates Detour navmesh from Recast build result.
    /// </summary>
    private static DtNavMesh CreateDetourNavMesh(RcBuilderResult buildResult, RcConfig config, 
        float agentHeight, float agentRadius, float agentMaxClimb)
    {
        var mesh = buildResult.Mesh;
        var meshDetail = buildResult.MeshDetail;
        
        // Set walkable flags on all polygons
        const int WALKABLE_FLAG = 0x01;
        if (mesh.flags != null)
        {
            for (int i = 0; i < mesh.npolys; i++)
            {
                mesh.flags[i] = WALKABLE_FLAG;
            }
        }
        
        // Create navmesh data structure
        var navMeshCreateParams = new DtNavMeshCreateParams();
        navMeshCreateParams.verts = mesh.verts;
        navMeshCreateParams.vertCount = mesh.nverts;
        navMeshCreateParams.polys = mesh.polys;
        navMeshCreateParams.polyAreas = mesh.areas;
        navMeshCreateParams.polyFlags = mesh.flags;
        navMeshCreateParams.polyCount = mesh.npolys;
        navMeshCreateParams.nvp = mesh.nvp;
        
        if (meshDetail != null)
        {
            navMeshCreateParams.detailMeshes = meshDetail.meshes;
            navMeshCreateParams.detailVerts = meshDetail.verts;
            navMeshCreateParams.detailVertsCount = meshDetail.nverts;
            navMeshCreateParams.detailTris = meshDetail.tris;
            navMeshCreateParams.detailTriCount = meshDetail.ntris;
        }
        
        navMeshCreateParams.walkableHeight = agentHeight;
        navMeshCreateParams.walkableRadius = agentRadius;
        navMeshCreateParams.walkableClimb = agentMaxClimb;
        navMeshCreateParams.bmin = mesh.bmin;
        navMeshCreateParams.bmax = mesh.bmax;
        navMeshCreateParams.cs = config.Cs;
        navMeshCreateParams.ch = config.Ch;
        navMeshCreateParams.buildBvTree = true;
        navMeshCreateParams.tileX = 0;
        navMeshCreateParams.tileLayer = 0;
        
        // Build navmesh data
        var navMeshData = DtNavMeshBuilder.CreateNavMeshData(navMeshCreateParams);
        if (navMeshData == null)
        {
            throw new InvalidOperationException("Failed to create navigation mesh data.");
        }
        
        // Initialize DtNavMesh
        var navMesh = new DtNavMesh();
        navMesh.Init(navMeshData, mesh.nvp, 0);
        
        return navMesh;
    }
    
    /// <summary>
    /// Exports navmesh to OBJ file format.
    /// </summary>
    private static void ExportNavMeshToObj(DtNavMesh navMesh, string outputPath)
    {
        var vertices = new System.Collections.Generic.List<float[]>();
        var indices = new System.Collections.Generic.List<int>();
        
        // Get the first tile (tile 0)
        var tile = navMesh.GetTile(0);
        if (tile?.data != null)
        {
            var data = tile.data;
            
            // Extract vertices
            for (int i = 0; i < data.header.vertCount; i++)
            {
                int vertIndex = i * 3;
                vertices.Add(new[]
                {
                    data.verts[vertIndex],
                    data.verts[vertIndex + 1],
                    data.verts[vertIndex + 2]
                });
            }
            
            // Extract triangles from polygons
            for (int i = 0; i < data.header.polyCount; i++)
            {
                var poly = data.polys[i];
                
                // Simple fan triangulation from first vertex
                for (int j = 2; j < poly.vertCount; j++)
                {
                    indices.Add(poly.verts[0]);
                    indices.Add(poly.verts[j - 1]);
                    indices.Add(poly.verts[j]);
                }
            }
        }
        
        // Ensure directory exists
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Write to OBJ file
        using (var writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("# Direct NavMesh Export from DotRecast");
            writer.WriteLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"# Vertices: {vertices.Count}");
            writer.WriteLine($"# Triangles: {indices.Count / 3}");
            writer.WriteLine();
            
            // Write vertices
            foreach (var vertex in vertices)
            {
                writer.WriteLine($"v {vertex[0]:F6} {vertex[1]:F6} {vertex[2]:F6}");
            }
            
            writer.WriteLine();
            
            // Write faces (OBJ uses 1-based indexing)
            for (int i = 0; i < indices.Count; i += 3)
            {
                int v1 = indices[i] + 1;
                int v2 = indices[i + 1] + 1;
                int v3 = indices[i + 2] + 1;
                writer.WriteLine($"f {v1} {v2} {v3}");
            }
        }
        
        Console.WriteLine($"      Exported {vertices.Count} vertices, {indices.Count / 3} triangles");
    }
}
