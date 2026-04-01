namespace Spatial.Pathfinding;

/// <summary>
/// Configuration for NavMesh generation and runtime tile management.
///
/// When <see cref="EnableTileUpdates"/> is false (default) the NavMesh is generated as a
/// single monolithic tile — the fastest path for static worlds.
///
/// When <see cref="EnableTileUpdates"/> is true the NavMesh is generated as a grid of
/// fixed-size tiles. Individual tiles can then be rebuilt at runtime via
/// <see cref="Pathfinding.Pathfinder.RebuildTile"/> or
/// <see cref="Integration.PathfindingService.RebuildNavMeshRegion"/>.
/// Use this for worlds with doors, destructible terrain, or moving platforms.
/// </summary>
public class NavMeshConfiguration
{
    /// <summary>
    /// World-space width and depth of each tile (meters).
    /// Smaller tiles = finer-grained updates but more tiles to manage.
    /// Default: 32 units. Reasonable range: 16–64 units.
    /// </summary>
    public float TileSize { get; set; } = 32.0f;

    /// <summary>
    /// Whether to generate a tile-based NavMesh that supports runtime tile updates.
    /// When false, a monolithic single-tile NavMesh is generated (default, best performance).
    /// When true, a multi-tile NavMesh is generated, enabling <see cref="Pathfinding.Pathfinder.RebuildTile"/>.
    /// Default: false
    /// </summary>
    public bool EnableTileUpdates { get; set; } = false;

    /// <summary>
    /// Maximum number of tiles in the multi-tile NavMesh.
    /// Must be large enough to cover the entire world at the chosen <see cref="TileSize"/>.
    /// Ignored when <see cref="EnableTileUpdates"/> is false.
    /// Default: 256 (covers 16x16 tiles of 32 units each = 512x512 world)
    /// </summary>
    public int MaxTiles { get; set; } = 256;

    /// <summary>
    /// Maximum number of polygons per tile.
    /// Increase if tiles are large or geometry is complex (results in build failure if too low).
    /// Ignored when <see cref="EnableTileUpdates"/> is false.
    /// Default: 2048
    /// </summary>
    public int MaxPolysPerTile { get; set; } = 2048;
}
