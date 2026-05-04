using Spatial.Pathfinding;
using System.Collections.Generic;

namespace Spatial.Integration;

public enum LinkArcShape { Parabola, Linear, None }

public record LinkTraversalConfig(
    float Speed,
    float MinDuration,
    float ArcHeightScale,
    LinkArcShape ArcShape);

public static class LinkTraversalDefaults
{
    public static readonly Dictionary<OffMeshLinkType, LinkTraversalConfig> ByType = new()
    {
        [OffMeshLinkType.Jump]     = new(Speed: 4f, MinDuration: 0.8f, ArcHeightScale: 0.4f, ArcShape: LinkArcShape.Parabola),
        [OffMeshLinkType.Climb]    = new(Speed: 2f, MinDuration: 1.0f, ArcHeightScale: 0f,   ArcShape: LinkArcShape.Linear),
        [OffMeshLinkType.Teleport] = new(Speed: 0f, MinDuration: 0f,   ArcHeightScale: 0f,   ArcShape: LinkArcShape.None),
    };
}
