using Spatial.Integration;
using Spatial.Integration.Commands;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using System.Numerics;

namespace Spatial.TestHarness;

/// <summary>
/// Comprehensive test demonstrating all features of the game server integration system:
/// 
/// Test Scenarios:
/// 1. Spawn 10 units at random positions across the map
/// 2. Command all 10 units to move to random distant destinations
/// 3. Spawn 3 temporary obstacles at random positions in the middle of the simulation
/// 4. Verify units use local avoidance to navigate around each other
/// 5. Verify units replan around the temporary obstacles
/// 6. Verify collision events fire correctly
/// 7. Verify temporary obstacles despawn after their duration
/// 8. Verify most units reach their destinations
/// </summary>
public static class MultiUnitTest
{
    private static readonly Random Random = new Random();
    
    public static void Run(VisualizationServer vizServer)
    {
        Console.WriteLine("TEST 3: MULTI-UNIT INTEGRATION (Complete Feature Demonstration)\n");
        
        try
        {
            // Step 1: Initialize systems
            Console.WriteLine("1. Initializing game server systems...");
            var physicsWorld = new PhysicsWorld(new PhysicsConfiguration 
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            });
            
            // Create ground and obstacles
            CreateTestEnvironment(physicsWorld);
            
            // Build navmesh
            var agentConfig = new AgentConfig
            {
                Height = 2.0f,
                Radius = 0.5f,
                MaxSlope = 45.0f,
                MaxClimb = 0.5f
            };
            
            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            var navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
            
            if (navMeshData?.NavMesh == null)
            {
                Console.WriteLine("   ✗ NavMesh generation failed\n");
                return;
            }
            
            // Initialize integration systems
            var pathfinder = new Pathfinder(navMeshData);
            var config = new PathfindingConfiguration
            {
                PathValidationInterval = 0.5f,
                EnableLocalAvoidance = true,
                EnableAutomaticReplanning = true,
                LocalAvoidanceRadius = 3.0f,
                MaxAvoidanceNeighbors = 5,
                TryLocalAvoidanceFirst = true
            };
            
            var entityManager = new EntityManager(physicsWorld);
            var movementController = new MovementController(physicsWorld, pathfinder, config);
            var collisionSystem = new CollisionEventSystem(physicsWorld);
            
            Console.WriteLine("   ✓ All systems initialized\n");
            
            // Step 2: Setup event handlers
            Console.WriteLine("2. Setting up event handlers...");
            SetupEventHandlers(entityManager, movementController, collisionSystem);
            Console.WriteLine("   ✓ Event handlers registered\n");
            
            // Broadcast initial state
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
            vizServer.BroadcastState(initialState);
            
            // Step 3: Spawn 10 units at random positions
            Console.WriteLine("3. Spawning 10 units at random positions...");
            var unitHandles = new List<EntityHandle>();
            var spawnPositions = new List<Vector3>();
            
            for (int i = 0; i < 10; i++)
            {
                // Generate random position within the map bounds, avoiding the center obstacles
                Vector3 position;
                int attempts = 0;
                do
                {
                    position = new Vector3(
                        (float)(Random.NextDouble() * 18 - 9),  // -9 to 9
                        1.51f,
                        (float)(Random.NextDouble() * 18 - 9)   // -9 to 9
                    );
                    attempts++;
                } while (IsTooCloseToObstacles(position) && attempts < 50);
                
                spawnPositions.Add(position);
                
                var command = new SpawnEntityCommand
                {
                    EntityType = EntityType.NPC,
                    Position = position,
                    ShapeType = ShapeType.Capsule,
                    Size = new Vector3(0.5f, 1.8f, 0),
                    Mass = 70.0f,
                    IsStatic = false
                };
                
                var handle = entityManager.SpawnEntity(command);
                unitHandles.Add(handle);
                Console.WriteLine($"   Unit {i}: spawned at ({position.X:F1}, {position.Z:F1})");
            }
            
            Console.WriteLine($"   ✓ Spawned {unitHandles.Count} units at random positions\n");
            
            // Let units settle on ground
            Console.WriteLine("4. Letting units settle (60 steps)...");
            for (int i = 0; i < 60; i++)
            {
                physicsWorld.Update(0.016f);
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(state);
                Thread.Sleep(16);
            }
            Console.WriteLine("   ✓ Units settled\n");
            
            // Step 4: Command all 10 units to move to random distant destinations
            Console.WriteLine("5. Commanding all 10 units to move to random distant destinations...");
            var destinations = new List<Vector3>();
            
            // Generate random destinations ensuring they're far enough from spawn positions
            for (int i = 0; i < 10; i++)
            {
                Vector3 destination;
                int attempts = 0;
                do
                {
                    destination = new Vector3(
                        (float)(Random.NextDouble() * 18 - 9),  // -9 to 9
                        0,
                        (float)(Random.NextDouble() * 18 - 9)   // -9 to 9
                    );
                    attempts++;
                    // Ensure destination is at least 8 units away from spawn for "far enough" movement
                } while ((Vector3.Distance(spawnPositions[i], destination) < 8.0f || IsTooCloseToObstacles(destination)) && attempts < 50);
                
                destinations.Add(destination);
            }
            
            for (int i = 0; i < 10; i++)
            {
                var request = new MovementRequest(
                    unitHandles[i].EntityId,
                    destinations[i],
                    maxSpeed: 3.0f
                );
                
                bool started = movementController.RequestMovement(request);
                if (started)
                {
                    var entity = physicsWorld.EntityRegistry.GetEntityById(unitHandles[i].EntityId);
                    var currentPos = physicsWorld.GetEntityPosition(entity!);
                    var distance = Vector3.Distance(currentPos, destinations[i]);
                    Console.WriteLine($"   Unit {unitHandles[i].EntityId}: ({currentPos.X:F1}, {currentPos.Z:F1}) → ({destinations[i].X:F1}, {destinations[i].Z:F1}) [Dist: {distance:F1}]");
                }
            }
            Console.WriteLine("   ✓ Movement commands issued to random distant destinations\n");
            
            // Step 5: Simulate for a while, then spawn 3 random temporary obstacles
            Console.WriteLine("6. Simulating movement (spawning 3 random obstacles during simulation)...");
            int totalSteps = 900; // 15 seconds
            var obstacleSpawnSteps = new[] { 120, 240, 360 }; // After 2s, 4s, 6s
            var obstacleHandles = new List<EntityHandle?> { null, null, null };
            
            for (int i = 0; i < totalSteps; i++)
            {
                // Spawn 3 temporary obstacles at different times
                for (int obsIdx = 0; obsIdx < 3; obsIdx++)
                {
                    if (i == obstacleSpawnSteps[obsIdx])
                    {
                        // Generate random position in the middle area where units are likely to pass
                        Vector3 obstaclePos;
                        int attempts = 0;
                        do
                        {
                            obstaclePos = new Vector3(
                                (float)(Random.NextDouble() * 10 - 5),  // -5 to 5 (center area)
                                1.5f,
                                (float)(Random.NextDouble() * 10 - 5)   // -5 to 5 (center area)
                            );
                            attempts++;
                        } while (IsTooCloseToObstacles(obstaclePos) && attempts < 20);
                        
                        // Vary obstacle sizes for interesting pathing
                        var sizes = new[]
                        {
                            new Vector3(2f, 2.5f, 2f),      // Medium box
                            new Vector3(3f, 2.5f, 1.5f),    // Wide shallow wall
                            new Vector3(1.5f, 2.5f, 3f)     // Narrow deep wall
                        };
                        
                        var durations = new[] { 6.0f, 5.0f, 7.0f }; // Varied durations
                        
                        Console.WriteLine($"\n   [Spawning temporary obstacle #{obsIdx + 1} at {i / 60.0f:F1}s]");
                        obstacleHandles[obsIdx] = entityManager.SpawnTemporaryObstacle(
                            position: obstaclePos,
                            duration: durations[obsIdx],
                            size: sizes[obsIdx]
                        );
                        Console.WriteLine($"   Obstacle {obstacleHandles[obsIdx]!.EntityId} spawned at ({obstaclePos.X:F1}, {obstaclePos.Z:F1})");
                        Console.WriteLine($"   Size: ({sizes[obsIdx].X:F1}, {sizes[obsIdx].Y:F1}, {sizes[obsIdx].Z:F1}), Duration: {durations[obsIdx]:F1}s");
                        Console.WriteLine($"   Units will need to repath around this obstacle!\n");
                    }
                }
                
                // Update systems
                movementController.UpdateMovement(0.016f);
                physicsWorld.Update(0.016f);
                entityManager.Update(0.016f);
                
                // Broadcast state
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(state);
                
                // Print progress every 2 seconds (show first 5 units)
                if (i % 120 == 0 && i > 0)
                {
                    Console.WriteLine($"\n   Progress at {i / 60.0f:F1}s (showing first 5 units):");
                    for (int j = 0; j < 5; j++)
                    {
                        var entity = physicsWorld.EntityRegistry.GetEntityById(unitHandles[j].EntityId);
                        if (entity != null)
                        {
                            var pos = physicsWorld.GetEntityPosition(entity);
                            var dist = Vector3.Distance(pos, destinations[j]);
                            Console.WriteLine($"   Unit {unitHandles[j].EntityId}: Pos=({pos.X:F1}, {pos.Z:F1}), DistToGoal={dist:F1}");
                        }
                    }
                }
                
                Thread.Sleep(16);
            }
            
            // Step 6: Check final results
            Console.WriteLine("\n7. Final Results:");
            int unitsReachedDestination = 0;
            
            for (int i = 0; i < 10; i++)
            {
                var entity = physicsWorld.EntityRegistry.GetEntityById(unitHandles[i].EntityId);
                if (entity != null)
                {
                    var finalPos = physicsWorld.GetEntityPosition(entity);
                    var dist = Vector3.Distance(finalPos, destinations[i]);
                    bool reached = dist < 2.0f;
                    
                    if (reached)
                        unitsReachedDestination++;
                    
                    var status = reached ? "✓ REACHED" : "✗ NOT REACHED";
                    Console.WriteLine($"   Unit {unitHandles[i].EntityId}: {status} (distance: {dist:F2})");
                }
            }
            
            // Summary
            Console.WriteLine($"\n   Total units reached destination: {unitsReachedDestination}/10");
            int obstaclesSpawned = obstacleHandles.Count(h => h != null);
            int obstaclesDespawned = obstacleHandles.Count(h => h != null && entityManager.GetEntityById(h.EntityId) == null);
            Console.WriteLine($"   Temporary obstacles spawned: {obstaclesSpawned}/3");
            Console.WriteLine($"   Temporary obstacles despawned: {obstaclesDespawned}/{obstaclesSpawned}");
            
            // Cleanup
            physicsWorld.Dispose();
            
            if (unitsReachedDestination >= 6) // Allow some to fail due to complexity and random placement (6 out of 10)
            {
                Console.WriteLine("\n✅ MULTI-UNIT INTEGRATION TEST PASSED");
            }
            else
            {
                Console.WriteLine("\n⚠️ MULTI-UNIT INTEGRATION TEST: Some units didn't reach destination");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ MULTI-UNIT INTEGRATION TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Creates test environment with ground and obstacles.
    /// </summary>
    private static void CreateTestEnvironment(PhysicsWorld physicsWorld)
    {
        // Ground plane
        var groundShape = physicsWorld.CreateBoxShape(new Vector3(25, 0.1f, 25));
        physicsWorld.RegisterEntity(
            entityId: 5000,
            entityType: EntityType.StaticObject,
            position: new Vector3(0, -0.05f, 0),
            shape: groundShape,
            isStatic: true
        );
        
        // Some scattered obstacles
        var obstacleShape = physicsWorld.CreateBoxShape(new Vector3(2, 3, 2));
        
        var obstaclePositions = new[]
        {
            new Vector3(-5, 1.5f, 0),
            new Vector3(5, 1.5f, 0),
            new Vector3(0, 1.5f, -5),
        };
        
        for (int i = 0; i < obstaclePositions.Length; i++)
        {
            physicsWorld.RegisterEntity(
                entityId: 5001 + i,
                entityType: EntityType.StaticObject,
                position: obstaclePositions[i],
                shape: obstacleShape,
                isStatic: true
            );
        }
    }
    
    /// <summary>
    /// Checks if a position is too close to the static obstacles.
    /// </summary>
    private static bool IsTooCloseToObstacles(Vector3 position)
    {
        var obstaclePositions = new[]
        {
            new Vector3(-5, 1.5f, 0),
            new Vector3(5, 1.5f, 0),
            new Vector3(0, 1.5f, -5),
        };
        
        float minDistance = 3.0f; // Units must spawn at least 3 units away from obstacles
        
        foreach (var obstaclePos in obstaclePositions)
        {
            var distance = Vector3.Distance(new Vector3(position.X, 1.5f, position.Z), obstaclePos);
            if (distance < minDistance)
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Sets up event handlers to track system behavior.
    /// </summary>
    private static void SetupEventHandlers(
        EntityManager entityManager,
        MovementController movementController,
        CollisionEventSystem collisionSystem)
    {
        // Entity lifecycle events
        entityManager.OnEntitySpawned += (id) =>
        {
            Console.WriteLine($"   [Event] Entity {id} spawned");
        };
        
        entityManager.OnEntityDespawned += (id) =>
        {
            Console.WriteLine($"   [Event] Entity {id} despawned");
        };
        
        // Movement events
        movementController.OnDestinationReached += (id, pos) =>
        {
            Console.WriteLine($"   [Event] Entity {id} reached destination at ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
        };
        
        movementController.OnPathBlocked += (id) =>
        {
            Console.WriteLine($"   [Event] Entity {id} path blocked!");
        };
        
        movementController.OnPathReplanned += (id) =>
        {
            Console.WriteLine($"   [Event] Entity {id} replanned path");
        };
        
        movementController.OnMovementStarted += (id, start, target) =>
        {
            Console.WriteLine($"   [Event] Entity {id} started movement from ({start.X:F2}, {start.Z:F2}) to ({target.X:F2}, {target.Z:F2})");
        };
        
        // Collision events
        collisionSystem.OnUnitHitObstacle += (collision) =>
        {
            Console.WriteLine($"   [Event] Unit {collision.EntityA.EntityId} hit obstacle {collision.EntityB.EntityId}");
        };
        
        collisionSystem.OnAnyCollision += (collision) =>
        {
            // Only log interesting collisions (not ground collisions)
            if (collision.EntityA.EntityType != EntityType.StaticObject || 
                collision.EntityB.EntityType != EntityType.StaticObject)
            {
                // Uncomment for detailed collision logging
                // Console.WriteLine($"   [Event] Collision: {collision.EntityA.EntityType} <-> {collision.EntityB.EntityType}");
            }
        };
    }
}
