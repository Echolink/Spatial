using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using DotRecast.Detour;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace Spatial.Pathfinding;

/// <summary>
/// Generates navigation meshes from geometry using DotRecast.
/// </summary>
public class NavMeshGenerator
{
    /// <summary>
    /// Generates a navigation mesh from triangle mesh geometry.
    /// </summary>
    public NavMeshData GenerateNavMesh(float[] vertices, int[] indices, AgentConfig agentConfig)
    {
        // Step 1: Create input geometry provider
        var geomProvider = new RcSimpleInputGeomProvider(vertices, indices);
        
        // Step 2: Calculate bounding box
        var (bmin, bmax) = CalculateBounds(vertices);
        
        // Step 3: Create RcAreaModification (walkable area)
        // Recast treats area id 63 (0x3F) as the default walkable area
        var walkableAreaMod = new RcAreaModification(0x3f);
        
        // Step 4: Create configuration for Recast
        // RcConfig constructor signature (19 parameters)
        // Try with filters DISABLED to see if they're removing all geometry
        var config = new RcConfig(
            RcPartition.WATERSHED,                          // partitionType
            agentConfig.CellSize,                           // cellSize
            agentConfig.CellHeight,                         // cellHeight
            agentConfig.MaxSlope,                           // agentMaxSlope (degrees)
            agentConfig.Height,                             // agentHeight
            agentConfig.Radius,                             // agentRadius
            agentConfig.MaxClimb,                           // agentMaxClimb
            8,                                              // regionMinSize
            20,                                             // regionMergeSize
            agentConfig.EdgeMaxLength,                      // edgeMaxLen
            agentConfig.EdgeMaxError,                       // edgeMaxError
            6,                                              // vertsPerPoly
            agentConfig.DetailSampleDistance,               // detailSampleDist
            agentConfig.DetailSampleMaxError,               // detailSampleMaxError
            false,                                          // filterLowHangingObstacles - DISABLED
            false,                                          // filterLedgeSpans - DISABLED
            false,                                          // filterWalkableLowHeightSpans - DISABLED
            walkableAreaMod,                                // walkableAreaMod
            true                                            // buildMeshDetail
        );
        
        // Step 5: Create builder config with bounding box
        var builderConfig = new RcBuilderConfig(config, bmin, bmax);
        
        // Step 6: Build the navigation mesh using RcBuilder
        var builder = new RcBuilder();
        
        Console.WriteLine($"Building navmesh with bounds: min=({bmin.X}, {bmin.Y}, {bmin.Z}), max=({bmax.X}, {bmax.Y}, {bmax.Z})");
        Console.WriteLine($"Config: cellSize={config.Cs}, cellHeight={config.Ch}, agentHeight={config.WalkableHeight}, agentRadius={config.WalkableRadius}");
        
        var buildResult = builder.Build(geomProvider, builderConfig, keepInterResults: true);
        
        if (buildResult == null)
        {
            throw new InvalidOperationException("Failed to build navigation mesh from geometry.");
        }
        
        Console.WriteLine($"Build result:");
        Console.WriteLine($"  SolidHeightfield: {(buildResult.SolidHeightfiled != null ? "present" : "null")}");
        if (buildResult.SolidHeightfiled != null)
        {
            var hf = buildResult.SolidHeightfiled;
            Console.WriteLine($"    Size: {hf.width} x {hf.height}, Spans: {hf.spans?.Length ?? 0}");
        }
        Console.WriteLine($"  CompactHeightfield: {(buildResult.CompactHeightfield != null ? "present" : "null")}");
        if (buildResult.CompactHeightfield != null)
        {
            var chf = buildResult.CompactHeightfield;
            Console.WriteLine($"    Cells: {chf.spanCount}, Walkable: checking...");
        }
        Console.WriteLine($"  ContourSet: {(buildResult.ContourSet != null ? "present" : "null")}");
        if (buildResult.ContourSet != null)
        {
            Console.WriteLine($"    Contours: {buildResult.ContourSet.conts?.Count ?? 0}");
        }
        Console.WriteLine($"  Mesh: {(buildResult.Mesh != null ? "present" : "null")}");
        Console.WriteLine($"  MeshDetail: {(buildResult.MeshDetail != null ? "present" : "null")}");
        
        // Step 7: Create Detour navigation mesh from Recast build result
        var navMesh = CreateDetourNavMesh(buildResult, config, agentConfig);
        
        // Step 8: Create query object for pathfinding
        var query = new DtNavMeshQuery(navMesh);
        
        return new NavMeshData(navMesh, query);
    }
    
    /// <summary>
    /// Calculates the bounding box of the geometry.
    /// </summary>
    private (RcVec3f min, RcVec3f max) CalculateBounds(float[] vertices)
    {
        if (vertices.Length < 3)
        {
            throw new ArgumentException("Need at least one vertex (3 floats)");
        }
        
        var min = new RcVec3f(vertices[0], vertices[1], vertices[2]);
        var max = new RcVec3f(vertices[0], vertices[1], vertices[2]);
        
        for (int i = 0; i < vertices.Length; i += 3)
        {
            var x = vertices[i];
            var y = vertices[i + 1];
            var z = vertices[i + 2];
            
            min.X = Math.Min(min.X, x);
            min.Y = Math.Min(min.Y, y);
            min.Z = Math.Min(min.Z, z);
            
            max.X = Math.Max(max.X, x);
            max.Y = Math.Max(max.Y, y);
            max.Z = Math.Max(max.Z, z);
        }
        
        return (min, max);
    }
    
