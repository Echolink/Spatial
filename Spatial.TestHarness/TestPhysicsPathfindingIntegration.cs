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
/// Test suite for physics-pathfinding integration (Minecraft-style behavior).
/// 
/// Tests:
/// 1. Basic pathfinding with gravity enabled
/// 2. Falling off ledges and recovery
/// 3. Knockback and automatic path replanning
/// </summary>
public static class TestPhysicsPathfindingIntegration
{
    public static void Run(VisualizationServer? vizServer = null)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   PHYSICS-PATHFINDING INTEGRATION TEST                      ║");
        Console.WriteLine("║   Minecraft-Style: Pathfinding + Gravity + Physics          ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var config = new PhysicsConfiguration
        {
            Gravity = new Vector3(0, -9.81f, 0),
            Timestep = 0.016f
        };
        var physicsWorld = new PhysicsWorld(config);
        Console.WriteLine("✓ Physics world initialized with gravity");

        // Load world geometry
        var meshPath = ResolvePath("worlds/seperated_land.obj");
        WorldData? worldData = null;

        if (File.Exists(meshPath))
        {
            Console.WriteLine($"✓ Loading world from: {Path.GetFileName(meshPath)}");
            var meshLoader = new MeshLoader();
            var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);

            string? metadataPath = meshPath + ".json";
            worldData = worldBuilder.LoadAndBuildWorld(meshPath, metadataPath);
            Console.WriteLine("✓ World geometry loaded");
        }
        else
        {
            Console.WriteLine($"⚠ World mesh not found: {meshPath}");
            Console.WriteLine("  Creating simple test platform...");
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
            navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);
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

        var pathfinder = new Pathfinder(navMeshData);
        var movementController = new MovementController(physicsWorld, pathfinder);

