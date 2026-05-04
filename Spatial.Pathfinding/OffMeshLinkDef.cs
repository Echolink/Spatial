using System.Numerics;

namespace Spatial.Pathfinding;

public enum OffMeshLinkType { Jump, Teleport, Climb }

public class OffMeshLinkDef
{
    public string Id { get; }
    public OffMeshLinkType Type { get; }
    public Vector3 Start { get; }
    public Vector3 End { get; }

    public OffMeshLinkDef(string id, OffMeshLinkType type, Vector3 start, Vector3 end)
    {
        Id = id;
        Type = type;
        Start = start;
        End = end;
    }
}
