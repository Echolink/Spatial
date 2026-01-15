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
    /// </summary>
    /// <param name="agentConfig">Agent configuration for NavMesh generation</param>
    /// <returns>Generated navigation mesh data</returns>
    public NavMeshData BuildNavMeshFromPhysicsWorld(AgentConfig agentConfig)
    {
        // Extract geometry from static colliders
        var (vertices, indices) = ExtractStaticColliderGeometry();
        
        // Generate NavMesh from geometry
        return _navMeshGenerator.GenerateNavMesh(vertices, indices, agentConfig);
    }
    
    /// <summary>
    /// Extracts triangle mesh geometry from all static colliders.
    /// This converts BepuPhysics colliders to a format DotRecast can use.
    /// </summary>
    private (List<Vector3> vertices, List<int> indices) ExtractStaticColliderGeometry()
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        int vertexOffset = 0;
        
        // Iterate through all entities and extract static collider geometry
        int staticEntityCount = 0;
        foreach (var entity in _physicsWorld.EntityRegistry.GetAllEntities())
        {
            if (!entity.IsStatic)
                continue;
            
            staticEntityCount++;
            
            // Get static reference (static entities use Statics collection, not Bodies)
            var staticReference = _physicsWorld.Simulation.Statics[entity.StaticHandle];
            
            // Extract geometry from collider using shape index stored in entity
            ExtractColliderGeometryFromStatic(staticReference, entity.ShapeIndex, vertices, indices, ref vertexOffset);
        }
        
        Console.WriteLine($"Extracted geometry from {staticEntityCount} static entities:");
        Console.WriteLine($"  Vertices: {vertices.Count}");
        Console.WriteLine($"  Indices: {indices.Count}");
        Console.WriteLine($"  Triangles: {indices.Count / 3}");
        
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
        
        return (vertices, indices);
    }
    
    /// <summary>
    /// Extracts geometry from a static collider and adds it to the vertex/index lists.
    /// </summary>
    private void ExtractColliderGeometryFromStatic(StaticReference staticReference, TypedIndex shapeIndex,
        List<Vector3> vertices, List<int> indices, ref int vertexOffset)
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
            ExtractBoxGeometry(box, pose, vertices, indices, ref vertexOffset);
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
        List<Vector3> vertices, List<int> indices, ref int vertexOffset)
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
            ExtractBoxGeometry(box, pose, vertices, indices, ref vertexOffset);
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
    /// </summary>
    private void ExtractBoxGeometry(Box box, RigidPose pose, List<Vector3> vertices, List<int> indices, ref int vertexOffset)
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
            
            vertexOffset += 4;
        }
        else
        {
            // This is a vertical obstacle (wall) - extract ALL faces to create a solid volume
            // that Recast will voxelize as unwalkable space
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
            
            // Bottom face (pointing down)
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 1); indices.Add(vertexOffset + 2);
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 2); indices.Add(vertexOffset + 3);
            
            // Top face (pointing up)
            indices.Add(vertexOffset + 4); indices.Add(vertexOffset + 6); indices.Add(vertexOffset + 5);
            indices.Add(vertexOffset + 4); indices.Add(vertexOffset + 7); indices.Add(vertexOffset + 6);
            
            // Front face (-Z)
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 5); indices.Add(vertexOffset + 1);
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 4); indices.Add(vertexOffset + 5);
            
            // Back face (+Z)
            indices.Add(vertexOffset + 3); indices.Add(vertexOffset + 2); indices.Add(vertexOffset + 6);
            indices.Add(vertexOffset + 3); indices.Add(vertexOffset + 6); indices.Add(vertexOffset + 7);
            
            // Left face (-X)
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 3); indices.Add(vertexOffset + 7);
            indices.Add(vertexOffset + 0); indices.Add(vertexOffset + 7); indices.Add(vertexOffset + 4);
            
            // Right face (+X)
            indices.Add(vertexOffset + 1); indices.Add(vertexOffset + 6); indices.Add(vertexOffset + 2);
            indices.Add(vertexOffset + 1); indices.Add(vertexOffset + 5); indices.Add(vertexOffset + 6);
            
            vertexOffset += 8;
        }
    }
}

