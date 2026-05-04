using System.Numerics;
using Spatial.Integration;
using Spatial.MeshLoading;
using Spatial.Pathfinding;
using Spatial.Physics;

namespace Vtest;

public class SpatialService
{
    private readonly SpatialOptions _options;
    private global::Spatial.Integration.World _world;

    public SpatialService()
    {
        _options = new SpatialOptions()
        {
            Agents = new[]
            {
                new AgentConfig()
                {
                    Radius = 0.3f,
                    Height = 1.4f,
                    MaxClimb = 0.3f,
                    MaxSlope = 40f
                }
            }
        };
    }

    public void Init()
    {
        string meshPath = Path.Combine(AppContext.BaseDirectory, _options.Path, _options.MapId);
        MultiAgentNavMesh multiNavMesh = new MultiAgentNavMesh(meshPath);
        foreach (var agent in _options.Agents)
        {
            multiNavMesh.Add(agent);
        }
        multiNavMesh.Bake();
        _world = new global::Spatial.Integration.World(multiNavMesh);

        // Load the mesh geometry into the physics world so agents have ground to stand on.
        // BakeNavMesh() uses a temporary physics world (discarded after baking), so the
        // live physics world starts empty — without this the agent falls forever.
        var worldBuilder = new WorldBuilder(_world.Physics, new MeshLoader());
        worldBuilder.LoadAndBuildWorld(meshPath);

    }

    public void Tick()
    {
        _world.Update(0.016f);
    }
    
    public void Move(int entityId, Vector3 position)
    {
        var response = _world.Move( entityId, position);
        if (response.Success)
            Console.WriteLine(
                $"{entityId} is moving to {response.ActualTargetPosition}. Distance : {response.EstimatedPathLength}. ETA : {response.EstimatedPathLength}");
        else
        {
            Console.WriteLine($"{entityId} failed to move to {response.ActualTargetPosition}. Reason: {response.Message}");
        }
    }

    public void Stop(int entityId)
    {
        _world.StopMove(entityId);
    }
    
    public void Spawn(int entityId, Vector3 position)
    {
        _world.Spawn(entityId, position, _options.Agents[0], EntityType.NPC);
    }
    
    public void Despawn(int entityId)
    {
        _world.Despawn(entityId);
    }

    public Vector3 GetPosition(int entityId)
    {
        return _world.GetPosition(entityId);
    }

}