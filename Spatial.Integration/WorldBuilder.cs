using System.Numerics;
using Spatial.Physics;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;

namespace Spatial.Integration;

/// <summary>
/// Builds physics worlds from loaded mesh files and metadata.
/// Bridges the gap between mesh loading and physics simulation.
/// </summary>
public class WorldBuilder
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly MeshLoader _meshLoader;
    private int _nextEntityId = 2000; // Start mesh entities at 2000 to avoid conflicts
    
    public WorldBuilder(PhysicsWorld physicsWorld, MeshLoader meshLoader)
    {
        _physicsWorld = physicsWorld;
        _meshLoader = meshLoader;
    }
    
    /// <summary>
    /// Loads a world from a mesh file and builds it in the physics world.
    /// </summary>
    /// <param name="meshFilePath">Path to mesh file (.obj, etc.)</param>
    /// <param name="metadataPath">Optional path to metadata file</param>
    /// <returns>WorldData for the loaded world</returns>
    public WorldData LoadAndBuildWorld(string meshFilePath, string? metadataPath = null)
    {
        Console.WriteLine($"\n[WorldBuilder] Loading world from file: {meshFilePath}");
        
        // Load mesh and metadata
        var worldData = _meshLoader.LoadWorld(meshFilePath, metadataPath);
        
        // Build physics entities from world data
        BuildWorld(worldData);
        
        return worldData;
    }
    
    /// <summary>
    /// Builds physics entities from loaded world data.
    /// </summary>
    /// <param name="worldData">World data to build from</param>
    public void BuildWorld(WorldData worldData)
    {
        Console.WriteLine($"\n[WorldBuilder] Building world: {worldData.Name}");
        Console.WriteLine($"[WorldBuilder] Processing {worldData.Meshes.Count} meshes...");
        
        int successCount = 0;
        int skippedCount = 0;
        
        foreach (var mesh in worldData.Meshes)
        {
            if (!mesh.IsValid || mesh.TriangleCount == 0)
            {
                Console.WriteLine($"[WorldBuilder] Skipping invalid/empty mesh: {mesh.Name}");
                skippedCount++;
                continue;
            }
            
            try
            {
                BuildMeshEntity(mesh, worldData.Transform);
                successCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldBuilder] Error building mesh '{mesh.Name}': {ex.Message}");
                skippedCount++;
            }
        }
        
        Console.WriteLine($"[WorldBuilder] Build complete:");
        Console.WriteLine($"[WorldBuilder]   Success: {successCount} meshes");
        if (skippedCount > 0)
        {
            Console.WriteLine($"[WorldBuilder]   Skipped: {skippedCount} meshes");
        }
    }
    
    /// <summary>
    /// Builds a physics entity from a single mesh.
    /// </summary>
    private void BuildMeshEntity(MeshData mesh, WorldTransform transform)
    {
        var entityId = _nextEntityId++;
        
        // Apply global transform to vertices if needed
        var transformedVertices = ApplyTransform(mesh.Vertices.ToArray(), transform);
        
        // Determine entity type from metadata
        var entityType = ParseEntityType(mesh.PhysicsProperties.EntityType);
        
        // Map NavMeshAreaType from MeshLoading to PhysicsWorld
        var navMeshArea = mesh.PhysicsProperties.NavMeshArea switch
        {
            Spatial.MeshLoading.Data.NavMeshAreaType.Walkable => PhysicsWorld.NavMeshAreaType.Walkable,
            Spatial.MeshLoading.Data.NavMeshAreaType.Unwalkable => PhysicsWorld.NavMeshAreaType.Unwalkable,
            Spatial.MeshLoading.Data.NavMeshAreaType.Ignore => PhysicsWorld.NavMeshAreaType.Ignore,
            _ => PhysicsWorld.NavMeshAreaType.Walkable
        };
        
        // Create mesh entity in physics world
        // The mesh is stored and can be extracted for navmesh generation
        _physicsWorld.RegisterMeshEntity(
            entityId,
            entityType,
            transformedVertices,
            mesh.Indices.ToArray(),
            position: Vector3.Zero, // Position is already baked into vertices
            navMeshArea: navMeshArea
        );
        
        Console.WriteLine($"[WorldBuilder]   Created entity '{mesh.Name}' (ID: {entityId}, Type: {entityType}, NavMesh: {navMeshArea})");
        Console.WriteLine($"[WorldBuilder]     Vertices: {mesh.Vertices.Count}, Triangles: {mesh.TriangleCount}");
    }
    
    /// <summary>
    /// Applies global transform to vertices.
    /// </summary>
    private Vector3[] ApplyTransform(Vector3[] vertices, WorldTransform transform)
    {
        if (transform.IsIdentity)
        {
            return vertices;
        }
        
        var result = new Vector3[vertices.Length];
        
        // Create transformation matrix
        var rotationMatrix = CreateRotationMatrix(transform.Rotation);
        
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            
            // Apply scale
            v *= transform.Scale;
            
            // Apply rotation
            v = Vector3.Transform(v, rotationMatrix);
            
            // Apply translation
            v += transform.Position;
            
            result[i] = v;
        }
        
        return result;
    }
    
    /// <summary>
    /// Creates a rotation matrix from Euler angles in degrees.
    /// </summary>
    private Quaternion CreateRotationMatrix(Vector3 eulerDegrees)
    {
        var radians = eulerDegrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }
    
    /// <summary>
    /// Parses entity type string to EntityType enum.
    /// </summary>
    private EntityType ParseEntityType(string typeString)
    {
        return typeString.ToLowerInvariant() switch
        {
            "player" => EntityType.Player,
            "npc" => EntityType.NPC,
            "enemy" => EntityType.Enemy,
            "staticobject" => EntityType.StaticObject,
            "obstacle" => EntityType.Obstacle,
            "temporaryobstacle" => EntityType.TemporaryObstacle,
            "projectile" => EntityType.Projectile,
            _ => EntityType.StaticObject // Default to static
        };
    }
    
    /// <summary>
    /// Adds procedural elements to the world after loading from file.
    /// This supports the hybrid approach: load base world from file, then add dynamic elements.
    /// </summary>
    /// <param name="proceduralBuilder">Action that adds procedural elements to the physics world</param>
    public void AddProceduralElements(Action<PhysicsWorld> proceduralBuilder)
    {
        Console.WriteLine($"\n[WorldBuilder] Adding procedural elements...");
        proceduralBuilder(_physicsWorld);
        Console.WriteLine($"[WorldBuilder] Procedural elements added");
    }
}
