using System.Numerics;

namespace Spatial.Server;

/// <summary>
/// Complete state of the simulation to send to Unity client
/// </summary>
public class SimulationState
{
    public List<EntityState> Entities { get; set; } = new();
    public NavMeshGeometry? NavMesh { get; set; }
    public PathData? CurrentPath { get; set; }
    public List<PathData> AgentPaths { get; set; } = new(); // Paths for all agents
    public float Timestamp { get; set; }
}

/// <summary>
/// State of a single entity (physics body)
/// </summary>
public class EntityState
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public float[] Position { get; set; } = new float[3]; // [x,y,z]
    public float[] Rotation { get; set; } = new float[4]; // quaternion [x,y,z,w]
    public float[] Size { get; set; } = new float[3]; // dimensions [width, height, depth]
    public float[] Velocity { get; set; } = new float[3]; // [x,y,z]
    public bool IsStatic { get; set; }
    public string ShapeType { get; set; } = "Box"; // Box, Capsule, Sphere, etc.
    public MeshGeometry? Mesh { get; set; } // Optional mesh data for mesh entities
}

/// <summary>
/// Mesh geometry data for entity visualization
/// </summary>
public class MeshGeometry
{
    public List<float[]> Vertices { get; set; } = new(); // List of [x,y,z]
    public List<int> Indices { get; set; } = new(); // Triangle indices (groups of 3)
}

/// <summary>
/// NavMesh geometry data for visualization
/// </summary>
public class NavMeshGeometry
{
    public List<float[]> Vertices { get; set; } = new(); // List of [x,y,z]
    public List<int> Indices { get; set; } = new(); // Triangle indices (groups of 3)
    public int PolygonCount { get; set; }
}

/// <summary>
/// Pathfinding path data
/// </summary>
public class PathData
{
    public List<float[]> Waypoints { get; set; } = new(); // List of [x,y,z]
    public float PathLength { get; set; }
    public int EntityId { get; set; } // Which entity is following this path
}
