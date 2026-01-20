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
    /// Generates a navigation mesh from triangle mesh geometry with area IDs.
    /// Uses DotRecast's area-based input for proper walkable/unwalkable classification.
    /// </summary>
    public NavMeshData GenerateNavMesh(float[] vertices, int[] indices, int[] areas, AgentConfig agentConfig)
    {
        // Step 1: Filter out walkable triangles that are below unwalkable geometry
        // This prevents navmesh generation in intersection volumes where thin horizontal
        // surfaces (grounds) overlap with vertical obstacles (walls)
        var (filteredVertices, filteredIndices, filteredAreas) = 
            FilterOccludedWalkableAreas(vertices, indices, areas);
        
        // Step 2: Create input geometry provider
        var geomProvider = new SimpleInputGeomProvider(filteredVertices, filteredIndices);
        
        // Step 3: Calculate bounding box
        var (bmin, bmax) = CalculateBounds(filteredVertices);
        
        // Add padding to bounds for proper voxelization
        // Recast needs space above and below geometry to work properly
        bmin.Y -= agentConfig.CellHeight; // Add one cell below
        bmax.Y += agentConfig.Height * 2; // Add clearance above for agent
        
        // Step 4: Create RcAreaModification (walkable area)
        // Recast treats area id 63 (0x3F) as the default walkable area
        var walkableAreaMod = new RcAreaModification(0x3f);
        
        // Step 5: Create configuration for Recast
        // RcConfig constructor signature (19 parameters)
        // Enable filters to exclude problematic areas based on agent clearance
        var config = new RcConfig(
            RcPartition.WATERSHED,                          // partitionType
            agentConfig.CellSize,                           // cellSize
            agentConfig.CellHeight,                         // cellHeight
            agentConfig.MaxSlope,                           // agentMaxSlope (degrees)
            agentConfig.Height,                             // agentHeight
            agentConfig.Radius,                             // agentRadius
            agentConfig.MaxClimb,                           // agentMaxClimb
            1,                                              // regionMinSize (reduced from 8 for small geometry)
            4,                                              // regionMergeSize (reduced from 20 for small geometry)
            agentConfig.EdgeMaxLength,                      // edgeMaxLen
            agentConfig.EdgeMaxError,                       // edgeMaxError
            6,                                              // vertsPerPoly
            agentConfig.DetailSampleDistance,               // detailSampleDist
            agentConfig.DetailSampleMaxError,               // detailSampleMaxError
            true,                                           // filterLowHangingObstacles - ENABLED to remove obstacles below walkable surfaces
            true,                                           // filterLedgeSpans - ENABLED to remove dangerous ledges
            true,                                           // filterWalkableLowHeightSpans - ENABLED to remove areas too low for agent (based on walkableHeight, not absolute Y)
            walkableAreaMod,                                // walkableAreaMod
            true                                            // buildMeshDetail
        );
        
        // Step 6: Create builder config with bounding box
        var builderConfig = new RcBuilderConfig(config, bmin, bmax);
        
        // Step 7: Build the navigation mesh using RcBuilder
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
    /// Generates a navigation mesh directly from triangle mesh geometry using DotRecast's recommended approach.
    /// This method bypasses area-based filtering and lets DotRecast handle walkable surface detection.
    /// 
    /// RECOMMENDED for:
    /// - Static world geometry loaded from files (.obj, .fbx, etc.)
    /// - Artist-authored levels
    /// - Maximum navmesh quality
    /// 
    /// Use the regular GenerateNavMesh() method for:
    /// - Dynamic procedural world generation  
    /// - Runtime obstacle modification
    /// - Geometry extracted from physics systems
    /// </summary>
    public NavMeshData GenerateNavMeshDirect(float[] vertices, int[] indices, AgentConfig agentConfig)
    {
        // Step 1: Create input geometry provider (no area filtering)
        var geomProvider = new SimpleInputGeomProvider(vertices, indices);
        
        // Step 2: Calculate bounding box
        var (bmin, bmax) = CalculateBounds(vertices);
        
        // Add padding to bounds (DotRecast recommended)
        bmin.Y -= agentConfig.CellHeight;
        bmax.Y += agentConfig.Height * 2;
        
        // Step 3: Create configuration with DotRecast recommended settings
        // Cell size: agent radius / 2 for outdoor, radius / 3 for indoor
        var cellSize = agentConfig.Radius / 2.0f;
        var cellHeight = cellSize / 2.0f;
        
        var walkableAreaMod = new RcAreaModification(0x3f);
        
        var config = new RcConfig(
            RcPartition.WATERSHED,                          // partitionType (recommended for most cases)
            cellSize,                                        // cellSize (radius / 2)
            cellHeight,                                      // cellHeight (half of cell size)
            agentConfig.MaxSlope,                           // agentMaxSlope
            agentConfig.Height,                             // agentHeight
            agentConfig.Radius,                             // agentRadius
            agentConfig.MaxClimb,                           // agentMaxClimb
            1,                                              // regionMinSize
            4,                                              // regionMergeSize
            agentConfig.Radius * 8.0f,                      // edgeMaxLen (recommended: radius * 8)
            1.3f,                                           // edgeMaxError (recommended: 1.1-1.5)
            6,                                              // vertsPerPoly
            cellSize * 6.0f,                                // detailSampleDist (recommended: cellSize * 6)
            cellHeight,                                     // detailSampleMaxError
            true,                                           // filterLowHangingObstacles
            true,                                           // filterLedgeSpans
            true,                                           // filterWalkableLowHeightSpans
            walkableAreaMod,                                // walkableAreaMod
            true                                            // buildMeshDetail
        );
        
        // Step 4: Create builder config with bounding box
        var builderConfig = new RcBuilderConfig(config, bmin, bmax);
        
        // Step 5: Build the navigation mesh using RcBuilder (DotRecast handles all filtering)
        var builder = new RcBuilder();
        
        Console.WriteLine($"[Direct] Building navmesh with DotRecast recommended settings:");
        Console.WriteLine($"[Direct]   Bounds: min=({bmin.X:F2}, {bmin.Y:F2}, {bmin.Z:F2}), max=({bmax.X:F2}, {bmax.Y:F2}, {bmax.Z:F2})");
        Console.WriteLine($"[Direct]   Agent: height={agentConfig.Height}, radius={agentConfig.Radius}, maxClimb={agentConfig.MaxClimb}");
        Console.WriteLine($"[Direct]   Voxel: cellSize={cellSize:F2}, cellHeight={cellHeight:F2}");
        
        var buildResult = builder.Build(geomProvider, builderConfig, keepInterResults: true);
        
        if (buildResult == null)
        {
            throw new InvalidOperationException("Failed to build navigation mesh from geometry.");
        }
        
        Console.WriteLine($"[Direct] Build result: {buildResult.Mesh?.npolys ?? 0} polys, {buildResult.ContourSet?.conts?.Count ?? 0} contours");
        
        // Step 6: Create Detour navigation mesh
        var navMesh = CreateDetourNavMesh(buildResult, config, agentConfig);
        
        // Step 7: Create query object for pathfinding
        var query = new DtNavMeshQuery(navMesh);
        
        return new NavMeshData(navMesh, query);
    }
    
    /// <summary>
    /// Generates a navigation mesh directly from a list of triangles using DotRecast's recommended approach.
    /// </summary>
    public NavMeshData GenerateNavMeshDirect(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, AgentConfig agentConfig)
    {
        // Convert Vector3 list to float array
        var verticesArray = new float[vertices.Count * 3];
        for (int i = 0; i < vertices.Count; i++)
        {
            verticesArray[i * 3] = vertices[i].X;
            verticesArray[i * 3 + 1] = vertices[i].Y;
            verticesArray[i * 3 + 2] = vertices[i].Z;
        }
        
        var indicesArray = indices.ToArray();
        
        return GenerateNavMeshDirect(verticesArray, indicesArray, agentConfig);
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
    /// Filters out walkable triangles that are occluded by unwalkable geometry above them.
    /// This prevents navmesh generation in areas where thin horizontal surfaces intersect
    /// with vertical obstacles (e.g., underground areas where a ground plane passes through a wall).
    /// 
    /// Algorithm:
    /// 1. Separate walkable (area 63) and unwalkable (area 0) triangles
    /// 2. For each walkable triangle, check if there's an unwalkable triangle above it
    /// 3. If occluded, mark the walkable triangle as unwalkable (area 0)
    /// </summary>
    private (float[] vertices, int[] indices, int[] areas) FilterOccludedWalkableAreas(
        float[] vertices, int[] indices, int[] areas)
    {
        int triangleCount = indices.Length / 3;
        var filteredAreas = new int[triangleCount];
        Array.Copy(areas, filteredAreas, triangleCount);
        
        int occludedCount = 0;
        
        // Process each walkable triangle
        for (int i = 0; i < triangleCount; i++)
        {
            // Skip if not walkable
            if (areas[i] != 63)
            {
                continue;
            }
            
            // Get triangle vertices
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];
            
            var v0 = new System.Numerics.Vector3(vertices[idx0 * 3], vertices[idx0 * 3 + 1], vertices[idx0 * 3 + 2]);
            var v1 = new System.Numerics.Vector3(vertices[idx1 * 3], vertices[idx1 * 3 + 1], vertices[idx1 * 3 + 2]);
            var v2 = new System.Numerics.Vector3(vertices[idx2 * 3], vertices[idx2 * 3 + 1], vertices[idx2 * 3 + 2]);
            
            // Calculate triangle center and average height
            var center = (v0 + v1 + v2) / 3.0f;
            float walkableHeight = center.Y;
            
            // Check if this walkable triangle is occluded by unwalkable geometry above it
            bool isOccluded = false;
            
            for (int j = 0; j < triangleCount; j++)
            {
                // Skip if not unwalkable
                if (areas[j] != 0)
                {
                    continue;
                }
                
                // Get unwalkable triangle vertices
                int uidx0 = indices[j * 3];
                int uidx1 = indices[j * 3 + 1];
                int uidx2 = indices[j * 3 + 2];
                
                var uv0 = new System.Numerics.Vector3(vertices[uidx0 * 3], vertices[uidx0 * 3 + 1], vertices[uidx0 * 3 + 2]);
                var uv1 = new System.Numerics.Vector3(vertices[uidx1 * 3], vertices[uidx1 * 3 + 1], vertices[uidx1 * 3 + 2]);
                var uv2 = new System.Numerics.Vector3(vertices[uidx2 * 3], vertices[uidx2 * 3 + 1], vertices[uidx2 * 3 + 2]);
                
                // Check if unwalkable triangle is below the walkable one
                // (indicating the walkable surface is inside or intersecting the unwalkable volume)
                float unwalkableMinY = Math.Min(Math.Min(uv0.Y, uv1.Y), uv2.Y);
                float unwalkableMaxY = Math.Max(Math.Max(uv0.Y, uv1.Y), uv2.Y);
                
                // If the walkable surface is within the vertical range of the unwalkable geometry
                if (walkableHeight >= unwalkableMinY && walkableHeight <= unwalkableMaxY)
                {
                    // Check horizontal overlap (simple 2D AABB test in XZ plane)
                    float walkableMinX = Math.Min(Math.Min(v0.X, v1.X), v2.X);
                    float walkableMaxX = Math.Max(Math.Max(v0.X, v1.X), v2.X);
                    float walkableMinZ = Math.Min(Math.Min(v0.Z, v1.Z), v2.Z);
                    float walkableMaxZ = Math.Max(Math.Max(v0.Z, v1.Z), v2.Z);
                    
                    float unwalkableMinX = Math.Min(Math.Min(uv0.X, uv1.X), uv2.X);
                    float unwalkableMaxX = Math.Max(Math.Max(uv0.X, uv1.X), uv2.X);
                    float unwalkableMinZ = Math.Min(Math.Min(uv0.Z, uv1.Z), uv2.Z);
                    float unwalkableMaxZ = Math.Max(Math.Max(uv0.Z, uv1.Z), uv2.Z);
                    
                    // Check if AABBs overlap in XZ plane
                    bool overlapX = walkableMinX <= unwalkableMaxX && walkableMaxX >= unwalkableMinX;
                    bool overlapZ = walkableMinZ <= unwalkableMaxZ && walkableMaxZ >= unwalkableMinZ;
                    
                    if (overlapX && overlapZ)
                    {
                        // This walkable triangle is inside or intersecting with unwalkable geometry
                        isOccluded = true;
                        break;
                    }
                }
            }
            
            if (isOccluded)
            {
                filteredAreas[i] = 0; // Mark as unwalkable
                occludedCount++;
            }
        }
        
        if (occludedCount > 0)
        {
            Console.WriteLine($"Filtered {occludedCount} occluded walkable triangles (marked as unwalkable)");
        }
        
        return (vertices, indices, filteredAreas);
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
        
        // IMPORTANT: Set walkable flags on polygons based on area IDs
        // Recast's area-based input ensures only intended areas become walkable
        // No height-based filtering needed - area IDs handle underground/building interiors
        const int WALKABLE_FLAG = 0x01; // Standard walkable flag
        
        if (mesh.flags != null && mesh.areas != null)
        {
            for (int i = 0; i < mesh.npolys; i++)
            {
                // Set walkable flag for all polygons with area 63 (walkable area)
                // Unwalkable polygons (area 0) remain without flags
                if (mesh.areas[i] == 63)
                {
                    mesh.flags[i] = WALKABLE_FLAG;
                }
            }
            
            int walkableCount = mesh.areas.Count(a => a == 63);
            Console.WriteLine($"Set walkable flags on {walkableCount}/{mesh.npolys} polygons (area 63)");
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
    /// Generates a navigation mesh from a list of triangles with area IDs.
    /// </summary>
    public NavMeshData GenerateNavMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, IReadOnlyList<int> areas, AgentConfig agentConfig)
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
        var areasArray = areas.ToArray();
        
        return GenerateNavMesh(verticesArray, indicesArray, areasArray, agentConfig);
    }
    
    /// <summary>
    /// Generates a navigation mesh from triangle mesh geometry (backward compatibility).
    /// All triangles default to walkable (area 63).
    /// </summary>
    public NavMeshData GenerateNavMesh(float[] vertices, int[] indices, AgentConfig agentConfig)
    {
        // Default all triangles to walkable (area 63)
        int triangleCount = indices.Length / 3;
        var areas = new int[triangleCount];
        Array.Fill(areas, 63); // RC_WALKABLE_AREA
        
        return GenerateNavMesh(vertices, indices, areas, agentConfig);
    }
    
    /// <summary>
    /// Generates a navigation mesh from a list of triangles (backward compatibility).
    /// All triangles default to walkable (area 63).
    /// </summary>
    public NavMeshData GenerateNavMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, AgentConfig agentConfig)
    {
        // Default all triangles to walkable (area 63)
        int triangleCount = indices.Count / 3;
        var areas = new int[triangleCount];
        Array.Fill(areas, 63); // RC_WALKABLE_AREA
        
        return GenerateNavMesh(vertices, indices, areas, agentConfig);
    }
}

