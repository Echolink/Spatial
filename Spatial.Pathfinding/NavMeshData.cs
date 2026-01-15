using DotRecast.Detour;

namespace Spatial.Pathfinding;

/// <summary>
/// Container for navigation mesh data.
/// The NavMesh is generated once and reused for multiple pathfinding queries.
/// </summary>
public class NavMeshData
{
    /// <summary>
    /// The Detour navigation mesh.
    /// This is what we query for paths.
    /// </summary>
    public DtNavMesh NavMesh { get; }
    
    /// <summary>
    /// Query object for finding paths on the NavMesh.
    /// </summary>
    public DtNavMeshQuery Query { get; }
    
    public NavMeshData(DtNavMesh navMesh, DtNavMeshQuery query)
    {
        NavMesh = navMesh;
        Query = query;
    }
}