    /// <summary>
    /// Creates DtNavMesh from RcBuilder result.
    /// </summary>
    private DtNavMesh CreateDetourNavMesh(RcBuilderResult buildResult, RcConfig config, AgentConfig agentConfig)
    {
        var mesh = buildResult.Mesh;
        var meshDetail = buildResult.MeshDetail;
        
        if (mesh == null)
        {
            throw new InvalidOperationException("Build result does not contain polygon mesh.");
        }
        
        // IMPORTANT: Set walkable flags on all polygons
        // Without flags, polygons won't be used in pathfinding queries
        const int WALKABLE_FLAG = 0x01; // Standard walkable flag
        if (mesh.flags != null && mesh.areas != null)
        {
            for (int i = 0; i < mesh.npolys; i++)
            {
                // Set walkable flag for all polygons with area 63 (walkable area)
                if (mesh.areas[i] == 63)
                {
                    mesh.flags[i] = WALKABLE_FLAG;
                }
            }
        }
        
        Console.WriteLine($"Mesh info: verts={mesh.nverts}, polys={mesh.npolys}, nvp={mesh.nvp}");
        
        // Print polygon areas and flags
        if (mesh.npolys > 0 && mesh.areas != null && mesh.flags != null)
        {
            Console.WriteLine($"Polygon info:");
            for (int i = 0; i < mesh.npolys; i++)
            {
                Console.WriteLine($"  Poly {i}: area={mesh.areas[i]}, flags={mesh.flags[i]}");
            }
        }
        
        // Print first few vertices to understand the geometry
        if (mesh.nverts > 0 && mesh.verts != null)
        {
            Console.WriteLine($"Sample mesh vertices:");
            int samplesToShow = Math.Min(8, mesh.nverts);
            for (int i = 0; i < samplesToShow; i++)
            {
                var x = mesh.verts[i * 3];
                var y = mesh.verts[i * 3 + 1];
                var z = mesh.verts[i * 3 + 2];
                Console.WriteLine($"  Vertex {i}: ({x:F2}, {y:F2}, {z:F2})");
            }
        }
        
        if (meshDetail != null)
        {
            Console.WriteLine($"MeshDetail info: verts={meshDetail.nverts}, tris={meshDetail.ntris}");
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
        
        navMeshCreateParams.walkableHeight = agentConfig.Height;
        navMeshCreateParams.walkableRadius = agentConfig.Radius;
        navMeshCreateParams.walkableClimb = agentConfig.MaxClimb;
        navMeshCreateParams.bmin = mesh.bmin;
        navMeshCreateParams.bmax = mesh.bmax;
        navMeshCreateParams.cs = config.Cs;
        navMeshCreateParams.ch = config.Ch;
        navMeshCreateParams.buildBvTree = true;
        navMeshCreateParams.tileX = 0;
        navMeshCreateParams.tileLayer = 0;
        
        Console.WriteLine($"Creating navmesh data with {navMeshCreateParams.polyCount} polygons...");
        
        // Build navmesh data
        var navMeshData = DtNavMeshBuilder.CreateNavMeshData(navMeshCreateParams);
        if (navMeshData == null)
        {
            Console.WriteLine("DtNavMeshBuilder.CreateNavMeshData returned null!");
            Console.WriteLine($"Parameters: verts={navMeshCreateParams.vertCount}, polys={navMeshCreateParams.polyCount}");
            throw new InvalidOperationException("Failed to create navigation mesh data.");
        }
        
        Console.WriteLine($"Navmesh data created successfully");
        
        // Initialize DtNavMesh
        var navMesh = new DtNavMesh();
        navMesh.Init(navMeshData, mesh.nvp, 0);
        
        Console.WriteLine($"DtNavMesh initialized:");
        Console.WriteLine($"  Max tiles: {navMesh.GetMaxTiles()}");
        
        // Print navmesh bounds from the parameters
        Console.WriteLine($"  NavMesh bounds: min=({navMeshCreateParams.bmin.X:F2}, {navMeshCreateParams.bmin.Y:F2}, {navMeshCreateParams.bmin.Z:F2}), max=({navMeshCreateParams.bmax.X:F2}, {navMeshCreateParams.bmax.Y:F2}, {navMeshCreateParams.bmax.Z:F2})");
        
        // Test: Try to query a known position
        var testQuery = new DtNavMeshQuery(navMesh);
        var testPos = new RcVec3f(0, 0.5f, 0); // Middle of ground area
        var testExtents = new RcVec3f(10, 10, 10);
        var testFilter = new DtQueryDefaultFilter();
        testFilter.SetIncludeFlags(0x01);
        testQuery.FindNearestPoly(testPos, testExtents, testFilter, out var testRef, out var testNearest, out var testFound);
        Console.WriteLine($"  Test query at (0, 0.5, 0): found={testFound}, ref={testRef}");
        if (testFound)
        {
            Console.WriteLine($"    Nearest point: ({testNearest.X:F2}, {testNearest.Y:F2}, {testNearest.Z:F2})");
        }
        
        return navMesh;
    }
    
    /// <summary>
    /// Generates a navigation mesh from a list of triangles.
    /// </summary>
    public NavMeshData GenerateNavMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, AgentConfig agentConfig)
    {
        // Convert Vector3 list to float array
        var verticesArray = new float[vertices.Count * 3];
        for (int i = 0; i < vertices.Count; i++)
        {
            verticesArray[i * 3] = vertices[i].X;
            verticesArray[i * 3 + 1] = vertices[i].Y;
            verticesArray[i * 3 + 2] = vertices[i].Z;
        }
        
        // Convert indices list to array
        var indicesArray = indices.ToArray();
        
        return GenerateNavMesh(verticesArray, indicesArray, agentConfig);
    }
}
