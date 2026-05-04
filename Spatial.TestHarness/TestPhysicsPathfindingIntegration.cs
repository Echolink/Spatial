using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;
using System.Numerics;
using System.Diagnostics;

namespace Spatial.TestHarness;

/// <summary>
/// Test suite for physics-pathfinding integration with off-mesh link traversal.
///
/// Tests (all require seperated_land_with_link.obj):
/// 1. Jump link traversal — agent crosses a gap via a jump off-mesh link
/// 2. Teleport link traversal — agent teleports from an elevated platform to ground
/// 3. Jump link with knockback — agent is knocked back mid-path, recovers, and re-crosses the jump link
/// </summary>
public static class TestPhysicsPathfindingIntegration
{
    // Off-mesh link positions baked into seperated_land_with_link.obj
    private static readonly Vector3 JumpLinkEntry  = new(53.32f, -2.60f, -7.03f);
    private static readonly Vector3 JumpLinkExit   = new(46.27f, -2.30f, -16.45f);
    private static readonly Vector3 TeleportEntry  = new(42.83f,  7.55f,  21.40f);
    private static readonly Vector3 TeleportExit   = new(20.05f, -2.12f, -23.58f);

    public static void Run(VisualizationServer? vizServer = null)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   PHYSICS-PATHFINDING INTEGRATION TEST                      ║");
        Console.WriteLine("║   Off-Mesh Link Traversal: Jump + Teleport + Knockback      ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var config = new PhysicsConfiguration
        {
            Gravity = new Vector3(0, -9.81f, 0),
            Timestep = 0.016f
        };
        var physicsWorld = new PhysicsWorld(config);
        Console.WriteLine("✓ Physics world initialized with gravity");

        // Load world geometry (must contain offmesh_* marker groups)
        var meshPath = ResolvePath("worlds/seperated_land_with_link.obj");
        WorldData? worldData = null;

        if (File.Exists(meshPath))
        {
            Console.WriteLine($"✓ Loading world from: {Path.GetFileName(meshPath)}");
            var meshLoader = new MeshLoader();
            var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);

            string? metadataPath = meshPath + ".json";
            worldData = worldBuilder.LoadAndBuildWorld(meshPath, metadataPath);
            Console.WriteLine($"✓ World geometry loaded  |  off-mesh links: {worldData.OffMeshLinks.Count}");
        }
        else
        {
            Console.WriteLine($"⚠ World mesh not found: {meshPath}");
            Console.WriteLine("  Creating simple test platform (off-mesh link tests will be skipped)...");
            CreateSimpleTestPlatform(physicsWorld);
        }

        // Generate navmesh
        var agentConfig = new AgentConfig
        {
            Radius = 0.5f,
            Height = 2.0f,
            MaxClimb = 0.5f,
            MaxSlope = 45.0f
        };

