using System.Numerics;

namespace Spatial.MeshLoading.Data;

/// <summary>
/// Metadata loaded from .json sidecar files.
/// All fields are optional - the system provides sensible defaults.
/// </summary>
public class WorldMetadata
{
    /// <summary>
    /// Metadata schema version
    /// </summary>
    public string Version { get; set; } = "1.0";
    
    /// <summary>
    /// Default entity type for meshes without specific configuration
    /// </summary>
    public string? DefaultEntityType { get; set; }
    
    /// <summary>
    /// Default static flag for meshes without specific configuration
    /// </summary>
    public bool? DefaultIsStatic { get; set; }
    
    /// <summary>
    /// Per-mesh configuration overrides.
    /// Supports wildcard patterns (e.g., "wall_*", "*_ground")
    /// </summary>
    public List<MeshMetadataEntry> Meshes { get; set; } = new();
    
    /// <summary>
    /// Global transform to apply to the entire world
    /// </summary>
    public TransformMetadata? Transform { get; set; }
}

/// <summary>
/// Metadata for a specific mesh or mesh pattern.
/// </summary>
public class MeshMetadataEntry
{
    /// <summary>
    /// Mesh name or wildcard pattern (e.g., "ground", "wall_*")
    /// REQUIRED field
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Entity type override
    /// </summary>
    public string? EntityType { get; set; }
    
    /// <summary>
    /// Static flag override
    /// </summary>
    public bool? IsStatic { get; set; }
    
    /// <summary>
    /// Material properties override
    /// </summary>
    public MaterialMetadata? Material { get; set; }
    
    /// <summary>
    /// NavMesh area type: "Walkable", "Unwalkable", or "Ignore"
    /// - Walkable: Ground, floors, stairs (creates navigable surface)
    /// - Unwalkable: Walls, building volumes (blocks navigation)
    /// - Ignore: Decorative objects (not included in navmesh)
    /// </summary>
    public string? NavMeshArea { get; set; }
    
    /// <summary>
    /// Checks if this entry's name pattern matches the given mesh name.
    /// Supports wildcards: "wall_*" matches "wall_north", "wall_south", etc.
    /// </summary>
    public bool MatchesMesh(string meshName)
    {
        if (string.IsNullOrEmpty(Name))
            return false;
        
        // Exact match takes priority
        if (Name == meshName)
            return true;
        
        // Wildcard matching
        if (Name.Contains('*'))
        {
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(Name).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(meshName, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        return false;
    }
}

/// <summary>
/// Material physics properties metadata.
/// </summary>
public class MaterialMetadata
{
    /// <summary>
    /// Surface friction (0.0 - 1.0)
    /// </summary>
    public float? Friction { get; set; }
    
    /// <summary>
    /// Bounciness/restitution (0.0 - 1.0)
    /// </summary>
    public float? Restitution { get; set; }
}

/// <summary>
/// Transform metadata for global world adjustments.
/// </summary>
public class TransformMetadata
{
    /// <summary>
    /// Scale multiplier [x, y, z]
    /// </summary>
    public float[]? Scale { get; set; }
    
    /// <summary>
    /// Rotation in Euler angles (degrees) [x, y, z]
    /// </summary>
    public float[]? Rotation { get; set; }
    
    /// <summary>
    /// Position offset [x, y, z]
    /// </summary>
    public float[]? Position { get; set; }
    
    /// <summary>
    /// Converts to WorldTransform with proper defaults
    /// </summary>
    public WorldTransform ToWorldTransform()
    {
        return new WorldTransform
        {
            Scale = Scale != null && Scale.Length == 3 
                ? new Vector3(Scale[0], Scale[1], Scale[2]) 
                : Vector3.One,
            Rotation = Rotation != null && Rotation.Length == 3 
                ? new Vector3(Rotation[0], Rotation[1], Rotation[2]) 
                : Vector3.Zero,
            Position = Position != null && Position.Length == 3 
                ? new Vector3(Position[0], Position[1], Position[2]) 
                : Vector3.Zero
        };
    }
}
