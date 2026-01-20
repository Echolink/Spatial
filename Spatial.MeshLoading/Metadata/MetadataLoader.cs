using System.Text.Json;
using System.Text.Json.Serialization;
using Spatial.MeshLoading.Data;

namespace Spatial.MeshLoading.Metadata;

/// <summary>
/// Loads and parses metadata from JSON sidecar files.
/// </summary>
public class MetadataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    /// <summary>
    /// Loads metadata from a JSON file.
    /// Returns null if file doesn't exist (use defaults instead).
    /// </summary>
    public WorldMetadata? LoadMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath))
        {
            Console.WriteLine($"[MetadataLoader] No metadata file found at: {metadataPath}");
            Console.WriteLine($"[MetadataLoader] Using default properties for all meshes");
            return null;
        }
        
        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<WorldMetadata>(json, JsonOptions);
            
            if (metadata == null)
            {
                Console.WriteLine($"[MetadataLoader] Failed to parse metadata file: {metadataPath}");
                return null;
            }
            
            Console.WriteLine($"[MetadataLoader] Loaded metadata from: {metadataPath}");
            Console.WriteLine($"[MetadataLoader]   Version: {metadata.Version}");
            Console.WriteLine($"[MetadataLoader]   Mesh entries: {metadata.Meshes.Count}");
            
            return metadata;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[MetadataLoader] JSON parsing error in {metadataPath}: {ex.Message}");
            throw new InvalidOperationException($"Failed to parse metadata file: {metadataPath}", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MetadataLoader] Error loading metadata from {metadataPath}: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Tries to load metadata from standard locations.
    /// Searches for: meshFile.obj.json, meshFile.json, meshFile.meta.json
    /// Returns null if no metadata file is found.
    /// </summary>
    public WorldMetadata? LoadMetadataForMeshFile(string meshFilePath)
    {
        // Try standard naming conventions
        var searchPaths = new[]
        {
            meshFilePath + ".json",           // arena.obj.json
            Path.ChangeExtension(meshFilePath, ".json"),  // arena.json
            Path.ChangeExtension(meshFilePath, ".meta.json")  // arena.meta.json
        };
        
        foreach (var searchPath in searchPaths)
        {
            if (File.Exists(searchPath))
            {
                return LoadMetadata(searchPath);
            }
        }
        
        Console.WriteLine($"[MetadataLoader] No metadata file found for: {meshFilePath}");
        Console.WriteLine($"[MetadataLoader] Searched: {string.Join(", ", searchPaths.Select(Path.GetFileName))}");
        return null;
    }
    
    /// <summary>
    /// Applies metadata to a mesh's physics properties.
    /// Uses default values for any properties not specified in metadata.
    /// </summary>
    public void ApplyMetadataToMesh(MeshData mesh, WorldMetadata? metadata)
    {
        if (metadata == null)
        {
            // Use all defaults
            return;
        }
        
        // Find matching metadata entry (exact match or pattern match)
        var exactMatch = metadata.Meshes.FirstOrDefault(m => m.Name == mesh.Name);
        var patternMatch = exactMatch == null 
            ? metadata.Meshes.FirstOrDefault(m => m.MatchesMesh(mesh.Name))
            : null;
        
        var matchingEntry = exactMatch ?? patternMatch;
        
        if (matchingEntry != null)
        {
            Console.WriteLine($"[MetadataLoader] Applying metadata to mesh '{mesh.Name}' (matched: '{matchingEntry.Name}')");
        }
        
        // Apply entity type
        mesh.PhysicsProperties.EntityType = 
            matchingEntry?.EntityType 
            ?? metadata.DefaultEntityType 
            ?? mesh.PhysicsProperties.EntityType;
        
        // Apply static flag
        mesh.PhysicsProperties.IsStatic = 
            matchingEntry?.IsStatic 
            ?? metadata.DefaultIsStatic 
            ?? mesh.PhysicsProperties.IsStatic;
        
        // Apply material properties
        if (matchingEntry?.Material != null)
        {
            if (matchingEntry.Material.Friction.HasValue)
            {
                mesh.PhysicsProperties.Friction = matchingEntry.Material.Friction.Value;
            }
            
            if (matchingEntry.Material.Restitution.HasValue)
            {
                mesh.PhysicsProperties.Restitution = matchingEntry.Material.Restitution.Value;
            }
        }
        
        // Apply NavMesh area type
        if (!string.IsNullOrEmpty(matchingEntry?.NavMeshArea))
        {
            if (Enum.TryParse<NavMeshAreaType>(matchingEntry.NavMeshArea, ignoreCase: true, out var areaType))
            {
                mesh.PhysicsProperties.NavMeshArea = areaType;
            }
            else
            {
                Console.WriteLine($"[MetadataLoader] Warning: Invalid NavMeshArea '{matchingEntry.NavMeshArea}' for mesh '{mesh.Name}'. Valid values: Walkable, Unwalkable, Ignore");
            }
        }
    }
    
    /// <summary>
    /// Validates metadata and returns a list of validation errors.
    /// Returns empty list if metadata is valid.
    /// </summary>
    public List<string> ValidateMetadata(WorldMetadata metadata)
    {
        var errors = new List<string>();
        
        // Check version
        if (string.IsNullOrWhiteSpace(metadata.Version))
        {
            errors.Add("Metadata version is missing or empty");
        }
        
        // Check mesh entries
        for (int i = 0; i < metadata.Meshes.Count; i++)
        {
            var entry = metadata.Meshes[i];
            
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                errors.Add($"Mesh entry at index {i} has missing or empty 'name' field");
            }
            
            // Validate material values are in valid range
            if (entry.Material != null)
            {
                if (entry.Material.Friction.HasValue && 
                    (entry.Material.Friction.Value < 0 || entry.Material.Friction.Value > 1))
                {
                    errors.Add($"Mesh '{entry.Name}': friction must be between 0.0 and 1.0");
                }
                
                if (entry.Material.Restitution.HasValue && 
                    (entry.Material.Restitution.Value < 0 || entry.Material.Restitution.Value > 1))
                {
                    errors.Add($"Mesh '{entry.Name}': restitution must be between 0.0 and 1.0");
                }
            }
        }
        
        // Validate transform arrays have correct length
        if (metadata.Transform != null)
        {
            if (metadata.Transform.Scale != null && metadata.Transform.Scale.Length != 3)
            {
                errors.Add($"Transform scale must have exactly 3 values [x, y, z], got {metadata.Transform.Scale.Length}");
            }
            
            if (metadata.Transform.Rotation != null && metadata.Transform.Rotation.Length != 3)
            {
                errors.Add($"Transform rotation must have exactly 3 values [x, y, z], got {metadata.Transform.Rotation.Length}");
            }
            
            if (metadata.Transform.Position != null && metadata.Transform.Position.Length != 3)
            {
                errors.Add($"Transform position must have exactly 3 values [x, y, z], got {metadata.Transform.Position.Length}");
            }
        }
        
        return errors;
    }
}
