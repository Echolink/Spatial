using Spatial.Pathfinding;

namespace Vtest
{
    public sealed class SpatialOptions
    {
        public string Path { get; set; } = "Content/Maps";
        public string MapId { get; set; } = "flat_plane.obj";
        public AgentConfig[] Agents { get; set; } = Array.Empty<AgentConfig>();
    }
}