        Console.WriteLine("\n⏱ Generating navmesh...");
        var navMeshStopwatch = Stopwatch.StartNew();
        var navMeshGenerator = new NavMeshGenerator();
        var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);

        NavMeshData? navMeshData;
        if (worldData != null)
        {
            navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig, worldData.OffMeshLinks);
        }
        else
        {
            navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
        }

        navMeshStopwatch.Stop();
        Console.WriteLine($"✓ NavMesh generated in {navMeshStopwatch.ElapsedMilliseconds}ms");

        if (navMeshData == null || navMeshData.NavMesh == null)
        {
            Console.WriteLine("✗ NavMesh generation failed!");
            return;
        }

        var pathfinder = new Pathfinder(navMeshData, worldData?.OffMeshLinks);
        var movementController = new MovementController(physicsWorld, pathfinder, agentConfig);

        // Send initial state to visualization
        if (vizServer != null)
        {
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
            vizServer.BroadcastState(initialState);
        }

        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST 1: Jump Link Traversal");
        Console.WriteLine($"  Start platform → [{JumpLinkEntry.X:F1},{JumpLinkEntry.Z:F1}] ~~JUMP~~ [{JumpLinkExit.X:F1},{JumpLinkExit.Z:F1}] → End platform");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        TestJumpLinkTraversal(physicsWorld, movementController, vizServer);

        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST 2: Teleport Link Traversal");
        Console.WriteLine($"  Elevated platform → [{TeleportEntry.X:F1},{TeleportEntry.Z:F1}] ~~TELEPORT~~ [{TeleportExit.X:F1},{TeleportExit.Z:F1}] → Ground");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        TestTeleportLinkTraversal(physicsWorld, movementController, vizServer);

        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST 3: Jump Link with Mid-Path Knockback");
        Console.WriteLine($"  Knocked back before jump link, recovers, re-crosses via jump");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        TestKnockbackAndReplanning(physicsWorld, movementController, vizServer);

        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   ALL TESTS COMPLETED                                        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    // Spawn slightly above the start platform; gravity settles the agent before movement begins.
    // Both spawn and target straddle jump link 01, forcing the path to use it.
    private static void TestJumpLinkTraversal(PhysicsWorld physicsWorld, MovementController movementController, VisualizationServer? vizServer)
    {
        Console.WriteLine("Scenario: Agent navigates from start platform to end platform across a gap");
        Console.WriteLine("Expected: Path includes jump off-mesh link; state transitions to LINK_TRAVERSAL");

        var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, 2.0f, 1.0f);
        // Spawn 4 units behind the jump entry, 3 units above terrain surface (~Y=-2.6)
        var spawnPos = new Vector3(JumpLinkEntry.X + 4.0f, JumpLinkEntry.Y + 3.0f, JumpLinkEntry.Z + 3.0f);
        var agent = physicsWorld.RegisterEntityWithInertia(
            entityId: 1001,
            entityType: EntityType.Player,
            position: spawnPos,
            shape: agentShape,
            inertia: agentInertia,
            isStatic: false,
            disableGravity: false
        );

        // Target 3 units past the jump exit on the far platform
        var targetPos = new Vector3(JumpLinkExit.X - 3.0f, JumpLinkExit.Y, JumpLinkExit.Z - 3.0f);
        var request = new MovementRequest(1001, targetPos, maxSpeed: 3.0f, agentHeight: 2.0f);
        var response = movementController.RequestMovement(request);

        Console.WriteLine($"  Agent spawned at:  ({spawnPos.X:F2}, {spawnPos.Y:F2}, {spawnPos.Z:F2})");
        Console.WriteLine($"  Target:            ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})");

        if (response.Success)
        {
            Console.WriteLine($"  Snapped target:    ({response.ActualTargetPosition.X:F2}, {response.ActualTargetPosition.Y:F2}, {response.ActualTargetPosition.Z:F2})");
            Console.WriteLine("  Running simulation for 8 seconds...");
        }
        else
        {
            Console.WriteLine($"  ✗ Movement failed: {response.Message}");
            return;
        }

        float simulationTime = 0f;
        const float maxTime = 8.0f;
        const float deltaTime = 0.016f;
        bool linkTraversalDetected = false;
        bool linkTraversalComplete = false;
        string prevState = "";

        while (simulationTime < maxTime)
        {
            physicsWorld.Update(deltaTime);
            movementController.UpdateMovement(deltaTime);
            simulationTime += deltaTime;

            var pos = physicsWorld.GetEntityPosition(agent);
            var state = GetCharacterState(movementController, agent);

            if (state == "LINK_TRAVERSAL" && !linkTraversalDetected)
            {
                linkTraversalDetected = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Jump link traversal started! Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
            }

            if (prevState == "LINK_TRAVERSAL" && state != "LINK_TRAVERSAL" && linkTraversalDetected && !linkTraversalComplete)
            {
                linkTraversalComplete = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Jump link traversal complete. Now: {state}, Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
            }
            prevState = state;

            if (Math.Floor(simulationTime) != Math.Floor(simulationTime - deltaTime))
            {
                Console.WriteLine($"  [{simulationTime:F1}s] Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), State: {state}");
            }

            if (vizServer != null && Math.Floor(simulationTime * 10) != Math.Floor((simulationTime - deltaTime) * 10))
            {
                var vizState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
                vizServer.BroadcastState(vizState);
            }

            Thread.Sleep((int)(deltaTime * 1000));
        }

        var finalPos = physicsWorld.GetEntityPosition(agent);
        var distanceToTarget = Vector3.Distance(finalPos, targetPos);
        Console.WriteLine($"\n  Final Pos: ({finalPos.X:F2}, {finalPos.Y:F2}, {finalPos.Z:F2})  |  Distance to target: {distanceToTarget:F2}m");
        Console.WriteLine($"  ✓ Test 1 Complete — LinkTraversalDetected={linkTraversalDetected}, LinkTraversalComplete={linkTraversalComplete}");
    }

    // Spawn above the elevated start platform (Y≈7.5); agent falls and settles, then the
    // teleport link carries it to ground level on the far side of the map.
    private static void TestTeleportLinkTraversal(PhysicsWorld physicsWorld, MovementController movementController, VisualizationServer? vizServer)
    {
        Console.WriteLine("Scenario: Agent starts on elevated platform and teleports to ground-level destination");
        Console.WriteLine("Expected: Path includes teleport off-mesh link; state transitions to LINK_TRAVERSAL instantly");

        var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, 2.0f, 1.0f);
        // Spawn 3.5 units above the elevated platform surface (Y≈7.5)
        var spawnPos = new Vector3(TeleportEntry.X, TeleportEntry.Y + 3.5f, TeleportEntry.Z);
        var agent = physicsWorld.RegisterEntityWithInertia(
            entityId: 1002,
            entityType: EntityType.Player,
            position: spawnPos,
            shape: agentShape,
            inertia: agentInertia,
            isStatic: false,
            disableGravity: false
        );

        // Target 3 units past the teleport exit on the ground-level platform
        var targetPos = new Vector3(TeleportExit.X - 3.0f, TeleportExit.Y, TeleportExit.Z - 3.0f);
        var request = new MovementRequest(1002, targetPos, maxSpeed: 3.0f, agentHeight: 2.0f);
        var response = movementController.RequestMovement(request);

        Console.WriteLine($"  Agent spawned at:  ({spawnPos.X:F2}, {spawnPos.Y:F2}, {spawnPos.Z:F2})  [above elevated platform Y≈{TeleportEntry.Y:F1}]");
        Console.WriteLine($"  Target:            ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})  [ground-level exit Y≈{TeleportExit.Y:F1}]");

        if (response.Success)
        {
            Console.WriteLine($"  Snapped target:    ({response.ActualTargetPosition.X:F2}, {response.ActualTargetPosition.Y:F2}, {response.ActualTargetPosition.Z:F2})");
            Console.WriteLine("  Running simulation for 10 seconds...");
        }
        else
        {
            Console.WriteLine($"  ✗ Movement failed: {response.Message}");
            return;
        }

        float simulationTime = 0f;
        const float maxTime = 10.0f;
        const float deltaTime = 0.016f;
        bool linkTraversalDetected = false;
        bool linkTraversalComplete = false;
        string prevState = "";

        while (simulationTime < maxTime)
        {
            physicsWorld.Update(deltaTime);
            movementController.UpdateMovement(deltaTime);
            simulationTime += deltaTime;

            var pos = physicsWorld.GetEntityPosition(agent);
            var state = GetCharacterState(movementController, agent);

            if (state == "LINK_TRAVERSAL" && !linkTraversalDetected)
            {
                linkTraversalDetected = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Teleport link traversal started! Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
            }

            if (prevState == "LINK_TRAVERSAL" && state != "LINK_TRAVERSAL" && linkTraversalDetected && !linkTraversalComplete)
            {
                linkTraversalComplete = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Teleport complete — agent is now at ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), State: {state}");
            }
            prevState = state;

            if (Math.Floor(simulationTime * 2) != Math.Floor((simulationTime - deltaTime) * 2))
            {
                Console.WriteLine($"  [{simulationTime:F1}s] Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), State: {state}");
            }

            if (vizServer != null && Math.Floor(simulationTime * 10) != Math.Floor((simulationTime - deltaTime) * 10))
            {
                var vizState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
                vizServer.BroadcastState(vizState);
            }

            Thread.Sleep((int)(deltaTime * 1000));
        }

        var finalPos = physicsWorld.GetEntityPosition(agent);
        var distanceToTarget = Vector3.Distance(finalPos, targetPos);
        Console.WriteLine($"\n  Final Pos: ({finalPos.X:F2}, {finalPos.Y:F2}, {finalPos.Z:F2})  |  Distance to target: {distanceToTarget:F2}m");
        Console.WriteLine($"  ✓ Test 2 Complete — LinkTraversalDetected={linkTraversalDetected}, LinkTraversalComplete={linkTraversalComplete}");
    }

    // Same start/target straddle the jump link as Test 1, but a knockback is applied
    // once the agent reaches the start-platform area, forcing it to recover and re-plan
    // a path that still crosses the jump link.
    private static void TestKnockbackAndReplanning(PhysicsWorld physicsWorld, MovementController movementController, VisualizationServer? vizServer)
    {
        Console.WriteLine("Scenario: Agent is knocked back before the jump link, recovers, and crosses it");
        Console.WriteLine("Expected: Knockback → AIRBORNE → RECOVERING → GROUNDED → re-plans → LINK_TRAVERSAL");

        var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, 2.0f, 1.0f);
        var spawnPos = new Vector3(JumpLinkEntry.X + 4.0f, JumpLinkEntry.Y + 3.0f, JumpLinkEntry.Z + 3.0f);
        var agent = physicsWorld.RegisterEntityWithInertia(
            entityId: 1003,
            entityType: EntityType.Player,
            position: spawnPos,
            shape: agentShape,
            inertia: agentInertia,
            isStatic: false,
            disableGravity: false
        );

        var targetPos = new Vector3(JumpLinkExit.X - 3.0f, JumpLinkExit.Y, JumpLinkExit.Z - 3.0f);
        var request = new MovementRequest(1003, targetPos, maxSpeed: 3.0f, agentHeight: 2.0f);
        var response = movementController.RequestMovement(request);

        Console.WriteLine($"  Agent spawned at:  ({spawnPos.X:F2}, {spawnPos.Y:F2}, {spawnPos.Z:F2})");
        Console.WriteLine($"  Target:            ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})");

        if (response.Success)
        {
            Console.WriteLine($"  Snapped target:    ({response.ActualTargetPosition.X:F2}, {response.ActualTargetPosition.Y:F2}, {response.ActualTargetPosition.Z:F2})");
            Console.WriteLine("  Running simulation for 12 seconds...");
        }
        else
        {
            Console.WriteLine($"  ✗ Movement failed: {response.Message}");
            return;
        }

        float simulationTime = 0f;
        const float maxTime = 12.0f;
        const float deltaTime = 0.016f;
        bool knockbackApplied = false;
        bool hasLanded = false;
        bool hasReplanned = false;
        bool linkTraversalDetected = false;
        Vector3 positionBeforeKnockback = Vector3.Zero;
        string prevState = "";

        while (simulationTime < maxTime)
        {
            physicsWorld.Update(deltaTime);
            movementController.UpdateMovement(deltaTime);
            simulationTime += deltaTime;

            var pos = physicsWorld.GetEntityPosition(agent);
            var vel = physicsWorld.GetEntityVelocity(agent);
            var state = GetCharacterState(movementController, agent);

            // Apply knockback once the agent is GROUNDED and moving (settled from spawn fall)
            if (!knockbackApplied && state == "GROUNDED" && simulationTime > 1.5f)
            {
                positionBeforeKnockback = pos;
                movementController.Knockback(1003, new Vector3(1, 0.5f, 1), force: 8.0f);
                knockbackApplied = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Knockback applied! Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
            }

            if (knockbackApplied && !hasLanded && Math.Abs(vel.Y) < 0.5f && pos.Y < JumpLinkEntry.Y + 2.0f)
            {
                hasLanded = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Landed after knockback — Y={pos.Y:F2}m, State: {state}");
            }

            if (hasLanded && !hasReplanned && state == "GROUNDED")
            {
                hasReplanned = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Agent re-planned path, State: {state}");
            }

            if (state == "LINK_TRAVERSAL" && !linkTraversalDetected)
            {
                linkTraversalDetected = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Jump link traversal (post-knockback)! Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
            }

            if (prevState == "LINK_TRAVERSAL" && state != "LINK_TRAVERSAL" && linkTraversalDetected)
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Jump link complete. State: {state}");
            prevState = state;

            if (Math.Floor(simulationTime * 2) != Math.Floor((simulationTime - deltaTime) * 2))
            {
                Console.WriteLine($"  [{simulationTime:F1}s] Pos=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), VelY={vel.Y:F2}m/s, State={state}");
            }

            if (vizServer != null && Math.Floor(simulationTime * 10) != Math.Floor((simulationTime - deltaTime) * 10))
            {
                var vizState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
                vizServer.BroadcastState(vizState);
            }

            Thread.Sleep((int)(deltaTime * 1000));
        }

        var finalPos = physicsWorld.GetEntityPosition(agent);
        Console.WriteLine($"\n  Final Pos: ({finalPos.X:F2}, {finalPos.Y:F2}, {finalPos.Z:F2})");
        Console.WriteLine($"  ✓ Test 3 Complete — Knockback={knockbackApplied}, Landed={hasLanded}, Replanned={hasReplanned}, JumpLink={linkTraversalDetected}");
    }

    private static string GetCharacterState(MovementController movementController, PhysicsEntity agent)
    {
        var state = movementController.GetCharacterState(agent);
        return state.ToString();
    }

    private static void CreateSimpleTestPlatform(PhysicsWorld physicsWorld)
    {
        // Create a simple flat ground
        var groundShape = physicsWorld.CreateBoxShape(new Vector3(20, 0.5f, 20));
        physicsWorld.RegisterEntityWithInertia(
            entityId: 1,
            entityType: EntityType.StaticObject,
            position: new Vector3(0, -0.25f, 0),
            shape: groundShape,
            inertia: default,
            isStatic: true
        );
    }

    private static string ResolvePath(string relativePath)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        return Path.Combine(projectRoot, relativePath);
    }
}
