using System.Numerics;
using Spatial.Physics;
using Spatial.Pathfinding;
using BepuPhysics.Collidables;

namespace Spatial.Server;

/// <summary>
/// Helper class to build SimulationState from physics world and pathfinding data
/// </summary>
public static class SimulationStateBuilder
{
    /// <summary>
    /// Build a complete simulation state from the current physics world
    /// </summary>
    public static SimulationState BuildFromPhysicsWorld(
        PhysicsWorld physicsWorld,
        NavMeshData? navMeshData = null,
        PathResult? pathResult = null,
        int? pathEntityId = null)
    {
        var state = new SimulationState
        {
            Timestamp = (float)DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond
        };
        
        // Add all entities
        foreach (var entity in physicsWorld.EntityRegistry.GetAllEntities())
        {
            var entityState = BuildEntityState(physicsWorld, entity);
            state.Entities.Add(entityState);
        }
        
        // Add NavMesh if provided
        if (navMeshData != null)
        {
            state.NavMesh = BuildNavMeshGeometry(navMeshData);
        }
        
        // Add path if provided
        if (pathResult != null && pathResult.Success)
        {
            state.CurrentPath = new PathData
            {
                Waypoints = pathResult.Waypoints.Select(wp => new[] { wp.X, wp.Y, wp.Z }).ToList(),
                PathLength = pathResult.TotalLength,
                EntityId = pathEntityId ?? -1
            };
        }
        
        return state;
    }
    
    /// <summary>
    /// Build entity state from a physics entity
    /// </summary>
    private static EntityState BuildEntityState(PhysicsWorld physicsWorld, PhysicsEntity entity)
    {
        var position = physicsWorld.GetEntityPosition(entity);
        var velocity = physicsWorld.GetEntityVelocity(entity);
        
        // Get shape information
        var (shapeType, size) = GetShapeInfo(physicsWorld, entity);
        
        return new EntityState
        {
            Id = entity.EntityId,
            Type = entity.EntityType.ToString(),
            Position = new[] { position.X, position.Y, position.Z },
            Rotation = new[] { 0f, 0f, 0f, 1f }, // Identity quaternion for now
            Size = size,
            Velocity = new[] { velocity.X, velocity.Y, velocity.Z },
            IsStatic = entity.IsStatic,
            ShapeType = shapeType
        };
    }
    
    /// <summary>
    /// Get shape type and size from a physics entity
    /// </summary>
    private static (string ShapeType, float[] Size) GetShapeInfo(PhysicsWorld physicsWorld, PhysicsEntity entity)
    {
        var shapeIndex = entity.ShapeIndex;
        var shapes = physicsWorld.Simulation.Shapes;
        
        // Determine shape type from TypedIndex
        switch (shapeIndex.Type)
        {
            case Box.Id:
                {
                    var box = shapes.GetShape<Box>(shapeIndex.Index);
                    return ("Box", new[] { box.Width, box.Height, box.Length });
                }
            case Capsule.Id:
                {
                    var capsule = shapes.GetShape<Capsule>(shapeIndex.Index);
                    return ("Capsule", new[] { capsule.Radius * 2, capsule.Length + capsule.Radius * 2, capsule.Radius * 2 });
                }
            case Sphere.Id:
                {
                    var sphere = shapes.GetShape<Sphere>(shapeIndex.Index);
                    return ("Sphere", new[] { sphere.Radius * 2, sphere.Radius * 2, sphere.Radius * 2 });
                }
            default:
                return ("Unknown", new[] { 1f, 1f, 1f });
        }
    }
    
    /// <summary>
    /// Build NavMesh geometry from NavMeshData
    /// </summary>
    private static NavMeshGeometry BuildNavMeshGeometry(NavMeshData navMeshData)
    {
        var geometry = new NavMeshGeometry();
        
        if (navMeshData.NavMesh == null)
            return geometry;
        
        // Extract vertices and triangles from DotRecast NavMesh
        // This is a simplified version - you may need to iterate through all tiles
        var navMesh = navMeshData.NavMesh;
        
        // Get the first tile (tile 0)
        var tile = navMesh.GetTile(0);
        if (tile?.data != null)
        {
            var data = tile.data;
            
            // Extract vertices
            for (int i = 0; i < data.header.vertCount; i++)
            {
                int vertIndex = i * 3;
                geometry.Vertices.Add(new[]
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
                
                // Each polygon can have multiple vertices - triangulate it
                // Simple fan triangulation from first vertex
                for (int j = 2; j < poly.vertCount; j++)
                {
                    geometry.Indices.Add(poly.verts[0]);
                    geometry.Indices.Add(poly.verts[j - 1]);
                    geometry.Indices.Add(poly.verts[j]);
                }
            }
            
            geometry.PolygonCount = data.header.polyCount;
        }
        
        return geometry;
    }
}
