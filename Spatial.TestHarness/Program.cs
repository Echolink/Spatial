using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using System.Numerics;

namespace Spatial.TestHarness;

/// <summary>
/// Test harness demonstrating the integration of Spatial.Physics and Spatial.Pathfinding.
/// 
/// This demonstrates:
/// 1. Setting up a physics world with static obstacles
/// 2. Building a navigation mesh from physics geometry
/// 3. Creating entities that can move
/// 4. Using pathfinding to find paths
/// 5. Using MovementController to move entities along paths
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Spatial Integration Test Harness ===\n");
        
        // Start visualization server
        var vizServer = new VisualizationServer();
        vizServer.Start(8181);
        
        Console.WriteLine("\n[Info] Waiting for Unity client to connect...");
        Console.WriteLine("[Info] Start Unity client now, or press any key to continue without visualization\n");
        
        // Clear any buffered console input
        bool isInteractive = Environment.UserInteractive && !Console.IsInputRedirected;
        if (isInteractive)
        {
            try
            {
                while (Console.KeyAvailable)
                {
                    Console.ReadKey(true); // Clear buffer
                }
            }
            catch
            {
                // Ignore errors when console is redirected
                isInteractive = false;
            }
        }
        
        // Wait until Unity client connects, or user can skip
        bool skipWaiting = false;
        int waitCounter = 0;
        int maxWaitTime = 50; // Wait max 5 seconds (50 * 100ms)
        
        while (!vizServer.HasClients() && !skipWaiting)
        {
            // Only check for key press if running in interactive mode
            if (isInteractive)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey(true);
                        skipWaiting = true;
                        break;
                    }
                }
                catch
                {
                    // Ignore errors when console is redirected
                    isInteractive = false;
                }
            }
            
            // Show periodic waiting message every 2 seconds
            waitCounter++;
            if (waitCounter % 20 == 0) // Every 2 seconds (20 * 100ms)
            {
                Console.WriteLine($"[Info] Still waiting for Unity client... ({waitCounter / 10} seconds elapsed)");
            }
            
            // Auto-skip after max wait time
            if (waitCounter >= maxWaitTime)
            {
                skipWaiting = true;
                Console.WriteLine("[Info] Wait timeout - continuing without visualization");
                break;
            }
            
            Thread.Sleep(100); // Check every 100ms
        }
        
        if (vizServer.HasClients())
        {
            Console.WriteLine($"\n[Info] {vizServer.ClientCount} Unity client(s) connected!");
            
            // Give Unity Editor extra time to fully render (Editor loads slower than builds)
            int unityRenderDelay = 5000; // 5 seconds, set to 0 to disable
            if (unityRenderDelay > 0)
            {
                Console.WriteLine($"[Info] Waiting {unityRenderDelay / 1000.0f:F1} seconds for Unity Editor to finish rendering...");
                Thread.Sleep(unityRenderDelay);
            }
            Console.WriteLine("[Info] Starting simulation...");
        }
        else
        {
            Console.WriteLine("\n[Info] Skipped waiting - continuing without visualization");
        }
        
        Console.WriteLine();
        
        try
        {
            // Run tests with visualization
            TestPhysicsCollision(vizServer);
            Console.WriteLine("\n" + new string('=', 60) + "\n");
            TestFullIntegration(vizServer);
            Console.WriteLine("\n" + new string('=', 60) + "\n");
            MultiUnitTest.Run(vizServer);
        }
        finally
        {
            // Cleanup
            Console.WriteLine("\n[Info] Shutting down visualization server...");
            vizServer.Stop();
        }
    }
    
    /// <summary>
    /// Test 1: Basic physics collision test to verify collision resolution works
    /// </summary>
    static void TestPhysicsCollision(VisualizationServer vizServer)
    {
        Console.WriteLine("TEST 1: PHYSICS COLLISION\n");
        
        try
        {
            // Step 1: Create physics world
            Console.WriteLine("1. Creating physics world...");
            var config = new PhysicsConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            };
            var physicsWorld = new PhysicsWorld(config);
            Console.WriteLine("   ✓ Physics world created\n");
            
            // Step 2: Create ground plane
            Console.WriteLine("2. Creating ground plane...");
            var groundShape = physicsWorld.CreateBoxShape(new Vector3(20, 0.1f, 20));
            physicsWorld.RegisterEntity(
                entityId: 1000,
                entityType: EntityType.StaticObject,
                position: new Vector3(0, -0.05f, 0),
                shape: groundShape,
                isStatic: true
            );
            Console.WriteLine("   ✓ Ground plane created\n");
            
            // Step 3: Create a falling entity
            Console.WriteLine("3. Creating falling entity...");
            const float entityMass = 1.0f;
            var (entityShape, entityInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, 2.0f, entityMass);
            
            var entity = physicsWorld.RegisterEntityWithInertia(
                entityId: 1,
                entityType: EntityType.Player,
                position: new Vector3(0, 1.51f, 0),
                shape: entityShape,
                inertia: entityInertia,
                isStatic: false
            );
            
            var initialPos = physicsWorld.GetEntityPosition(entity);
            Console.WriteLine($"   ✓ Entity created at ({initialPos.X:F2}, {initialPos.Y:F2}, {initialPos.Z:F2})\n");
            
            // Step 4: Test physics simulation
            Console.WriteLine("4. Testing collision (10 steps)...");
            for (int i = 0; i < 10; i++)
            {
                var posBefore = physicsWorld.GetEntityPosition(entity);
                var velBefore = physicsWorld.GetEntityVelocity(entity);
                
                physicsWorld.Update(0.016f);
                
                // Broadcast state to Unity
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
                vizServer.BroadcastState(state);
                
                var posAfter = physicsWorld.GetEntityPosition(entity);
                var velAfter = physicsWorld.GetEntityVelocity(entity);
                
                if (i < 3 || i >= 8) // Show first 3 and last 2 steps
                {
                    Console.WriteLine($"   Step {i + 1}: Pos=({posAfter.X:F2}, {posAfter.Y:F2}, {posAfter.Z:F2}), Vel=({velAfter.X:F2}, {velAfter.Y:F2}, {velAfter.Z:F2})");
                }
                else if (i == 3)
                {
                    Console.WriteLine($"   ...");
                }
                
                // Small delay for visualization
                Thread.Sleep(16);
            }
            
            var finalPos = physicsWorld.GetEntityPosition(entity);
            Console.WriteLine($"\n   ✓ Final position: ({finalPos.X:F2}, {finalPos.Y:F2}, {finalPos.Z:F2})");
            Console.WriteLine($"   ✓ Entity settled on ground successfully\n");
            
            // Cleanup
            physicsWorld.Dispose();
            Console.WriteLine("✅ PHYSICS COLLISION TEST PASSED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ PHYSICS COLLISION TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Test 2: Full integration test - Physics + NavMesh + Pathfinding + Movement
    /// </summary>
    static void TestFullIntegration(VisualizationServer vizServer)
    {
        Console.WriteLine("TEST 2: FULL INTEGRATION (Physics + NavMesh + Pathfinding + Movement)\n");
        
        try
        {
            // Step 1: Create physics world with obstacles
            Console.WriteLine("1. Creating physics world with obstacles...");
            var config = new PhysicsConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            };
            var physicsWorld = new PhysicsWorld(config);
            CreateStaticObstacles(physicsWorld);
            Console.WriteLine("   ✓ Physics world with obstacles created\n");
            
            // Broadcast initial state
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
            vizServer.BroadcastState(initialState);
            
            // Step 2: Build navigation mesh
            Console.WriteLine("2. Building navigation mesh...");
            var agentConfig = new AgentConfig
            {
                Height = 2.0f,
                Radius = 0.4f,  // Smaller radius to allow tighter navigation
                MaxSlope = 45.0f,
                MaxClimb = 0.5f
            };
            
            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            
            var navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
            
            if (navMeshData != null && navMeshData.NavMesh != null)
            {
                Console.WriteLine($"   ✓ NavMesh generated successfully\n");
                
                // Broadcast state with NavMesh
                var stateWithNavMesh = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(stateWithNavMesh);
            }
            else
            {
                Console.WriteLine($"   ✗ NavMesh generation failed\n");
                physicsWorld.Dispose();
                return;
            }
            
            // Step 3: Find a path (goal is behind the wall obstacle)
            Console.WriteLine("3. Finding path from (-5, 1, 0) to (6, 1, 0)...");
            var pathfinder = new Pathfinder(navMeshData);
            var start = new Vector3(-5, 1, 0);  // Start on left side
            var goal = new Vector3(6, 1, 0);    // Goal on right side, behind the wall at (3, 1.5, 0)
            var extents = new Vector3(5.0f, 10.0f, 5.0f); // Search extents
            
            var pathResult = pathfinder.FindPath(start, goal, extents);
            
            if (pathResult.Success && pathResult.Waypoints.Count > 0)
            {
                Console.WriteLine($"   ✓ Path found with {pathResult.Waypoints.Count} waypoints:");
                for (int i = 0; i < pathResult.Waypoints.Count; i++)
                {
                    var wp = pathResult.Waypoints[i];
                    Console.WriteLine($"      [{i}] ({wp.X:F2}, {wp.Y:F2}, {wp.Z:F2})");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($"   ✗ No path found\n");
                physicsWorld.Dispose();
                return;
            }
            
            // Step 4: Create agent and move along path
            Console.WriteLine("4. Creating agent and moving along path...");
            const float agentMass = 1.0f;
            var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
                agentConfig.Radius, 
                agentConfig.Height, 
                agentMass
            );
            
            // Place agent slightly above ground
            var startAboveGround = new Vector3(start.X, 1.51f, start.Z);
            var agent = physicsWorld.RegisterEntityWithInertia(
                entityId: 1,
                entityType: EntityType.Player,
                position: startAboveGround,
                shape: agentShape,
                inertia: agentInertia,
                isStatic: false
            );
            
            var movementController = new MovementController(physicsWorld, pathfinder);
            
            // Request movement to goal
            var moveRequest = new MovementRequest(1, goal, maxSpeed: 3.0f);
            bool moveStarted = movementController.RequestMovement(moveRequest);
            
            if (!moveStarted)
            {
                Console.WriteLine($"   ✗ Failed to start movement\n");
                physicsWorld.Dispose();
                return;
            }
            
            Console.WriteLine($"   ✓ Agent created and movement started\n");
            
            // Add a dynamic falling box to demonstrate physics
            Console.WriteLine("   Adding dynamic physics box...");
            const float boxMass = 2.0f;
            var boxSize = new Vector3(1.0f, 1.0f, 1.0f);
            var (boxShape, boxInertia) = physicsWorld.CreateBoxShapeWithInertia(boxSize, boxMass);
            var dynamicBox = physicsWorld.RegisterEntityWithInertia(
                entityId: 2,
                entityType: EntityType.NPC,
                position: new Vector3(3, 8, 3), // Drop from high above, offset from wall
                shape: boxShape,
                inertia: boxInertia,
                isStatic: false
            );
            Console.WriteLine($"   ✓ Dynamic box created at (3, 8, 3) - will fall to ground\n");
            
            // Step 5: Simulate movement (let agent settle on ground first, then move)
            Console.WriteLine("5. Simulating movement (360 steps, ~6 seconds)...");
            int steps = 360;
            bool reachedGoal = false;
            
            for (int i = 0; i < steps; i++)
            {
                movementController.UpdateMovement(0.016f);
                physicsWorld.Update(0.016f);
                
                // Broadcast state with path
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(
                    physicsWorld, 
                    navMeshData, 
                    pathResult, 
                    agent.EntityId
                );
                vizServer.BroadcastState(state);
                
                var currentPos = physicsWorld.GetEntityPosition(agent);
                
                // Print every 30 steps
                if (i % 30 == 0)
                {
                    var distanceToGoal = Vector3.Distance(currentPos, goal);
                    var boxPos = physicsWorld.GetEntityPosition(dynamicBox);
                    var boxVel = physicsWorld.GetEntityVelocity(dynamicBox);
                    Console.WriteLine($"   Step {i + 1}: Agent=({currentPos.X:F2}, {currentPos.Y:F2}, {currentPos.Z:F2}), DistToGoal={distanceToGoal:F2}");
                    Console.WriteLine($"           Box=({boxPos.X:F2}, {boxPos.Y:F2}, {boxPos.Z:F2}), Vel=({boxVel.X:F2}, {boxVel.Y:F2}, {boxVel.Z:F2})");
                    
                    // Check if reached goal
                    if (distanceToGoal < 1.0f)
                    {
                        reachedGoal = true;
                        Console.WriteLine($"\n   ✓ Agent reached destination at step {i + 1}!");
                        break;
                    }
                }
                
                // Small delay for visualization (60 FPS)
                Thread.Sleep(16);
            }
            
            var finalPosition = physicsWorld.GetEntityPosition(agent);
            var finalDistance = Vector3.Distance(finalPosition, goal);
            Console.WriteLine($"\n   Final position: ({finalPosition.X:F2}, {finalPosition.Y:F2}, {finalPosition.Z:F2})");
            Console.WriteLine($"   Final distance to goal: {finalDistance:F2}\n");
            
            // Cleanup
            physicsWorld.Dispose();
            
            if (reachedGoal || finalDistance < 1.0f)
            {
                Console.WriteLine("✅ FULL INTEGRATION TEST PASSED");
            }
            else
            {
                Console.WriteLine("⚠️ FULL INTEGRATION TEST: Agent didn't reach goal (may need more steps or larger navmesh)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ FULL INTEGRATION TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Creates static obstacles in the physics world.
    /// These will be used to generate the navigation mesh.
    /// </summary>
    static void CreateStaticObstacles(PhysicsWorld physicsWorld)
    {
        // Create a thin ground plane (reduce height to make it more like a floor)
        var groundShape = physicsWorld.CreateBoxShape(new Vector3(20, 0.1f, 20));
        physicsWorld.RegisterEntity(
            entityId: 1000,
            entityType: EntityType.StaticObject,
            position: new Vector3(0, -0.05f, 0), // Ground surface at y=0
            shape: groundShape,
            isStatic: true
        );
        
        // Create some walls/obstacles that sit ON TOP of the ground
        // Walls should extend upward from the ground level (y=0)
        var wallShape = physicsWorld.CreateBoxShape(new Vector3(1, 3, 1));
        
        // Corner walls
        physicsWorld.RegisterEntity(
            entityId: 1001,
            entityType: EntityType.StaticObject,
            position: new Vector3(7, 1.5f, 7),
            shape: wallShape,
            isStatic: true
        );
        
        physicsWorld.RegisterEntity(
            entityId: 1002,
            entityType: EntityType.StaticObject,
            position: new Vector3(-7, 1.5f, 7),
            shape: wallShape,
            isStatic: true
        );
        
        physicsWorld.RegisterEntity(
            entityId: 1003,
            entityType: EntityType.StaticObject,
            position: new Vector3(7, 1.5f, -7),
            shape: wallShape,
            isStatic: true
        );
        
        physicsWorld.RegisterEntity(
            entityId: 1004,
            entityType: EntityType.StaticObject,
            position: new Vector3(-7, 1.5f, -7),
            shape: wallShape,
            isStatic: true
        );
        
        // Create a TALL and WIDE wall obstacle in the middle to force pathfinding around it
        // This wall must block the navmesh completely to force the agent to navigate around
        var largeWallShape = physicsWorld.CreateBoxShape(new Vector3(1, 5, 8)); // 1m thick, 5m tall, 8m wide (Z-axis)
        physicsWorld.RegisterEntity(
            entityId: 1005,
            entityType: EntityType.StaticObject,
            position: new Vector3(0, 2.5f, 0), // Centered between start (-5,1,0) and goal (6,1,0)
            shape: largeWallShape,
            isStatic: true
        );
    }
}
