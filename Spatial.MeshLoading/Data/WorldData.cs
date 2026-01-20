using System.Numerics;

namespace Spatial.MeshLoading.Data;

/// <summary>
/// Represents a complete world loaded from mesh files and metadata.
/// Contains all meshes and global transform information.
/// </summary>
public class WorldData
{
    /// <summary>
    /// Name/identifier for this world
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Source file path
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;
    
    /// <summary>
    /// All meshes in the world
    /// </summary>
    public List<MeshData> Meshes { get; set; } = new();
    
    /// <summary>
    /// Global transform to apply to all meshes
    /// </summary>
    public WorldTransform Transform { get; set; } = new();
    
    /// <summary>
    /// Metadata version (for future compatibility)
    /// </summary>
    public string MetadataVersion { get; set; } = "1.0";
    
    /// <summary>
    /// Returns true if the world has at least one valid mesh
    /// </summary>
    public bool IsValid => Meshes.Any(m => m.IsValid);
    
    /// <summary>
    /// Total triangle count across all meshes
    /// </summary>
    public int TotalTriangles => Meshes.Sum(m => m.TriangleCount);
}

/// <summary>
/// Global transform to apply to an entire world.
/// </summary>
public class WorldTransform
{
    /// <summary>
    /// Global scale multiplier (XYZ)
    /// </summary>
    public Vector3 Scale { get; set; } = Vector3.One;
    
    /// <summary>
    /// Global rotation in Euler angles (degrees)
    /// </summary>
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    
    /// <summary>
    /// Global position offset (XYZ)
    /// </summary>
    public Vector3 Position { get; set; } = Vector3.Zero;
    
    /// <summary>
    /// Returns true if this is an identity transform (no changes)
    /// </summary>
    public bool IsIdentity => Scale == Vector3.One && Rotation == Vector3.Zero && Position == Vector3.Zero;
}
