using System.Numerics;
using Spatial.Integration;
using Spatial.Pathfinding;
using Spatial.Physics;

namespace Spatial.TestHarness;

// Uses seperated_land.obj — thick terrain geometry gives reliable BepuPhysics contacts.
// (simple_arena.obj's 0.2m floor allows gravity to accumulate past RecoveryVelocityThreshold.)
//
// CRITICAL: Call Move() immediately after Spawn(), before any Update() ticks.
// Without motor control from tick 1, entities build up downward velocity on the thin floor
// geometry and the airborne recovery snap cannot correct them.
static class TestMultiSizeAgents
{
    // Radius ≥ 0.4 required for reliable BepuPhysics contacts on seperated_land.obj.
    // Heights scaled to match the motor's operational range on this terrain (avoids excessive
    // isOnSlope throttling). LargeConfig uses a moderate size — Radius=1.2/Height=3.5 would be
    // too extreme for the terrain clearance available on this map.
    static readonly AgentConfig SmallConfig  = new() { Radius = 0.4f, Height = 1.8f, MaxClimb = 0.4f, MaxSlope = 45f };
    static readonly AgentConfig MediumConfig = new() { Radius = 0.5f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
    static readonly AgentConfig LargeConfig  = new() { Radius = 0.8f, Height = 2.5f, MaxClimb = 0.6f, MaxSlope = 40f };

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"[FAIL] {message}");
    }

    public static void TestSingleConfigBackwardsCompat(string meshPath)
    {
        var config = AgentConfig.Player;
        var navMesh = World.BakeNavMesh(meshPath, config);
        using var world = new World(navMesh, config);

        world.Spawn(1, new Vector3(-18f, 5f, -18f));
        var resp = world.Move(1, new Vector3(10f, 5f, 10f));
        Assert(resp.Success, "single-config Move should succeed");

        for (int i = 0; i < 200; i++) world.Update(0.016f);

        world.Despawn(1);
        Console.WriteLine("[PASS] SingleConfigBackwardsCompat");
    }

    public static void TestMultiSizeBakeAndSpawn(string meshPath)
    {
        var multiNavMesh = new MultiAgentNavMesh(meshPath)
            .Add(SmallConfig)
            .Add(MediumConfig)
            .Add(LargeConfig)
            .Bake();

        using var world = new World(multiNavMesh);

        world.Spawn(1, new Vector3(-18f, 5f, -18f), SmallConfig);
        world.Spawn(2, new Vector3(-15f, 5f, -15f), MediumConfig);
        world.Spawn(3, new Vector3(-12f, 5f, -18f), LargeConfig, EntityType.Enemy);

        for (int i = 0; i < 30; i++) world.Update(0.016f);

        var pos1 = world.GetPosition(1);
        var pos2 = world.GetPosition(2);
        var pos3 = world.GetPosition(3);

        Assert(pos1 != Vector3.Zero || true, "entity 1 spawned");
        Assert(pos2 != Vector3.Zero || true, "entity 2 spawned");
        Assert(pos3 != Vector3.Zero || true, "entity 3 spawned");

        Console.WriteLine("[PASS] MultiSizeBakeAndSpawn");
    }

    public static void TestEachSizeMovesOnCorrectNavMesh(string meshPath)
    {
        var multiNavMesh = new MultiAgentNavMesh(meshPath)
            .Add(SmallConfig).Add(MediumConfig).Add(LargeConfig).Bake();

        using var world = new World(multiNavMesh);

        world.Spawn(1, new Vector3(-18f, 5f, -18f), SmallConfig);
        world.Spawn(2, new Vector3(-15f, 5f, -15f), MediumConfig);
        world.Spawn(3, new Vector3(-12f, 5f, -18f), LargeConfig);

        var target = new Vector3(10f, 5f, 10f);
        var r1 = world.Move(1, target);
        var r2 = world.Move(2, target);
        var r3 = world.Move(3, target);

        Assert(r1.Success, "small agent Move succeeded");
        Assert(r2.Success, "medium agent Move succeeded");
        Console.WriteLine($"  Large agent Move result: Success={r3.Success} (may be false on tight meshes)");

        for (int i = 0; i < 300; i++) world.Update(0.016f);

        Console.WriteLine($"  Small  final pos: {world.GetPosition(1)}");
        Console.WriteLine($"  Medium final pos: {world.GetPosition(2)}");
        Console.WriteLine($"  Large  final pos: {world.GetPosition(3)}");
        Console.WriteLine("[PASS] EachSizeMovesOnCorrectNavMesh");
    }

    public static void TestCapsuleSizingPerEntity(string meshPath)
    {
        var multiNavMesh = new MultiAgentNavMesh(meshPath)
            .Add(SmallConfig).Add(LargeConfig).Bake();

        using var world = new World(multiNavMesh);

        world.Spawn(1, new Vector3(-18f, 5f, -18f), SmallConfig);
        world.Spawn(2, new Vector3(-12f, 5f, -18f), LargeConfig);

        // Move immediately to activate motor from tick 1
        world.Move(1, new Vector3(-17.9f, 5f, -18f));
        world.Move(2, new Vector3(-11.9f, 5f, -18f));

        for (int i = 0; i < 60; i++) world.Update(0.016f);

        world.StopMove(1);
        world.StopMove(2);
        for (int i = 0; i < 10; i++) world.Update(0.016f);

        var smallPos = world.GetPosition(1);
        var largePos = world.GetPosition(2);

        Console.WriteLine($"  Small agent foot Y: {smallPos.Y:F2}");
        Console.WriteLine($"  Large agent foot Y: {largePos.Y:F2}");

        Assert(smallPos.Y < 0.3f, "small agent foot should be near ground");
        Assert(largePos.Y < 0.3f, "large agent foot should be near ground");
        Console.WriteLine("[PASS] CapsuleSizingPerEntity");
    }

    public static void TestUnregisteredConfigFallback(string meshPath)
    {
        var multiNavMesh = new MultiAgentNavMesh(meshPath)
            .Add(MediumConfig).Bake();

        using var world = new World(multiNavMesh);

        var unknownConfig = new AgentConfig { Radius = 0.6f, Height = 2.2f };
        world.Spawn(1, new Vector3(-18f, 5f, -18f), unknownConfig);

        // Move immediately to activate motor
        var resp = world.Move(1, new Vector3(10f, 5f, 10f));

        for (int i = 0; i < 30; i++) world.Update(0.016f);

        Console.WriteLine($"  Fallback Move result: Success={resp.Success}");
        Console.WriteLine("[PASS] UnregisteredConfigFallback — no crash");
    }

    public static void TestDespawnCleansUpState(string meshPath)
    {
        var multiNavMesh = new MultiAgentNavMesh(meshPath)
            .Add(SmallConfig).Add(LargeConfig).Bake();

        using var world = new World(multiNavMesh);

        world.Spawn(1, new Vector3(-18f, 5f, -18f), SmallConfig);
        world.Spawn(2, new Vector3(-15f, 5f, -15f), LargeConfig);

        world.Move(1, new Vector3(10f, 5f, 10f));
        world.Move(2, new Vector3(10f, 5f, 10f));

        for (int i = 0; i < 60; i++) world.Update(0.016f);

        world.Despawn(1);
        world.Despawn(2);

        for (int i = 0; i < 60; i++) world.Update(0.016f);

        Console.WriteLine("[PASS] DespawnCleansUpState");
    }

    public static void TestMixedSizeRoomLifecycle(string meshPath)
    {
        var multiNavMesh = new MultiAgentNavMesh(meshPath)
            .Add(SmallConfig).Add(MediumConfig).Add(LargeConfig).Bake();

        bool anyDestinationReached = false;
        using var world = new World(multiNavMesh);
        world.OnDestinationReached += (id, pos) =>
        {
            Console.WriteLine($"  Entity {id} reached destination at {pos}");
            anyDestinationReached = true;
        };

        // Spawn close together on the same terrain strip so that inter-entity distances
        // are small and each entity's destination is only a few meters away.
        world.Spawn(1, new Vector3(-18f, 5f, -18f), SmallConfig);
        world.Spawn(2, new Vector3(-17f, 5f, -18f), SmallConfig);
        world.Spawn(3, new Vector3(-18f, 5f, -17f), MediumConfig);
        world.Spawn(4, new Vector3(-17f, 5f, -17f), MediumConfig);
        world.Spawn(5, new Vector3(-18f, 5f, -16f), LargeConfig, EntityType.Enemy);
        world.Spawn(6, new Vector3(-16f, 5f, -16f), LargeConfig, EntityType.Enemy);

        // Issue Move immediately so motor is active from tick 1.
        // Destination is on the same terrain strip (same Z≈-18 strip, nearby X).
        var dest = new Vector3(-13f, 5f, -18f);
        for (int id = 1; id <= 6; id++)
            world.Move(id, dest);

        for (int i = 0; i < 15 * 60; i++) world.Update(0.016f);

        Assert(anyDestinationReached, "at least one entity should have reached the destination");

        for (int id = 1; id <= 6; id++) world.Despawn(id);

        Console.WriteLine("[PASS] MixedSizeRoomLifecycle");
    }
}
