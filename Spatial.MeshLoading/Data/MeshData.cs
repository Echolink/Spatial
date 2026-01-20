using System.Numerics;

namespace Spatial.MeshLoading.Data;

/// <summary>
/// Represents loaded mesh geometry data.
/// Contains vertices, indices, and optional metadata like normals and UVs.
/// </summary>
public class MeshData
{
    /// <summary>
    /// Name of the mesh (from file or metadata)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Vertex positions (XYZ)
    /// </summary>
    public List<Vector3> Vertices { get; set; } = new();
    
    /// <summary>
    /// Triangle indices (3 indices per triangle)
    /// </summary>
    public List<int> Indices { get; set; } = new();
    
    /// <summary>
    /// Vertex normals (optional, same count as Vertices)
    /// </summary>
    public List<Vector3> Normals { get; set; } = new();
    
    /// <summary>
    /// UV texture coordinates (optional)
    /// </summary>
    public List<Vector2> UVs { get; set; } = new();
    
    /// <summary>
    /// Physics properties for this mesh (from metadata)
    /// </summary>
    public MeshPhysicsProperties PhysicsProperties { get; set; } = new();
    
    /// <summary>
    /// Returns true if the mesh has valid geometry
    /// </summary>
    public bool IsValid => Vertices.Count >= 3 && Indices.Count >= 3 && Indices.Count % 3 == 0;
    
    /// <summary>
    /// Number of triangles in the mesh
    /// </summary>
    public int TriangleCount => Indices.Count / 3;
}

/// <summary>
/// NavMesh area types for Recast navigation mesh generation.
/// Following DotRecast conventions.
/// </summary>
public enum NavMeshAreaType
{
    /// <summary>
    /// Walkable surface (area ID 63 / 0x3F) - Default for ground, floors, stairs
    /// </summary>
    Walkable = 0,
    
    /// <summary>
    /// Unwalkable/blocking volume (area ID 0) - Blocks navigation (walls, building volumes)
    /// </summary>
    Unwalkable = 1,
    
    /// <summary>
    /// Ignore - Don't include in navmesh generation at all
    /// </summary>
    Ignore = 2
}

/// <summary>
/// Physics properties for a mesh loaded from metadata.
/// </summary>
public class MeshPhysicsProperties
{
    /// <summary>
    /// Entity type (Player, NPC, StaticObject, etc.)
    /// </summary>
    public string EntityType { get; set; } = "StaticObject";
    
    /// <summary>
    /// Whether the mesh is static (immovable) or dynamic
    /// </summary>
    public bool IsStatic { get; set; } = true;
    
    /// <summary>
    /// Surface friction coefficient (0.0 - 1.0)
    /// </summary>
    public float Friction { get; set; } = 0.5f;
    
    /// <summary>
    /// Bounciness/restitution (0.0 - 1.0)
    /// </summary>
    public float Restitution { get; set; } = 0.0f;
    
    /// <summary>
    /// NavMesh area type - determines how this mesh affects navigation mesh generation.
    /// Walkable: Creates navigable surface (ground, floors)
    /// Unwalkable: Blocks navigation (walls, building interiors)
    /// Ignore: Not included in navmesh at all (decorative objects)
    /// </summary>
    public NavMeshAreaType NavMeshArea { get; set; } = NavMeshAreaType.Walkable;
}
