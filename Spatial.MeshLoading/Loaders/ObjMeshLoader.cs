using System.Numerics;
using System.Globalization;
using System.Text.RegularExpressions;
using Spatial.MeshLoading.Data;
using Spatial.Pathfinding;

namespace Spatial.MeshLoading.Loaders;

/// <summary>
/// Loads .obj (Wavefront OBJ) mesh files.
/// OBJ is a simple text-based format widely supported by 3D tools like Blender.
/// </summary>
public class ObjMeshLoader : IMeshFormatLoader
{
    // Matches: offmesh_jump_01_start, offmesh_teleport_02_end, offmesh_climb_03_start
    private static readonly Regex OffMeshPattern =
        new(@"^offmesh_(jump|teleport|climb)_(\w+)_(start|end)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        var globalVertices = new List<Vector3>();
        var globalNormals = new List<Vector3>();
        var globalUVs = new List<Vector2>();

        MeshData? currentMesh = null;
        string currentMeshName = "default";

        var meshVertexIndices = new List<int>();
        var meshVertices = new List<Vector3>();

        // Keyed by (type, id, role) → centroid position; paired after parsing
        var halfDefs = new Dictionary<(OffMeshLinkType type, string id, string role), Vector3>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            var command = parts[0].ToLowerInvariant();

            switch (command)
            {
                case "o":
                case "g":
                    if (currentMesh != null && meshVertices.Count > 0)
                    {
                        FinalizeMesh(currentMesh, meshVertices, meshVertexIndices);
                        FinalizeGroup(currentMesh, meshVertices, halfDefs, worldData);
                    }

                    currentMeshName = parts.Length > 1 ? string.Join("_", parts.Skip(1)) : "unnamed";
                    currentMesh = new MeshData { Name = currentMeshName };
                    meshVertices.Clear();
                    meshVertexIndices.Clear();
                    break;

                case "v":
                    if (parts.Length >= 4)
                        globalVertices.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;

                case "vn":
                    if (parts.Length >= 4)
                        globalNormals.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;

                case "vt":
                    if (parts.Length >= 3)
                        globalUVs.Add(new Vector2(ParseFloat(parts[1]), ParseFloat(parts[2])));
                    break;

                case "f":
                    if (currentMesh == null)
                    {
                        currentMeshName = "default";
                        currentMesh = new MeshData { Name = currentMeshName };
                    }
                    ParseFace(parts, globalVertices, globalNormals, globalUVs,
                              meshVertices, meshVertexIndices);
                    break;

                case "usemtl":
                case "mtllib":
                case "s":
                    break;
            }
        }

        // Finalize last group
        if (currentMesh != null && meshVertices.Count > 0)
        {
            FinalizeMesh(currentMesh, meshVertices, meshVertexIndices);
            FinalizeGroup(currentMesh, meshVertices, halfDefs, worldData);
        }

        if (worldData.Meshes.Count == 0 && globalVertices.Count > 0)
        {
            Console.WriteLine($"[ObjMeshLoader] No objects/groups found, creating single default mesh");
            currentMesh = new MeshData { Name = "default" };
            currentMesh.Vertices.AddRange(globalVertices);
            worldData.Meshes.Add(currentMesh);
        }

        // Pair start+end half-defs into complete OffMeshLinkDef objects
        var starts = halfDefs.Where(kv => kv.Key.role == "start").ToList();
        foreach (var s in starts)
        {
            var endKey = (s.Key.type, s.Key.id, "end");
            if (halfDefs.TryGetValue(endKey, out var endPos))
            {
                worldData.OffMeshLinks.Add(new OffMeshLinkDef(s.Key.id, s.Key.type, s.Value, endPos));
                Console.WriteLine($"[ObjMeshLoader] Off-mesh link: {s.Key.type} '{s.Key.id}'  {s.Value} → {endPos}");
            }
            else
            {
                Console.WriteLine($"[ObjMeshLoader] WARNING: offmesh_{s.Key.type}_{s.Key.id}_start has no matching _end");
            }
        }
    }

    private void FinalizeGroup(MeshData mesh, List<Vector3> vertices,
        Dictionary<(OffMeshLinkType type, string id, string role), Vector3> halfDefs,
        WorldData worldData)
    {
        var match = OffMeshPattern.Match(mesh.Name);
        if (match.Success)
        {
            var type = Enum.Parse<OffMeshLinkType>(match.Groups[1].Value, ignoreCase: true);
            var id   = match.Groups[2].Value;
            var role = match.Groups[3].Value.ToLowerInvariant();
            // Use average XZ but minimum Y (bottom of sphere = terrain contact point).
            // The sphere marker's centroid Y sits at sphere center, which is above the
            // navmesh surface by roughly the sphere radius. Using minY ensures the link
            // endpoint snaps to the nearest polygon during NavMesh build.
            float cx = vertices.Sum(v => v.X) / vertices.Count;
            float cy = vertices.Min(v => v.Y);
            float cz = vertices.Sum(v => v.Z) / vertices.Count;
            var centroid = new Vector3(cx, cy, cz);
            halfDefs[(type, id, role)] = centroid;
            // Marker geometry is intentionally NOT added to worldData.Meshes
        }
        else
        {
            worldData.Meshes.Add(mesh);
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
