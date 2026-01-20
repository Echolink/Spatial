using System.Numerics;
using System.Globalization;
using Spatial.MeshLoading.Data;

namespace Spatial.MeshLoading.Loaders;

/// <summary>
/// Loads .obj (Wavefront OBJ) mesh files.
/// OBJ is a simple text-based format widely supported by 3D tools like Blender.
/// </summary>
public class ObjMeshLoader : IMeshFormatLoader
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".obj" };
    
    public bool CanLoad(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
    
    public WorldData LoadMeshFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Mesh file not found: {filePath}");
        }
        
        Console.WriteLine($"[ObjMeshLoader] Loading mesh from: {filePath}");
        
        var worldData = new WorldData
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            SourceFile = filePath
        };
        
        try
        {
            // Parse OBJ file
            var lines = File.ReadAllLines(filePath);
            ParseObjFile(lines, worldData);
            
            Console.WriteLine($"[ObjMeshLoader] Loaded {worldData.Meshes.Count} mesh(es)");
            Console.WriteLine($"[ObjMeshLoader] Total vertices: {worldData.Meshes.Sum(m => m.Vertices.Count)}");
            Console.WriteLine($"[ObjMeshLoader] Total triangles: {worldData.TotalTriangles}");
            
            return worldData;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load OBJ file: {filePath}", ex);
        }
    }
    
    private void ParseObjFile(string[] lines, WorldData worldData)
    {
        // Temporary storage for global vertex/normal/uv data
        var globalVertices = new List<Vector3>();
        var globalNormals = new List<Vector3>();
        var globalUVs = new List<Vector2>();
        
        // Current mesh being built
        MeshData? currentMesh = null;
        string currentMeshName = "default";
        
        // Temporary indices for current mesh
        var meshVertexIndices = new List<int>();
        var meshVertices = new List<Vector3>();
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;
            
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;
            
            var command = parts[0].ToLowerInvariant();
            
            switch (command)
            {
                case "o":  // Object name
                case "g":  // Group name
                    // Start a new mesh
                    if (currentMesh != null && meshVertices.Count > 0)
                    {
                        FinalizeMesh(currentMesh, meshVertices, meshVertexIndices);
                        worldData.Meshes.Add(currentMesh);
                    }
                    
                    currentMeshName = parts.Length > 1 ? string.Join("_", parts.Skip(1)) : "unnamed";
                    currentMesh = new MeshData { Name = currentMeshName };
                    meshVertices.Clear();
                    meshVertexIndices.Clear();
                    break;
                
                case "v":  // Vertex position
                    if (parts.Length >= 4)
                    {
                        var x = ParseFloat(parts[1]);
                        var y = ParseFloat(parts[2]);
                        var z = ParseFloat(parts[3]);
                        globalVertices.Add(new Vector3(x, y, z));
                    }
                    break;
                
                case "vn":  // Vertex normal
                    if (parts.Length >= 4)
                    {
                        var x = ParseFloat(parts[1]);
                        var y = ParseFloat(parts[2]);
                        var z = ParseFloat(parts[3]);
                        globalNormals.Add(new Vector3(x, y, z));
                    }
                    break;
                
                case "vt":  // Texture coordinate
                    if (parts.Length >= 3)
                    {
                        var u = ParseFloat(parts[1]);
                        var v = ParseFloat(parts[2]);
                        globalUVs.Add(new Vector2(u, v));
                    }
                    break;
                
                case "f":  // Face
                    // Ensure we have a current mesh
                    if (currentMesh == null)
                    {
                        currentMeshName = "default";
                        currentMesh = new MeshData { Name = currentMeshName };
                    }
                    
                    ParseFace(parts, globalVertices, globalNormals, globalUVs,
                             meshVertices, meshVertexIndices);
                    break;
                
                case "usemtl":  // Material (we ignore for now, but could use for grouping)
                case "mtllib":  // Material library (ignore)
                case "s":       // Smooth shading (ignore)
                    // Ignore these for physics purposes
                    break;
            }
        }
        
        // Finalize last mesh
        if (currentMesh != null && meshVertices.Count > 0)
        {
            FinalizeMesh(currentMesh, meshVertices, meshVertexIndices);
            worldData.Meshes.Add(currentMesh);
        }
        
        // If no objects/groups were defined, create a single default mesh
        if (worldData.Meshes.Count == 0 && globalVertices.Count > 0)
        {
            Console.WriteLine($"[ObjMeshLoader] No objects/groups found, creating single default mesh");
            currentMesh = new MeshData { Name = "default" };
            
            // All vertices go into this mesh, we need to reprocess faces
            // For simplicity, if no grouping was used, we'll need to reparse
            // Let's just use all global vertices directly
            currentMesh.Vertices.AddRange(globalVertices);
            
            // We need to create indices - this is tricky without reparsing faces
            // For now, assume faces were already processed into meshVertices/indices
            worldData.Meshes.Add(currentMesh);
        }
    }
    
    private void ParseFace(string[] parts, List<Vector3> globalVertices, List<Vector3> globalNormals,
                          List<Vector2> globalUVs, List<Vector3> meshVertices, List<int> meshIndices)
    {
        // OBJ faces can have 3+ vertices (triangle or polygon)
        // Format: f v1 v2 v3 or f v1/vt1 v2/vt2 v3/vt3 or f v1/vt1/vn1 v2/vt2/vn2 v3/vt3/vn3
        
        var faceVertexIndices = new List<int>();
        
        for (int i = 1; i < parts.Length; i++)
        {
            var vertexData = parts[i].Split('/');
            
            // Parse vertex index (1-based in OBJ, convert to 0-based)
            if (int.TryParse(vertexData[0], out int vIndex))
            {
                vIndex = vIndex < 0 ? globalVertices.Count + vIndex : vIndex - 1;
                
                if (vIndex >= 0 && vIndex < globalVertices.Count)
                {
                    // Add vertex to mesh and record its index
                    int localIndex = meshVertices.Count;
                    meshVertices.Add(globalVertices[vIndex]);
                    faceVertexIndices.Add(localIndex);
                }
            }
        }
        
        // Triangulate face if it has more than 3 vertices (fan triangulation)
        if (faceVertexIndices.Count >= 3)
        {
            for (int i = 1; i < faceVertexIndices.Count - 1; i++)
            {
                meshIndices.Add(faceVertexIndices[0]);
                meshIndices.Add(faceVertexIndices[i]);
                meshIndices.Add(faceVertexIndices[i + 1]);
            }
        }
    }
    
    private void FinalizeMesh(MeshData mesh, List<Vector3> vertices, List<int> indices)
    {
        mesh.Vertices.AddRange(vertices);
        mesh.Indices.AddRange(indices);
        
        Console.WriteLine($"[ObjMeshLoader]   Mesh '{mesh.Name}': {mesh.Vertices.Count} vertices, {mesh.TriangleCount} triangles");
    }
    
    private float ParseFloat(string value)
    {
        // OBJ files use period as decimal separator regardless of locale
        return float.Parse(value, CultureInfo.InvariantCulture);
    }
}
