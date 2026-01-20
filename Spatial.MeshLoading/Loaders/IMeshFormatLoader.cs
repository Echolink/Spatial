using Spatial.MeshLoading.Data;

namespace Spatial.MeshLoading.Loaders;

/// <summary>
/// Interface for mesh format loaders.
/// Allows pluggable support for different file formats (.obj, .fbx, .gltf, etc.)
/// </summary>
public interface IMeshFormatLoader
{
    /// <summary>
    /// File extensions this loader supports (e.g., ".obj", ".fbx")
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }
    
    /// <summary>
    /// Loads mesh data from a file.
    /// Returns WorldData containing all meshes in the file.
    /// </summary>
    WorldData LoadMeshFile(string filePath);
    
    /// <summary>
    /// Checks if this loader can handle the given file.
    /// </summary>
    bool CanLoad(string filePath);
}