/// <summary>
/// Simple implementation of IRcInputGeomProvider for triangle mesh input.
/// </summary>
public class SimpleInputGeomProvider : IRcInputGeomProvider
{
    private readonly float[] _vertices;
    private readonly int[] _faces;
    private readonly RcVec3f _bmin;
    private readonly RcVec3f _bmax;
    private readonly List<RcTriMesh> _meshes;
    private readonly List<RcConvexVolume> _convexVolumes;
    private readonly List<RcOffMeshConnection> _offMeshConnections;

    public SimpleInputGeomProvider(float[] vertices, int[] faces)
    {
        _vertices = vertices;
        _faces = faces;
        _convexVolumes = new List<RcConvexVolume>();
        _offMeshConnections = new List<RcOffMeshConnection>();
        
        // Calculate bounds
        _bmin = new RcVec3f(float.MaxValue, float.MaxValue, float.MaxValue);
        _bmax = new RcVec3f(float.MinValue, float.MinValue, float.MinValue);
        
        for (int i = 0; i < vertices.Length; i += 3)
        {
            _bmin.X = Math.Min(_bmin.X, vertices[i]);
            _bmin.Y = Math.Min(_bmin.Y, vertices[i + 1]);
            _bmin.Z = Math.Min(_bmin.Z, vertices[i + 2]);
            
            _bmax.X = Math.Max(_bmax.X, vertices[i]);
            _bmax.Y = Math.Max(_bmax.Y, vertices[i + 1]);
            _bmax.Z = Math.Max(_bmax.Z, vertices[i + 2]);
        }
        
        // Create mesh
        _meshes = new List<RcTriMesh> { new RcTriMesh(vertices, faces) };
    }

    public RcVec3f GetMeshBoundsMin() => _bmin;
    public RcVec3f GetMeshBoundsMax() => _bmax;
    public RcTriMesh GetMesh() => _meshes[0];
    public IEnumerable<RcTriMesh> Meshes() => _meshes;
    public IList<RcConvexVolume> ConvexVolumes() => _convexVolumes;
    public List<RcOffMeshConnection> GetOffMeshConnections() => _offMeshConnections;
    
    public void AddConvexVolume(RcConvexVolume volume)
    {
        _convexVolumes.Add(volume);
    }
    
    public void AddOffMeshConnection(RcVec3f start, RcVec3f end, float radius, bool bidir, int area, int flags)
    {
        _offMeshConnections.Add(new RcOffMeshConnection(start, end, radius, bidir, area, flags));
    }
    
    public void RemoveOffMeshConnections(Predicate<RcOffMeshConnection> predicate)
    {
        _offMeshConnections.RemoveAll(predicate);
    }
}

