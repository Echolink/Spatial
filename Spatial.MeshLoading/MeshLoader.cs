using Spatial.MeshLoading.Data;
using Spatial.MeshLoading.Loaders;
using Spatial.MeshLoading.Metadata;

namespace Spatial.MeshLoading;

/// <summary>
/// Main entry point for loading mesh files and metadata.
/// Automatically detects file format and applies metadata.
/// </summary>
public class MeshLoader
{
    private readonly List<IMeshFormatLoader> _formatLoaders;
    private readonly MetadataLoader _metadataLoader;
    
    public MeshLoader()
    {
        _formatLoaders = new List<IMeshFormatLoader>
        {
            new ObjMeshLoader()
            // Add more loaders here in the future: FbxMeshLoader, GltfMeshLoader, etc.
        };
        
        _metadataLoader = new MetadataLoader();
    }
    
    /// <summary>
    /// Loads a world from a mesh file with optional metadata.
    /// Automatically searches for metadata file if metadataPath is not specified.
    /// </summary>
    /// <param name="meshFilePath">Path to mesh file (.obj, .fbx, etc.)</param>
    /// <param name="metadataPath">Optional path to metadata .json file</param>
    /// <returns>WorldData with all meshes and applied metadata</returns>
    public WorldData LoadWorld(string meshFilePath, string? metadataPath = null)
    {
        if (!File.Exists(meshFilePath))
        {
            throw new FileNotFoundException($"Mesh file not found: {meshFilePath}");
        }
        
        Console.WriteLine($"\n[MeshLoader] Loading world from: {meshFilePath}");
        
        // Find appropriate loader for this file format
        var loader = FindLoaderForFile(meshFilePath);
        if (loader == null)
        {
            var extension = Path.GetExtension(meshFilePath);
            throw new NotSupportedException($"No loader found for file format: {extension}");
        }
        
        // Load mesh geometry
        var worldData = loader.LoadMeshFile(meshFilePath);
        
        // Load metadata (either from specified path or auto-detect)
        WorldMetadata? metadata = null;
        if (metadataPath != null)
        {
            metadata = _metadataLoader.LoadMetadata(metadataPath);
        }
        else
        {
            metadata = _metadataLoader.LoadMetadataForMeshFile(meshFilePath);
        }
        
        // Apply metadata to meshes
        if (metadata != null)
        {
            ApplyMetadata(worldData, metadata);
        }
        else
        {
            Console.WriteLine($"[MeshLoader] No metadata found - using default properties");
        }
        
        // Validate all meshes
        ValidateMeshes(worldData);
        
        Console.WriteLine($"[MeshLoader] World loaded successfully:");
        Console.WriteLine($"[MeshLoader]   Meshes: {worldData.Meshes.Count}");
        Console.WriteLine($"[MeshLoader]   Total vertices: {worldData.Meshes.Sum(m => m.Vertices.Count)}");
        Console.WriteLine($"[MeshLoader]   Total triangles: {worldData.TotalTriangles}");
        
        return worldData;
    }
    
    /// <summary>
    /// Applies metadata to all meshes in the world.
    /// </summary>
    private void ApplyMetadata(WorldData worldData, WorldMetadata metadata)
    {
        // Apply global transform
        if (metadata.Transform != null)
        {
            worldData.Transform = metadata.Transform.ToWorldTransform();
            
            if (!worldData.Transform.IsIdentity)
            {
                Console.WriteLine($"[MeshLoader] Applying global transform:");
                Console.WriteLine($"[MeshLoader]   Scale: {worldData.Transform.Scale}");
                Console.WriteLine($"[MeshLoader]   Rotation: {worldData.Transform.Rotation}");
                Console.WriteLine($"[MeshLoader]   Position: {worldData.Transform.Position}");
            }
        }
        
        // Apply per-mesh metadata
        foreach (var mesh in worldData.Meshes)
        {
            _metadataLoader.ApplyMetadataToMesh(mesh, metadata);
        }
        
        // Store metadata version
        worldData.MetadataVersion = metadata.Version;
    }
    
    /// <summary>
    /// Validates all meshes in the world.
    /// Throws if any mesh has invalid geometry.
    /// </summary>
    private void ValidateMeshes(WorldData worldData)
    {
        var invalidMeshes = worldData.Meshes.Where(m => !m.IsValid).ToList();
        
        if (invalidMeshes.Any())
        {
            var names = string.Join(", ", invalidMeshes.Select(m => $"'{m.Name}'"));
            throw new InvalidOperationException(
                $"World contains invalid meshes: {names}. " +
                $"Meshes must have at least 3 vertices and indices must be multiples of 3.");
        }
        
        // Warn about empty meshes
        var emptyMeshes = worldData.Meshes.Where(m => m.TriangleCount == 0).ToList();
        if (emptyMeshes.Any())
        {
            var names = string.Join(", ", emptyMeshes.Select(m => $"'{m.Name}'"));
            Console.WriteLine($"[MeshLoader] Warning: Empty meshes (no triangles): {names}");
        }
    }
    
    /// <summary>
    /// Finds an appropriate loader for the given file.
    /// </summary>
    private IMeshFormatLoader? FindLoaderForFile(string filePath)
    {
        return _formatLoaders.FirstOrDefault(loader => loader.CanLoad(filePath));
    }
    
    /// <summary>
    /// Gets all supported file extensions.
    /// </summary>
    public IReadOnlyList<string> SupportedExtensions => 
        _formatLoaders.SelectMany(l => l.SupportedExtensions).ToList();
}