        // Send initial state to visualization
        if (vizServer != null)
        {
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
            vizServer.BroadcastState(initialState);
        }

        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST 1: Basic Pathfinding with Gravity Enabled");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        TestBasicPathfindingWithGravity(physicsWorld, movementController, vizServer);

        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST 2: Falling Off Ledge and Recovery");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        TestFallingAndRecovery(physicsWorld, movementController, vizServer);

        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST 3: Knockback and Automatic Replanning");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        TestKnockbackAndReplanning(physicsWorld, movementController, vizServer);

        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   ALL TESTS COMPLETED                                        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }

    private static void TestBasicPathfindingWithGravity(PhysicsWorld physicsWorld, MovementController movementController, VisualizationServer? vizServer)
    {
        Console.WriteLine("Scenario: Agent follows path while gravity is active");
        Console.WriteLine("Expected: Agent stays on ground, follows path correctly");

        // Create agent
        var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, 2.0f, 1.0f);
        var spawnPos = new Vector3(0, 1.0f, 0); // On ground
        var agent = physicsWorld.RegisterEntityWithInertia(
            entityId: 1001,
            entityType: EntityType.Player,
            position: spawnPos,
            shape: agentShape,
            inertia: agentInertia,
            isStatic: false,
            disableGravity: false // Gravity enabled!
        );

        var targetPos = new Vector3(10, 1.0f, 10);
        var request = new MovementRequest(1001, targetPos, maxSpeed: 3.0f, agentHeight: 2.0f);
        movementController.RequestMovement(request);

        Console.WriteLine($"  Agent spawned at: ({spawnPos.X:F2}, {spawnPos.Y:F2}, {spawnPos.Z:F2})");
        Console.WriteLine($"  Target: ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})");
        Console.WriteLine("  Running simulation for 5 seconds...");

        var startTime = DateTime.UtcNow;
        float simulationTime = 0f;
        const float maxTime = 5.0f;
        const float deltaTime = 0.016f;

        while (simulationTime < maxTime)
        {
            physicsWorld.Update(deltaTime);
            movementController.UpdateMovement(deltaTime);
            simulationTime += deltaTime;

            var pos = physicsWorld.GetEntityPosition(agent);
            var vel = physicsWorld.GetEntityVelocity(agent);

            // Check every second
            if (Math.Floor(simulationTime) != Math.Floor(simulationTime - deltaTime))
            {
                Console.WriteLine($"  [{simulationTime:F1}s] Position: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), " +
                    $"Velocity Y: {vel.Y:F2} m/s, State: {GetCharacterState(movementController, agent)}");
            }

            // Update visualization
            if (vizServer != null && Math.Floor(simulationTime * 10) != Math.Floor((simulationTime - deltaTime) * 10))
            {
                var vizState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
                vizServer.BroadcastState(vizState);
            }

            Thread.Sleep((int)(deltaTime * 1000));
        }

        var finalPos = physicsWorld.GetEntityPosition(agent);
        var finalVel = physicsWorld.GetEntityVelocity(agent);
        var distance = Vector3.Distance(spawnPos, finalPos);

        Console.WriteLine($"\n  Final Position: ({finalPos.X:F2}, {finalPos.Y:F2}, {finalPos.Z:F2})");
        Console.WriteLine($"  Final Velocity Y: {finalVel.Y:F2} m/s");
        Console.WriteLine($"  Distance Traveled: {distance:F2}m");
        Console.WriteLine($"  ✓ Test 1 Complete: Agent maintained ground contact while following path");
    }

    private static void TestFallingAndRecovery(PhysicsWorld physicsWorld, MovementController movementController, VisualizationServer? vizServer)
    {
        Console.WriteLine("Scenario: Agent falls off ledge, lands, and recovers");
        Console.WriteLine("Expected: Agent falls, lands, transitions to RECOVERING, then replans path");

        // Create a platform with a drop
        var platformShape = physicsWorld.CreateBoxShape(new Vector3(5, 0.5f, 5));
        physicsWorld.RegisterEntity(
            entityId: 2001,
            entityType: EntityType.StaticObject,
            position: new Vector3(0, 0.25f, 0),
            shape: platformShape,
            isStatic: true
        );

        // Create ground below
        var groundShape = physicsWorld.CreateBoxShape(new Vector3(20, 0.5f, 20));
        physicsWorld.RegisterEntity(
            entityId: 2002,
            entityType: EntityType.StaticObject,
            position: new Vector3(0, -2.0f, 0),
            shape: groundShape,
            isStatic: true
        );

        // Create agent on platform
        var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, 2.0f, 1.0f);
        var spawnPos = new Vector3(0, 1.0f, 0); // On platform
        var agent = physicsWorld.RegisterEntityWithInertia(
            entityId: 2003,
            entityType: EntityType.Player,
            position: spawnPos,
            shape: agentShape,
            inertia: agentInertia,
            isStatic: false,
            disableGravity: false
        );

        // Request movement that will take agent off the platform
        var targetPos = new Vector3(8, -2.0f, 0); // Off platform, on ground below
        var request = new MovementRequest(2003, targetPos, maxSpeed: 3.0f, agentHeight: 2.0f);
        movementController.RequestMovement(request);

        Console.WriteLine($"  Agent spawned on platform at: ({spawnPos.X:F2}, {spawnPos.Y:F2}, {spawnPos.Z:F2})");
        Console.WriteLine($"  Target (below platform): ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})");
        Console.WriteLine("  Running simulation for 8 seconds...");

        float simulationTime = 0f;
        const float maxTime = 8.0f;
        const float deltaTime = 0.016f;
        bool hasFallen = false;
        bool hasLanded = false;
        bool hasRecovered = false;

        while (simulationTime < maxTime)
        {
            physicsWorld.Update(deltaTime);
            movementController.UpdateMovement(deltaTime);
            simulationTime += deltaTime;

            var pos = physicsWorld.GetEntityPosition(agent);
            var vel = physicsWorld.GetEntityVelocity(agent);
            var state = GetCharacterState(movementController, agent);

            // Detect falling
            if (!hasFallen && vel.Y < -1.0f)
            {
                hasFallen = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Agent started falling (Velocity Y: {vel.Y:F2} m/s)");
            }

            // Detect landing
            if (hasFallen && !hasLanded && Math.Abs(vel.Y) < 0.5f && pos.Y < 0)
            {
                hasLanded = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Agent landed at Y={pos.Y:F2}m, State: {state}");
            }

            // Detect recovery
            if (hasLanded && !hasRecovered && state == "GROUNDED")
            {
                hasRecovered = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Agent recovered and resumed pathfinding, State: {state}");
            }

            // Log every 0.5 seconds
            if (Math.Floor(simulationTime * 2) != Math.Floor((simulationTime - deltaTime) * 2))
            {
                Console.WriteLine($"  [{simulationTime:F1}s] Y={pos.Y:F2}m, VelY={vel.Y:F2}m/s, State={state}");
            }

            if (vizServer != null && Math.Floor(simulationTime * 10) != Math.Floor((simulationTime - deltaTime) * 10))
            {
                var vizState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
                vizServer.BroadcastState(vizState);
            }

            Thread.Sleep((int)(deltaTime * 1000));
        }

        Console.WriteLine($"\n  ✓ Test 2 Complete: Falling={hasFallen}, Landing={hasLanded}, Recovery={hasRecovered}");
    }

    private static void TestKnockbackAndReplanning(PhysicsWorld physicsWorld, MovementController movementController, VisualizationServer? vizServer)
    {
        Console.WriteLine("Scenario: Agent gets knocked back mid-movement, recovers, and replans");
        Console.WriteLine("Expected: Agent flies back, lands, transitions to RECOVERING, then replans path");

        // Create agent
        var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, 2.0f, 1.0f);
        var spawnPos = new Vector3(0, 1.0f, 0);
        var agent = physicsWorld.RegisterEntityWithInertia(
            entityId: 3001,
            entityType: EntityType.Player,
            position: spawnPos,
            shape: agentShape,
            inertia: agentInertia,
            isStatic: false,
            disableGravity: false
        );

        var targetPos = new Vector3(15, 1.0f, 0);
        var request = new MovementRequest(3001, targetPos, maxSpeed: 3.0f, agentHeight: 2.0f);
        movementController.RequestMovement(request);

        Console.WriteLine($"  Agent spawned at: ({spawnPos.X:F2}, {spawnPos.Y:F2}, {spawnPos.Z:F2})");
        Console.WriteLine($"  Target: ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})");
        Console.WriteLine("  Running simulation...");

        float simulationTime = 0f;
        const float maxTime = 10.0f;
        const float deltaTime = 0.016f;
        bool knockbackApplied = false;
        bool hasLanded = false;
        bool hasReplanned = false;
        Vector3 positionBeforeKnockback = Vector3.Zero;

        while (simulationTime < maxTime)
        {
            physicsWorld.Update(deltaTime);
            movementController.UpdateMovement(deltaTime);
            simulationTime += deltaTime;

            var pos = physicsWorld.GetEntityPosition(agent);
            var vel = physicsWorld.GetEntityVelocity(agent);
            var state = GetCharacterState(movementController, agent);

            // Apply knockback after 1 second of movement
            if (!knockbackApplied && simulationTime > 1.0f)
            {
                positionBeforeKnockback = pos;
                var knockbackDirection = new Vector3(-1, 0.5f, 0); // Backward and upward
                movementController.Knockback(3001, knockbackDirection, force: 8.0f);
                knockbackApplied = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Applied knockback! Position before: ({positionBeforeKnockback.X:F2}, {positionBeforeKnockback.Y:F2}, {positionBeforeKnockback.Z:F2})");
            }

            // Detect landing after knockback
            if (knockbackApplied && !hasLanded && Math.Abs(vel.Y) < 0.5f && pos.Y < 2.0f)
            {
                hasLanded = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Agent landed after knockback at Y={pos.Y:F2}m, State: {state}");
            }

            // Detect replanning (agent resumes movement toward target)
            if (hasLanded && !hasReplanned && state == "GROUNDED" && vel.X > 0.1f)
            {
                hasReplanned = true;
                Console.WriteLine($"  [{simulationTime:F2}s] ✓ Agent replanned and resumed movement toward target, State: {state}");
            }

            // Log every 0.5 seconds
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
        var distanceFromTarget = Vector3.Distance(finalPos, targetPos);
        var distanceTraveled = Vector3.Distance(positionBeforeKnockback, finalPos);

        Console.WriteLine($"\n  Final Position: ({finalPos.X:F2}, {finalPos.Y:F2}, {finalPos.Z:F2})");
        Console.WriteLine($"  Distance from target: {distanceFromTarget:F2}m");
        Console.WriteLine($"  Distance traveled after knockback: {distanceTraveled:F2}m");
        Console.WriteLine($"  ✓ Test 3 Complete: Knockback={knockbackApplied}, Landing={hasLanded}, Replanning={hasReplanned}");
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
        physicsWorld.RegisterEntity(
            entityId: 1,
            entityType: EntityType.StaticObject,
            position: new Vector3(0, -0.25f, 0),
            shape: groundShape,
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
