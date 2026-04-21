using System.Numerics;
using Spatial.Integration;
using Spatial.Pathfinding;

namespace Spatial.TestHarness;

static class TestObstacleSpawn
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"[FAIL] {message}");
    }

    public static void TestObstacleSpawnNoOverlap(string meshPath)
    {
        var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
        using var world = new World(navMesh, AgentConfig.Player);

        world.Spawn(1, new Vector3(0, 0, 0));
        for (int i = 0; i < 30; i++) world.Update(0.016f);

        var result = world.SpawnObstacle(100, new Vector3(10, 0, 10), new Vector3(2, 2, 2));

        Assert(result.Spawned, "obstacle should spawn when footprint is clear");
        Assert(result.DisplacedEntityIds.Count == 0, "no units should be displaced");
        Assert(result.Entity != null, "entity should be non-null on success");

        Console.WriteLine("[PASS] ObstacleSpawnNoOverlap");
    }

    public static void TestObstacleSpawnRejectedByOverlap(string meshPath)
    {
        var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
        using var world = new World(navMesh, AgentConfig.Player);

        world.Spawn(1, new Vector3(0, 0, 0));
        for (int i = 0; i < 30; i++) world.Update(0.016f);

        var result = world.SpawnObstacle(100, new Vector3(0, 0, 0), new Vector3(2, 2, 2));

        Assert(!result.Spawned, "spawn should be rejected when unit is inside footprint");
        Assert(result.DisplacedEntityIds.Contains(1), "blocking unit ID should be returned");
        Assert(result.Entity == null, "entity should be null on rejection");

        var unitPos = world.GetPosition(1);
        Assert(MathF.Abs(unitPos.X) < 1f, "unit should not have been moved on rejection");

        Console.WriteLine("[PASS] ObstacleSpawnRejectedByOverlap");
    }

    public static void TestObstacleForceSpawnPushesUnit(string meshPath)
    {
        var config = AgentConfig.Player;
        var navMesh = World.BakeNavMesh(meshPath, config);
        using var world = new World(navMesh, config);

        var spawnPos = new Vector3(0, 0, 0);
        world.Spawn(1, spawnPos);
        for (int i = 0; i < 30; i++) world.Update(0.016f);

        var unitPosBefore = world.GetPosition(1);

        var obstacleSize = new Vector3(2, 2, 2);
        var result = world.SpawnObstacle(100, spawnPos, obstacleSize, forceSpawn: true);

        Assert(result.Spawned, "force spawn should always succeed");
        Assert(result.DisplacedEntityIds.Contains(1), "unit should be listed as displaced");

        float halfX = obstacleSize.X * 0.5f + config.Radius + 0.1f;
        float halfZ = obstacleSize.Z * 0.5f + config.Radius + 0.1f;
        var unitPosAfter = world.GetPosition(1);
        bool outsideFootprint =
            MathF.Abs(unitPosAfter.X - spawnPos.X) >= halfX ||
            MathF.Abs(unitPosAfter.Z - spawnPos.Z) >= halfZ;

        Console.WriteLine($"  Unit before: {unitPosBefore}");
        Console.WriteLine($"  Unit after:  {unitPosAfter}  (halfX={halfX:F2}, halfZ={halfZ:F2})");
        Assert(outsideFootprint, "unit must be outside obstacle footprint after push");

        Console.WriteLine("[PASS] ObstacleForceSpawnPushesUnit");
    }

    public static void TestPushedUnitMovementStopped(string meshPath)
    {
        var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
        using var world = new World(navMesh, AgentConfig.Player);

        world.Spawn(1, new Vector3(0, 0, 0));
        for (int i = 0; i < 30; i++) world.Update(0.016f);

        world.Move(1, new Vector3(15, 0, 15));
        for (int i = 0; i < 10; i++) world.Update(0.016f);

        var velBeforePush = world.GetVelocity(1);
        Console.WriteLine($"  Speed before push: {velBeforePush.Length():F3} m/s");

        world.SpawnObstacle(100, new Vector3(0, 0, 0), new Vector3(2, 2, 2), forceSpawn: true);

        var velImmediate = world.GetVelocity(1);
        Console.WriteLine($"  Speed immediately after push: {velImmediate.Length():F3} m/s  vel={velImmediate}");

        for (int i = 0; i < 5; i++) world.Update(0.016f);

        var vel = world.GetVelocity(1);
        float speed = vel.Length();
        Console.WriteLine($"  Unit speed after 5 settle ticks: {speed:F3} m/s  pos={world.GetPosition(1)}");

        var resp = world.Move(1, new Vector3(10, 0, 10));
        Assert(resp.Success, "re-issued Move after push should succeed");

        Console.WriteLine("[PASS] PushedUnitMovementStopped");
    }

    public static void TestMultipleUnitsPushedOut(string meshPath)
    {
        var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
        using var world = new World(navMesh, AgentConfig.Player);

        world.Spawn(1, new Vector3(-0.5f, 0, 0));
        world.Spawn(2, new Vector3( 0.5f, 0, 0));
        for (int i = 0; i < 30; i++) world.Update(0.016f);

        var result = world.SpawnObstacle(100, new Vector3(0, 0, 0), new Vector3(4, 2, 4), forceSpawn: true);

        Assert(result.Spawned, "obstacle should spawn");
        Assert(result.DisplacedEntityIds.Count == 2, "both units should be displaced");
        Assert(result.DisplacedEntityIds.Contains(1) && result.DisplacedEntityIds.Contains(2),
               "both entity IDs should appear in displaced list");

        float halfX = 4 * 0.5f + AgentConfig.Player.Radius + 0.1f;
        float halfZ = 4 * 0.5f + AgentConfig.Player.Radius + 0.1f;

        foreach (int id in new[] { 1, 2 })
        {
            var pos = world.GetPosition(id);
            bool outside = MathF.Abs(pos.X) >= halfX || MathF.Abs(pos.Z) >= halfZ;
            Console.WriteLine($"  Entity {id} pos after push: {pos}  outside={outside}");
            Assert(outside, $"entity {id} must be outside footprint after push");
        }

        Console.WriteLine("[PASS] MultipleUnitsPushedOut");
    }

    public static void TestDespawnObstacleRestoresWalkability(string meshPath)
    {
        var navConfig = new NavMeshConfiguration { EnableTileUpdates = true };
        var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player, navConfig);
        using var world = new World(navMesh, AgentConfig.Player);

        // Spawn unit FIRST so it lands on a valid navmesh position before the obstacle clears tiles.
        world.Spawn(1, new Vector3(-5, 0, 0));
        // Activate motor immediately to keep the unit grounded during settle ticks.
        world.Move(1, new Vector3(-4.9f, 0, 0));
        for (int i = 0; i < 30; i++) world.Update(0.016f);
        world.StopMove(1);

        var obstaclePos = new Vector3(5, 0, 0);
        var obstacleSize = new Vector3(2, 2, 2);

        var spawnResult = world.SpawnObstacle(100, obstaclePos, obstacleSize);
        Assert(spawnResult.Spawned, "obstacle should spawn");

        var resp1 = world.Move(1, new Vector3(10, 0, 0));
        Console.WriteLine($"  Move with obstacle present: Success={resp1.Success}, Adjusted={resp1.WasTargetAdjusted}");

        world.DespawnObstacle(100);

        // After despawn the navmesh tile is rebuilt; teleport to a clean navmesh position
        // (the unit may have drifted while the tile was rebuilding) and re-issue the move.
        world.Teleport(1, new Vector3(-5, 0, 0));
        for (int i = 0; i < 5; i++) world.Update(0.016f);

        var resp2 = world.Move(1, new Vector3(10, 0, 0));
        Console.WriteLine($"  Move after DespawnObstacle: Success={resp2.Success}, Adjusted={resp2.WasTargetAdjusted}");
        Assert(resp2.Success, "move to the same target should succeed after obstacle removed");

        Console.WriteLine("[PASS] DespawnObstacleRestoresWalkability");
    }

    public static void TestRetryAfterRejectionSucceeds(string meshPath)
    {
        var navMesh = World.BakeNavMesh(meshPath, AgentConfig.Player);
        using var world = new World(navMesh, AgentConfig.Player);

        world.Spawn(1, new Vector3(0, 0, 0));
        world.Move(1, new Vector3(0.1f, 0, 0)); // activate motor for stable grounding
        for (int i = 0; i < 30; i++) world.Update(0.016f);
        world.StopMove(1);

        var obstaclePos  = new Vector3(0, 0, 0);
        var obstacleSize = new Vector3(2, 2, 2);

        var attempt1 = world.SpawnObstacle(100, obstaclePos, obstacleSize);
        Assert(!attempt1.Spawned, "first attempt should be rejected");

        foreach (var blockerId in attempt1.DisplacedEntityIds)
            world.Teleport(blockerId, new Vector3(7, 0, 7));

        for (int i = 0; i < 30; i++) world.Update(0.016f);

        var attempt2 = world.SpawnObstacle(100, obstaclePos, obstacleSize);
        Assert(attempt2.Spawned, "second attempt should succeed after unit is moved away");
        Assert(attempt2.DisplacedEntityIds.Count == 0, "no units displaced on clean retry");

        Console.WriteLine("[PASS] RetryAfterRejectionSucceeds");
    }
}
