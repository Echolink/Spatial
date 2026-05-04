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
    /// <param name="getTraversalInfo">
    /// Optional delegate: given an entityId, returns (typeName, normalizedT) or null.
    /// Use this to forward off-mesh link traversal state from the character controller.
    /// </param>
    public static SimulationState BuildFromPhysicsWorld(
        PhysicsWorld physicsWorld,
        NavMeshData? navMeshData = null,
        PathResult? pathResult = null,
        int? pathEntityId = null,
        Func<int, (string type, float t)?>? getTraversalInfo = null)
    {
        var state = new SimulationState
        {
            Timestamp = (float)DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond
        };

        // Add all entities
        foreach (var entity in physicsWorld.EntityRegistry.GetAllEntities())
        {
            var entityState = BuildEntityState(physicsWorld, entity);

            if (getTraversalInfo != null)
            {
                var info = getTraversalInfo(entity.EntityId);
                if (info.HasValue)
                {
                    entityState.TraversalType = info.Value.type;
                    entityState.TraversalT    = info.Value.t;
                }
            }

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
        
        var entityState = new EntityState
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
        
        // Check if this entity has mesh data
        var meshData = physicsWorld.GetMeshData(entity.EntityId);
        if (meshData.HasValue)
        {
            entityState.ShapeType = "Mesh";
            entityState.Mesh = BuildMeshGeometry(meshData.Value.vertices, meshData.Value.indices);
        }
        
        return entityState;
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
    /// Build mesh geometry from vertex and index data
    /// </summary>
    private static MeshGeometry BuildMeshGeometry(Vector3[] vertices, int[] indices)
    {
        var geometry = new MeshGeometry();
        
        // Convert Vector3[] to List<float[]>
        foreach (var vertex in vertices)
        {
            geometry.Vertices.Add(new[] { vertex.X, vertex.Y, vertex.Z });
        }
        
        // Copy indices
        geometry.Indices.AddRange(indices);
        
        return geometry;
    }
    
    /// <summary>
    /// Build NavMesh geometry from NavMeshData — iterates every tile so tiled NavMeshes
    /// are fully serialized and tile erasure/rebuild is visible in Unity.
    /// </summary>
    private static NavMeshGeometry BuildNavMeshGeometry(NavMeshData navMeshData)
    {
        var geometry = new NavMeshGeometry();

        if (navMeshData.NavMesh == null)
            return geometry;

        var navMesh = navMeshData.NavMesh;
        int maxTiles = navMesh.GetMaxTiles();
        int vertexOffset = 0;

        for (int tileIdx = 0; tileIdx < maxTiles; tileIdx++)
        {
            var tile = navMesh.GetTile(tileIdx);
            if (tile?.data == null) continue;

            var data = tile.data;

            for (int i = 0; i < data.header.vertCount; i++)
            {
                int vi = i * 3;
                geometry.Vertices.Add(new[] { data.verts[vi], data.verts[vi + 1], data.verts[vi + 2] });
            }

            for (int i = 0; i < data.header.polyCount; i++)
            {
                var poly = data.polys[i];
                // Only render walkable ground polygons (flags bit 0 = walkable).
                // Non-walkable polygons (obstacle footprint, area=0) are intentionally
                // excluded so tile rebuilds produce a visible hole in the overlay.
                if ((poly.flags & 0x01) == 0) continue;
                for (int j = 2; j < poly.vertCount; j++)
                {
                    geometry.Indices.Add(poly.verts[0]     + vertexOffset);
                    geometry.Indices.Add(poly.verts[j - 1] + vertexOffset);
                    geometry.Indices.Add(poly.verts[j]     + vertexOffset);
                }
            }

            geometry.PolygonCount += data.header.polyCount;
            vertexOffset += data.header.vertCount;
        }

        return geometry;
    }
}
