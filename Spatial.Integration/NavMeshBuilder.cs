using Spatial.Physics;
using Spatial.Pathfinding;
using System;
using System.Numerics;
using System.Collections.Generic;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Spatial.Integration;

/// <summary>
/// Bridges Spatial.Physics and Spatial.Pathfinding.
/// Extracts static collider geometry from the physics world and generates a navigation mesh.
/// 
/// This is the integration point between the two systems:
/// - Reads geometry from Spatial.Physics (static colliders)
/// - Generates NavMesh using Spatial.Pathfinding
/// </summary>
public class NavMeshBuilder
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly NavMeshGenerator _navMeshGenerator;
    
    /// <summary>
    /// Creates a new NavMesh builder.
    /// </summary>
    public NavMeshBuilder(PhysicsWorld physicsWorld, NavMeshGenerator navMeshGenerator)
    {
        _physicsWorld = physicsWorld;
        _navMeshGenerator = navMeshGenerator;
    }
    
    /// <summary>
    /// Builds a navigation mesh from all static colliders in the physics world.
    /// Uses area-based filtering for proper walkable/unwalkable classification.
    /// </summary>
    /// <param name="agentConfig">Agent configuration for NavMesh generation</param>
    /// <returns>Generated navigation mesh data</returns>
    public NavMeshData BuildNavMeshFromPhysicsWorld(AgentConfig agentConfig)
    {
        // Extract geometry with area information from static colliders
        var (vertices, indices, areas) = ExtractStaticColliderGeometry();
        
        // Generate NavMesh from geometry with area data
        return _navMeshGenerator.GenerateNavMesh(vertices, indices, areas, agentConfig);
    }
    
    /// <summary>
    /// Builds a navigation mesh directly from raw mesh geometry using DotRecast's recommended approach.
    /// This bypasses physics system processing and area filtering for maximum quality.
    /// 
    /// RECOMMENDED for:
    /// - Static world geometry loaded from files
    /// - Artist-authored levels
    /// - Maximum navmesh quality
    /// 
    /// Use BuildNavMeshFromPhysicsWorld() for:
    /// - Dynamic procedural worlds
    /// - Runtime obstacle modification
    /// - Integration with physics simulation
    /// </summary>
    public NavMeshData BuildNavMeshDirect(AgentConfig agentConfig)
    {
        // Extract raw mesh geometry without physics processing
        var (vertices, indices) = ExtractRawMeshGeometry();
        
        // Calculate source mesh Y-range for validation
        float sourceMinY = float.MaxValue;
        float sourceMaxY = float.MinValue;
        
        foreach (var v in vertices)
        {
            sourceMinY = Math.Min(sourceMinY, v.Y);
            sourceMaxY = Math.Max(sourceMaxY, v.Y);
        }
        
        // Generate NavMesh using direct approach
        var navMeshData = _navMeshGenerator.GenerateNavMeshDirect(vertices, indices, agentConfig);
        
        // Validate navmesh height range against source mesh
        if (vertices.Count > 0)
        {
            // Get navmesh bounds from the generated mesh
            var navMesh = navMeshData.NavMesh;
            
            // Get first tile using GetTile by index
            int maxTiles = navMesh.GetMaxTiles();
            if (maxTiles > 0)
            {
                var tile = navMesh.GetTile(0); // Get first tile
                
                if (tile != null && tile.data != null)
                {
                    var header = tile.data.header;
                    float navMeshMinY = header.bmin.Y;
                    float navMeshMaxY = header.bmax.Y;
                    
                    Console.WriteLine($"[Direct] NavMesh height validation:");
                    Console.WriteLine($"[Direct]   Source mesh Y-range: [{sourceMinY:F2}, {sourceMaxY:F2}]");
                    Console.WriteLine($"[Direct]   NavMesh Y-range: [{navMeshMinY:F2}, {navMeshMaxY:F2}]");
                    
                    // Check if navmesh Y-range is reasonable compared to source
                    float yRangeDifference = Math.Abs((navMeshMaxY - navMeshMinY) - (sourceMaxY - sourceMinY));
                    if (yRangeDifference > 1.0f)
                    {
                        Console.WriteLine($"[Direct] WARNING: NavMesh Y-range differs significantly from source mesh (difference: {yRangeDifference:F2})");
                    }
                    
                    // Check if navmesh is at correct height relative to source
                    float centerDifference = Math.Abs(((navMeshMaxY + navMeshMinY) / 2) - ((sourceMaxY + sourceMinY) / 2));
                    if (centerDifference > 1.0f)
                    {
                        Console.WriteLine($"[Direct] WARNING: NavMesh center height differs from source mesh center (difference: {centerDifference:F2})");
                    }
                }
                else
                {
                    Console.WriteLine($"[Direct] WARNING: Could not retrieve navmesh tile data for validation");
                }
            }
            else
            {
                Console.WriteLine($"[Direct] WARNING: NavMesh has no tiles");
            }
        }
        
        return navMeshData;
    }
    
    /// <summary>
    /// Extracts raw triangle mesh geometry from all mesh entities without physics processing.
    /// This preserves original mesh data for direct navmesh generation.
    /// </summary>
    private (List<Vector3> vertices, List<int> indices) ExtractRawMeshGeometry()
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        
        foreach (var entity in _physicsWorld.EntityRegistry.GetAllEntities())
        {
            if (!entity.IsStatic)
                continue;
            
            // Check if this entity has raw mesh data (loaded from file)
            var meshData = _physicsWorld.GetMeshData(entity.EntityId);
            if (meshData.HasValue)
            {
                var (meshVertices, meshIndices, navMeshArea) = meshData.Value;
                
                // Skip if marked as "Ignore"
                if (navMeshArea == PhysicsWorld.NavMeshAreaType.Ignore)
                {
                    continue;
                }
                
                Console.WriteLine($"  Using raw mesh data for entity {entity.EntityId}: {meshVertices.Length} vertices");
                
                // Add vertices and indices directly
                int startIndex = vertices.Count;
                vertices.AddRange(meshVertices);
                
                foreach (var index in meshIndices)
                {
                    indices.Add(startIndex + index);
                }
            }
        }
        
        // Validate and log mesh height range
        if (vertices.Count > 0)
        {
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            
            foreach (var v in vertices)
            {
                minY = Math.Min(minY, v.Y);
                maxY = Math.Max(maxY, v.Y);
            }
            
            Console.WriteLine($"[Direct] Extracted raw geometry:");
            Console.WriteLine($"[Direct]   Vertices: {vertices.Count}");
            Console.WriteLine($"[Direct]   Indices: {indices.Count}");
            Console.WriteLine($"[Direct]   Triangles: {indices.Count / 3}");
            Console.WriteLine($"[Direct]   Mesh Y-Range: [{minY:F2}, {maxY:F2}] (height: {maxY - minY:F2})");
            
            // Validate that Y-range makes sense
            if (maxY - minY < 0.1f)
            {
                Console.WriteLine($"[Direct] WARNING: Mesh has very small Y-range ({maxY - minY:F2}). This may indicate a flat or degenerate mesh.");
            }
        }
        else
        {
            Console.WriteLine($"[Direct] Extracted raw geometry:");
            Console.WriteLine($"[Direct]   Vertices: {vertices.Count}");
            Console.WriteLine($"[Direct]   Indices: {indices.Count}");
            Console.WriteLine($"[Direct]   Triangles: {indices.Count / 3}");
        }
        
        return (vertices, indices);
    }
    
    /// <summary>
    /// Extracts triangle mesh geometry from all static colliders with area information.
    /// This converts BepuPhysics colliders to a format DotRecast can use, along with
    /// Recast area IDs for proper navmesh generation.
    /// </summary>
    private (List<Vector3> vertices, List<int> indices, List<int> areas) ExtractStaticColliderGeometry()
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var areas = new List<int>(); // Recast area ID per triangle
        int vertexOffset = 0;
        
        // First pass: collect all obstacle volumes for intersection testing
        var obstacleVolumes = new List<ObstacleVolume>();
        
        // Iterate through all entities and extract static collider geometry
        int staticEntityCount = 0;
        int ignoredCount = 0;
        
        foreach (var entity in _physicsWorld.EntityRegistry.GetAllEntities())
        {
            if (!entity.IsStatic)
                continue;
            
            staticEntityCount++;
            
            // Check if this entity has raw mesh data (loaded from file)
            var meshData = _physicsWorld.GetMeshData(entity.EntityId);
            if (meshData.HasValue)
            {
                var (meshVertices, meshIndices, navMeshArea) = meshData.Value;
                
                // Skip if marked as "Ignore"
                if (navMeshArea == PhysicsWorld.NavMeshAreaType.Ignore)
                {
                    Console.WriteLine($"  Ignoring mesh entity {entity.EntityId} (marked as Ignore)");
                    ignoredCount++;
                    continue;
                }
                
                // If this is unwalkable geometry, collect it as an obstacle volume
                if (navMeshArea == PhysicsWorld.NavMeshAreaType.Unwalkable)
                {
                    // Calculate AABB for this mesh
                    var bounds = CalculateMeshBounds(meshVertices);
                    obstacleVolumes.Add(new ObstacleVolume(bounds.Min, bounds.Max));
                }
                
                // Map to Recast area IDs
                // Recast convention: Area 63 (0x3F) = walkable, Area 0 = unwalkable
                int recastAreaId = navMeshArea switch
                {
                    PhysicsWorld.NavMeshAreaType.Walkable => 63,   // RC_WALKABLE_AREA
                    PhysicsWorld.NavMeshAreaType.Unwalkable => 0,  // RC_NULL_AREA
                    _ => 63
                };
                
                Console.WriteLine($"  Using mesh data for entity {entity.EntityId}: {meshVertices.Length} vertices, area={navMeshArea} (Recast ID: {recastAreaId})");
                
                // Add vertices and indices directly
                int startIndex = vertices.Count;
                vertices.AddRange(meshVertices);
                
                // Calculate number of triangles in this mesh
                int triangleCount = meshIndices.Length / 3;
                
                foreach (var index in meshIndices)
                {
                    indices.Add(startIndex + index);
                }
                
                // Assign area ID to each triangle
                for (int i = 0; i < triangleCount; i++)
                {
                    areas.Add(recastAreaId);
                }
            }
            else
            {
                // Fall back to extracting from box/shape collider
                // Get static reference (static entities use Statics collection, not Bodies)
                var staticReference = _physicsWorld.Simulation.Statics[entity.StaticHandle];
                
                // Check shape type to determine if it's an obstacle volume
                var shapeIndex = entity.ShapeIndex;
                if (shapeIndex.Type == Box.Id)
                {
                    ref var box = ref _physicsWorld.Simulation.Shapes.GetShape<Box>(shapeIndex.Index);
                    var halfSize = new Vector3(box.Width * 0.5f, box.Height * 0.5f, box.Length * 0.5f);
                    
                    // Check if this is a vertical obstacle (not a flat ground)
                    bool isVerticalObstacle = !(box.Height < box.Width && box.Height < box.Length);
                    
                    if (isVerticalObstacle)
                    {
                        // Calculate AABB for this box in world space
                        var pose = staticReference.Pose;
                        var bounds = CalculateBoxBounds(box, pose);
                        obstacleVolumes.Add(new ObstacleVolume(bounds.Min, bounds.Max));
                    }
                }
                
                // Extract geometry from collider using shape index stored in entity
                // Box colliders default to "Unwalkable" (blocking volumes)
                ExtractColliderGeometryFromStatic(staticReference, entity.ShapeIndex, vertices, indices, areas, ref vertexOffset);
            }
        }
        
        // Second pass: Filter walkable triangles that intersect with obstacle volumes
        int filteredCount = FilterWalkableTrianglesInObstacles(vertices, indices, areas, obstacleVolumes);
        
        Console.WriteLine($"Extracted geometry from {staticEntityCount} static entities:");
        Console.WriteLine($"  Vertices: {vertices.Count}");
        Console.WriteLine($"  Indices: {indices.Count}");
        Console.WriteLine($"  Triangles: {indices.Count / 3}");
        Console.WriteLine($"  Area assignments: {areas.Count}");
        Console.WriteLine($"  Obstacle volumes: {obstacleVolumes.Count}");
        if (filteredCount > 0)
        {
            Console.WriteLine($"  Filtered walkable triangles inside obstacles: {filteredCount}");
        }
        if (ignoredCount > 0)
        {
            Console.WriteLine($"  Ignored: {ignoredCount} meshes");
        }
        
        // Print first few vertices to understand the geometry
        if (vertices.Count > 0)
        {
            Console.WriteLine($"Sample extracted vertices:");
            int samplesToShow = Math.Min(8, vertices.Count);
            for (int i = 0; i < samplesToShow; i++)
            {
                Console.WriteLine($"  Vertex {i}: ({vertices[i].X:F2}, {vertices[i].Y:F2}, {vertices[i].Z:F2})");
            }
        }
        
        return (vertices, indices, areas);
    }
    
    /// <summary>
    /// Extracts geometry from a static collider and adds it to the vertex/index lists.
    /// </summary>
    private void ExtractColliderGeometryFromStatic(StaticReference staticReference, TypedIndex shapeIndex,
        List<Vector3> vertices, List<int> indices, List<int> areas, ref int vertexOffset)
    {
        var pose = staticReference.Pose;
        
        // Access the shape from the simulation's shape collection
        var shapes = _physicsWorld.Simulation.Shapes;
        
        // Check shape type and extract geometry accordingly
        // For now, we'll handle Box shapes (most common for static obstacles)
        if (shapeIndex.Type == Box.Id)
        {
            // Get the box shape using the TypedIndex with explicit type argument
            ref var box = ref shapes.GetShape<Box>(shapeIndex.Index);
            ExtractBoxGeometry(box, pose, vertices, indices, areas, ref vertexOffset);
        }
        else if (shapeIndex.Type == Capsule.Id)
        {
            // Capsules can be approximated as cylinders for navmesh purposes
            // For now, skip capsules - they're typically used for dynamic objects
            // In a full implementation, you'd extract cylinder geometry here
        }
        // Add more shape types as needed (Sphere, Cylinder, Mesh, etc.)
        // For navmesh generation, we primarily care about static obstacles which are often boxes
    }
    
    /// <summary>
    /// Extracts geometry from a dynamic body collider and adds it to the vertex/index lists.
    /// (Kept for potential future use with dynamic obstacles)
    /// </summary>
    private void ExtractColliderGeometry(BodyReference bodyReference, 
        List<Vector3> vertices, List<int> indices, List<int> areas, ref int vertexOffset)
    {
        var pose = bodyReference.Pose;
        var collidable = bodyReference.Collidable;
        
        // Get shape index from collidable
        var shapeIndex = collidable.Shape;
        
        // Access the shape from the simulation's shape collection
        var shapes = _physicsWorld.Simulation.Shapes;
        
        // Check shape type and extract geometry accordingly
        // For now, we'll handle Box shapes (most common for static obstacles)
        if (shapeIndex.Type == Box.Id)
        {
            // Get the box shape using the TypedIndex with explicit type argument
            ref var box = ref shapes.GetShape<Box>(shapeIndex.Index);
            ExtractBoxGeometry(box, pose, vertices, indices, areas, ref vertexOffset);
        }
        else if (shapeIndex.Type == Capsule.Id)
        {
            // Capsules can be approximated as cylinders for navmesh purposes
            // For now, skip capsules - they're typically used for dynamic objects
            // In a full implementation, you'd extract cylinder geometry here
        }
        // Add more shape types as needed (Sphere, Cylinder, Mesh, etc.)
        // For navmesh generation, we primarily care about static obstacles which are often boxes
    }
    
    /// <summary>
    /// Extracts triangle mesh geometry from a box collider.
    /// For navmesh generation, we distinguish between horizontal surfaces (walkable)
    /// and vertical obstacles (should block navigation).
    /// 
    /// Key improvement: Vertical obstacles now include BOTTOM face geometry to properly
    /// fill the volume below them, preventing navmesh from generating in intersections.
    /// </summary>
    private void ExtractBoxGeometry(Box box, RigidPose pose, List<Vector3> vertices, List<int> indices, List<int> areas, ref int vertexOffset)
    {
        var halfSize = new Vector3(box.Width * 0.5f, box.Height * 0.5f, box.Length * 0.5f);
        
        // Determine if this is a horizontal surface (ground/floor) or vertical obstacle (wall)
        // Check the box dimensions: if height is much smaller than width/length, it's a floor
        // If height is larger than width or length, it's likely a vertical wall
        bool isHorizontalSurface = box.Height < box.Width && box.Height < box.Length;
        
        if (isHorizontalSurface)
        {
            // This is a ground/floor - extract top face as walkable surface
            var localVertices = new Vector3[]
            {
                new Vector3(-halfSize.X,  halfSize.Y, -halfSize.Z), // 0: top-left-front
                new Vector3( halfSize.X,  halfSize.Y, -halfSize.Z), // 1: top-right-front
                new Vector3( halfSize.X,  halfSize.Y,  halfSize.Z), // 2: top-right-back
                new Vector3(-halfSize.X,  halfSize.Y,  halfSize.Z)  // 3: top-left-back
            };
            
            // Transform to world space
            foreach (var localVertex in localVertices)
            {
                var worldVertex = Vector3.Transform(localVertex, pose.Orientation) + pose.Position;
                vertices.Add(worldVertex);
            }
            
            // Create top face (2 triangles wound CCW from above so normals point up)
            indices.Add(vertexOffset + 0);
            indices.Add(vertexOffset + 2);
            indices.Add(vertexOffset + 1);
            
            indices.Add(vertexOffset + 0);
            indices.Add(vertexOffset + 3);
            indices.Add(vertexOffset + 2);
            
            // Box colliders default to walkable (area 63)
            areas.Add(63); // First triangle
            areas.Add(63); // Second triangle
            
            vertexOffset += 4;
        }
        else
        {
            // This is a vertical obstacle (wall) - extract ALL faces to create a solid volume
            // that Recast will voxelize as unwalkable space.
            // CRITICAL: We include ALL six faces (including bottom) to ensure the volume
            // beneath the obstacle is properly marked as unwalkable, preventing navmesh
            // generation in areas where horizontal surfaces intersect with vertical obstacles.
            var localVertices = new Vector3[]
            {
                // Bottom face (4 vertices)
                new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z), // 0
                new Vector3( halfSize.X, -halfSize.Y, -halfSize.Z), // 1
                new Vector3( halfSize.X, -halfSize.Y,  halfSize.Z), // 2
                new Vector3(-halfSize.X, -halfSize.Y,  halfSize.Z), // 3
                // Top face (4 vertices)
                new Vector3(-halfSize.X,  halfSize.Y, -halfSize.Z), // 4
                new Vector3( halfSize.X,  halfSize.Y, -halfSize.Z), // 5
                new Vector3( halfSize.X,  halfSize.Y,  halfSize.Z), // 6
                new Vector3(-halfSize.X,  halfSize.Y,  halfSize.Z)  // 7
            };
            
            // Transform to world space
            foreach (var localVertex in localVertices)
            {
                var worldVertex = Vector3.Transform(localVertex, pose.Orientation) + pose.Position;
                vertices.Add(worldVertex);
            }
            
            // Create all 6 faces of the box (12 triangles total)
            // Each face is wound so normals point outward
            
            // Bottom face (pointing down) - CRITICAL for intersection handling
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 1); indices.Add(vertexOffset + 2);
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 2); indices.Add(vertexOffset + 3);
            areas.Add(0); areas.Add(0); // Unwalkable
            
            // Top face (pointing up)
            indices.Add(vertexOffset + 4); indices.Add(vertexOffset + 6); indices.Add(vertexOffset + 5);
            indices.Add(vertexOffset + 4); indices.Add(vertexOffset + 7); indices.Add(vertexOffset + 6);
            areas.Add(0); areas.Add(0); // Unwalkable
            
            // Front face (-Z)
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 5); indices.Add(vertexOffset + 1);
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 4); indices.Add(vertexOffset + 5);
            areas.Add(0); areas.Add(0); // Unwalkable
            
            // Back face (+Z)
            indices.Add(vertexOffset + 3); indices.Add(vertexOffset + 2); indices.Add(vertexOffset + 6);
            indices.Add(vertexOffset + 3); indices.Add(vertexOffset + 6); indices.Add(vertexOffset + 7);
            areas.Add(0); areas.Add(0); // Unwalkable
            
            // Left face (-X)
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 3); indices.Add(vertexOffset + 7);
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 7); indices.Add(vertexOffset + 4);
            areas.Add(0); areas.Add(0); // Unwalkable
            
            // Right face (+X)
            indices.Add(vertexOffset + 1); indices.Add(vertexOffset + 6); indices.Add(vertexOffset + 2);
            indices.Add(vertexOffset + 1); indices.Add(vertexOffset + 5); indices.Add(vertexOffset + 6);
            areas.Add(0); areas.Add(0); // Unwalkable
            
            vertexOffset += 8;
        }
    }
    
    /// <summary>
    /// Represents an axis-aligned bounding box for obstacle volumes.
    /// </summary>
    private struct ObstacleVolume
    {
        public Vector3 Min;
        public Vector3 Max;
        
        public ObstacleVolume(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }
        
        /// <summary>
        /// Tests if a point is inside this obstacle volume.
        /// </summary>
        public bool ContainsPoint(Vector3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }
        
        /// <summary>
        /// Tests if a triangle intersects with this obstacle volume.
        /// Uses multiple tests for robust intersection detection:
        /// 1. Vertex containment - check if any vertex is inside
        /// 2. Center containment - check if triangle center is inside
        /// 3. AABB overlap - check if triangle's bounding box overlaps
        /// 4. Edge intersection - check if triangle edges intersect the volume
        /// 5. Horizontal triangle check - explicit check for horizontal triangles inside vertical volumes
        /// </summary>
        public bool IntersectsTriangle(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            // Increased epsilon for thin ground planes that intersect with towers
            const float EPSILON = 0.1f; // Increased from 0.001f for better detection of ground planes
            
            // Test 1: Check if any vertex is inside the volume (with epsilon tolerance)
            if (ContainsPointWithEpsilon(v0, EPSILON) || 
                ContainsPointWithEpsilon(v1, EPSILON) || 
                ContainsPointWithEpsilon(v2, EPSILON))
                return true;
            
            // Test 2: Check if triangle center is inside the volume
            var center = (v0 + v1 + v2) / 3.0f;
            if (ContainsPointWithEpsilon(center, EPSILON))
                return true;
            
            // Test 3: Check if triangle's AABB overlaps with obstacle volume
            var triMin = Vector3.Min(Vector3.Min(v0, v1), v2);
            var triMax = Vector3.Max(Vector3.Max(v0, v1), v2);
            
            // Add epsilon to catch very thin surfaces
            triMin -= new Vector3(EPSILON);
            triMax += new Vector3(EPSILON);
            
            bool overlapsX = triMin.X <= Max.X && triMax.X >= Min.X;
            bool overlapsY = triMin.Y <= Max.Y && triMax.Y >= Min.Y;
            bool overlapsZ = triMin.Z <= Max.Z && triMax.Z >= Min.Z;
            
            if (!overlapsX || !overlapsY || !overlapsZ)
                return false; // No AABB overlap means no intersection
            
            // Test 4: Sample multiple points on the triangle surface
            // This catches cases where thin triangles pass through the volume
            // but vertices and center are outside
            for (float u = 0.25f; u <= 0.75f; u += 0.25f)
            {
                for (float v = 0.25f; v <= 0.75f; v += 0.25f)
                {
                    if (u + v <= 1.0f) // Valid barycentric coordinates
                    {
                        var point = v0 + u * (v1 - v0) + v * (v2 - v0);
                        if (ContainsPointWithEpsilon(point, EPSILON))
                            return true;
                    }
                }
            }
            
            // Test 5: Explicit check for horizontal triangles inside vertical volumes
            // This catches ground plane triangles that are inside tower volumes
            if (IsNearlyHorizontal(v0, v1, v2))
            {
                var horizontalCenter = (v0 + v1 + v2) / 3.0f;
                if (ContainsPointWithEpsilon(horizontalCenter, EPSILON))
                {
                    return true; // Horizontal triangle center is inside obstacle volume
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if a triangle is nearly horizontal (ground plane).
        /// Uses normal calculation to determine if triangle is flat.
        /// </summary>
        private bool IsNearlyHorizontal(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Cross(edge1, edge2);
            normal = Vector3.Normalize(normal);
            
            // Check if normal is pointing mostly upward (Y component close to 1)
            // Threshold: normal.Y > 0.9 means triangle is nearly horizontal
            return Math.Abs(normal.Y) > 0.9f;
        }
        
        /// <summary>
        /// Tests if a point is inside this obstacle volume with epsilon tolerance.
        /// </summary>
        private bool ContainsPointWithEpsilon(Vector3 point, float epsilon)
        {
            return point.X >= Min.X - epsilon && point.X <= Max.X + epsilon &&
                   point.Y >= Min.Y - epsilon && point.Y <= Max.Y + epsilon &&
                   point.Z >= Min.Z - epsilon && point.Z <= Max.Z + epsilon;
        }
    }
    
    /// <summary>
    /// Calculates the axis-aligned bounding box of a mesh.
    /// </summary>
    private (Vector3 Min, Vector3 Max) CalculateMeshBounds(Vector3[] vertices)
    {
        if (vertices.Length == 0)
            return (Vector3.Zero, Vector3.Zero);
        
        var min = vertices[0];
        var max = vertices[0];
        
        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        
        return (min, max);
    }
    
    /// <summary>
    /// Calculates the axis-aligned bounding box of a box collider in world space.
    /// </summary>
    private (Vector3 Min, Vector3 Max) CalculateBoxBounds(Box box, RigidPose pose)
    {
        var halfSize = new Vector3(box.Width * 0.5f, box.Height * 0.5f, box.Length * 0.5f);
        
        // Get all 8 corners of the box in local space
        var corners = new Vector3[]
        {
            new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),
            new Vector3( halfSize.X, -halfSize.Y, -halfSize.Z),
            new Vector3( halfSize.X, -halfSize.Y,  halfSize.Z),
            new Vector3(-halfSize.X, -halfSize.Y,  halfSize.Z),
            new Vector3(-halfSize.X,  halfSize.Y, -halfSize.Z),
            new Vector3( halfSize.X,  halfSize.Y, -halfSize.Z),
            new Vector3( halfSize.X,  halfSize.Y,  halfSize.Z),
            new Vector3(-halfSize.X,  halfSize.Y,  halfSize.Z)
        };
        
        // Transform to world space and find min/max
        var min = Vector3.Transform(corners[0], pose.Orientation) + pose.Position;
        var max = min;
        
        for (int i = 1; i < corners.Length; i++)
        {
            var worldCorner = Vector3.Transform(corners[i], pose.Orientation) + pose.Position;
            min = Vector3.Min(min, worldCorner);
            max = Vector3.Max(max, worldCorner);
        }
        
        return (min, max);
    }
    
    /// <summary>
    /// Filters walkable triangles that are inside obstacle volumes.
    /// This prevents navmesh generation in areas where thin walkable surfaces
    /// pass through solid obstacles (e.g., ground plane intersecting a wall).
    /// </summary>
    /// <returns>Number of triangles filtered</returns>
    private int FilterWalkableTrianglesInObstacles(
        List<Vector3> vertices, 
        List<int> indices, 
        List<int> areas, 
        List<ObstacleVolume> obstacleVolumes)
    {
        if (obstacleVolumes.Count == 0)
            return 0;
        
        int filteredCount = 0;
        int triangleCount = indices.Count / 3;
        
        for (int i = 0; i < triangleCount; i++)
        {
            // Only check walkable triangles
            if (areas[i] != 63) // Not walkable
                continue;
            
            // Get triangle vertices
            int idx0 = indices[i * 3];
            int idx1 = indices[i * 3 + 1];
            int idx2 = indices[i * 3 + 2];
            
            var v0 = vertices[idx0];
            var v1 = vertices[idx1];
            var v2 = vertices[idx2];
            
            // Check if this triangle intersects with any obstacle volume
            foreach (var obstacle in obstacleVolumes)
            {
                if (obstacle.IntersectsTriangle(v0, v1, v2))
                {
                    // Mark this walkable triangle as unwalkable
                    areas[i] = 0;
                    filteredCount++;
                    break; // No need to check other obstacles
                }
            }
        }
        
        return filteredCount;
    }
}

