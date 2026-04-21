using Spatial.Pathfinding;
using System.Runtime.CompilerServices;

namespace Spatial.Integration;

/// <summary>
/// Holds one NavMeshData per registered AgentConfig, baked from the same map geometry.
///
/// Usage:
///   var navMesh = new MultiAgentNavMesh("worlds/arena.obj")
///       .Add(goblinConfig)
///       .Add(humanConfig)
///       .Add(trollConfig)
///       .Bake();
///   using var world = new World(navMesh);
///
/// The same AgentConfig instance used in Add() must be passed to World.Spawn() — lookup
/// uses reference equality so the game server should keep configs as static/singleton fields.
/// </summary>
public class MultiAgentNavMesh
{
    private readonly string _meshFilePath;
    private readonly List<AgentConfig> _configs = new();

    internal Dictionary<AgentConfig, NavMeshData> NavMeshes { get; } =
        new(ReferenceEqualityComparer.Instance);

    public MultiAgentNavMesh(string meshFilePath)
    {
        _meshFilePath = meshFilePath;
    }

    /// <summary>
    /// Registers an agent config to be baked. Call before Bake().
    /// </summary>
    public MultiAgentNavMesh Add(AgentConfig config)
    {
        _configs.Add(config);
        return this;
    }

    /// <summary>
    /// Bakes one NavMesh per registered AgentConfig. CPU-intensive — call once at startup.
    /// </summary>
    public MultiAgentNavMesh Bake(NavMeshConfiguration? navConfig = null)
    {
        if (_configs.Count == 0)
            throw new InvalidOperationException("Add at least one AgentConfig before calling Bake().");

        foreach (var config in _configs)
            NavMeshes[config] = World.BakeNavMesh(_meshFilePath, config, navConfig);

        return this;
    }

    /// <summary>
    /// Returns the first registered config, used as the default when no config is specified at spawn.
    /// </summary>
    internal AgentConfig DefaultConfig => _configs[0];
}
