using Spatial.Integration;
using Spatial.Integration.Commands;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using System.Numerics;

namespace Spatial.TestHarness;

/// <summary>
/// Test demonstrating improved local avoidance behavior:
/// 
/// Test Scenarios:
/// 1. Spawn two agents on opposite sides
/// 2. Command them to swap positions (crossing paths)
/// 3. Verify they avoid each other when they meet
/// 4. Verify they reach their destinations
/// 5. Monitor for ground sinking issues
/// </summary>
public static class TestLocalAvoidance
{
    public static void Run(VisualizationServer vizServer)
    {
        Console.WriteLine("TEST: LOCAL AVOIDANCE - AGENTS CROSSING PATHS\n");
        
        try
        {
            // Step 1: Initialize systems
            Console.WriteLine("1. Initializing systems...");
            var physicsWorld = new PhysicsWorld(new PhysicsConfiguration 
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            });
            
            // Create simple ground
            var groundShape = physicsWorld.CreateBoxShape(new Vector3(50, 0.1f, 50));
            physicsWorld.RegisterEntity(
                entityId: 1000,
                entityType: EntityType.StaticObject,
                position: new Vector3(0, -0.05f, 0),
                shape: groundShape,
                isStatic: true
            );
            
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
            
            var pathfinder = new Pathfinder(navMeshData);
            var config = new PathfindingConfiguration
            {
                PathValidationInterval = 0.2f,           // Check frequently for collisions
                EnableLocalAvoidance = true,
                EnableAutomaticReplanning = true,
                LocalAvoidanceRadius = 5.0f,            // Detect collisions early
                MaxAvoidanceNeighbors = 5,
                TryLocalAvoidanceFirst = false,         // Prefer replanning for head-on collisions
                ReplanCooldown = 0.5f                   // Allow quick replanning to avoid collisions
            };
            
            var entityManager = new EntityManager(physicsWorld);
            var movementController = new MovementController(physicsWorld, pathfinder, agentConfig, config);
            
            Console.WriteLine("   ✓ Systems initialized\n");
            
            // Step 2: Spawn two agents on opposite sides
            Console.WriteLine("2. Spawning two agents on opposite sides...");
            
            // Agent 1: Left side
            var agent1Cmd = new SpawnEntityCommand
            {
                EntityType = EntityType.NPC,
                Position = new Vector3(-8, 2.5f, 0),
                ShapeType = ShapeType.Capsule,
                Size = new Vector3(0.5f, 1.8f, 0),
                Mass = 70.0f,
                IsStatic = false
            };
            
            // Agent 2: Right side
            var agent2Cmd = new SpawnEntityCommand
            {
                EntityType = EntityType.NPC,
                Position = new Vector3(8, 2.5f, 0),
                ShapeType = ShapeType.Capsule,
                Size = new Vector3(0.5f, 1.8f, 0),
                Mass = 70.0f,
                IsStatic = false
            };
            
            var agent1Handle = entityManager.SpawnEntity(agent1Cmd);
            var agent2Handle = entityManager.SpawnEntity(agent2Cmd);
            
            Console.WriteLine($"   Agent 1 (ID {agent1Handle.EntityId}) spawned at (-8, 0)");
            Console.WriteLine($"   Agent 2 (ID {agent2Handle.EntityId}) spawned at (8, 0)");
            Console.WriteLine("   ✓ Agents spawned\n");
            
            // Broadcast initial state
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
            vizServer.BroadcastState(initialState);
            
            // Let agents settle
            Console.WriteLine("3. Letting agents settle...");
            for (int i = 0; i < 60; i++)
            {
                physicsWorld.Update(0.016f);
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(state);
                Thread.Sleep(16);
            }
            Console.WriteLine("   ✓ Agents settled\n");
            
            var agent1Entity = physicsWorld.EntityRegistry.GetEntityById(agent1Handle.EntityId)!;
            var agent2Entity = physicsWorld.EntityRegistry.GetEntityById(agent2Handle.EntityId)!;
            
            var agent1StartPos = physicsWorld.GetEntityPosition(agent1Entity);
            var agent2StartPos = physicsWorld.GetEntityPosition(agent2Entity);
            
            // Step 3: Command agents to SWAP positions (crossing paths)
            Console.WriteLine("4. Commanding agents to SWAP positions (they will cross paths)...");
            Console.WriteLine("   Agent 1: Moving from left (-8) to right (8)");
            Console.WriteLine("   Agent 2: Moving from right (8) to left (-8)");
            Console.WriteLine("   They should avoid each other when they meet in the middle!\n");
            
            // Agent 1 goes to where Agent 2 started
            var response1 = movementController.RequestMovement(new MovementRequest(
                agent1Handle.EntityId,
                new Vector3(8, 0, 0),
                maxSpeed: 2.0f,
                agentHeight: agentConfig.Height,
                agentRadius: agentConfig.Radius
            ));
            
            if (!response1.Success)
            {
                Console.WriteLine($"   ✗ Agent 1 movement failed: {response1.Message}");
            }
            
            // Agent 2 goes to where Agent 1 started
            var response2 = movementController.RequestMovement(new MovementRequest(
                agent2Handle.EntityId,
                new Vector3(-8, 0, 0),
                maxSpeed: 2.0f,
                agentHeight: agentConfig.Height,
                agentRadius: agentConfig.Radius
            ));
            
            if (!response2.Success)
            {
                Console.WriteLine($"   ✗ Agent 2 movement failed: {response2.Message}");
            }
            
            Console.WriteLine("   ✓ Movement commands issued\n");
            
            // Step 4: Monitor movement and avoidance
            Console.WriteLine("5. Monitoring movement (agents should avoid each other)...\n");
            
            int agent1Reached = 0;
            int agent2Reached = 0;
            
            movementController.OnDestinationReached += (entityId, position) =>
            {
                if (entityId == agent1Handle.EntityId)
                {
                    agent1Reached = 1;
                    Console.WriteLine($"\n   ✓ Agent 1 REACHED destination at ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");
                }
                else if (entityId == agent2Handle.EntityId)
                {
                    agent2Reached = 1;
                    Console.WriteLine($"\n   ✓ Agent 2 REACHED destination at ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");
                }
            };
            
            float lowestY1 = float.MaxValue;
            float lowestY2 = float.MaxValue;
            float closestDistance = float.MaxValue;
            float expectedY = 1.4f;  // Expected Y position for grounded agents
            
            // Simulate for 10 seconds or until both reach destination
            for (int i = 0; i < 600; i++)
            {
                movementController.UpdateMovement(0.016f);
                physicsWorld.Update(0.016f);
                
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(state);
                
                var pos1 = physicsWorld.GetEntityPosition(agent1Entity);
                var pos2 = physicsWorld.GetEntityPosition(agent2Entity);
                var vel1 = physicsWorld.GetEntityVelocity(agent1Entity);
                var vel2 = physicsWorld.GetEntityVelocity(agent2Entity);
                var dist = Vector3.Distance(pos1, pos2);
                
                // Track lowest Y positions (to detect sinking)
                lowestY1 = Math.Min(lowestY1, pos1.Y);
                lowestY2 = Math.Min(lowestY2, pos2.Y);
                closestDistance = Math.Min(closestDistance, dist);
                
                // Print status every second
                if (i % 60 == 0)
                {
                    Console.WriteLine($"   Time: {i / 60.0f:F1}s");
                    Console.WriteLine($"   Agent 1: X={pos1.X:F2}, Y={pos1.Y:F2}, Z={pos1.Z:F2} | Vel=({vel1.X:F2}, {vel1.Y:F2}, {vel1.Z:F2})");
                    Console.WriteLine($"   Agent 2: X={pos2.X:F2}, Y={pos2.Y:F2}, Z={pos2.Z:F2} | Vel=({vel2.X:F2}, {vel2.Y:F2}, {vel2.Z:F2})");
                    Console.WriteLine($"   Distance: {dist:F2}, Closest so far: {closestDistance:F2}");
                    Console.WriteLine($"   Lowest Y positions: Agent1={lowestY1:F2}, Agent2={lowestY2:F2}\n");
                }
                
                // Detect severe sinking (more than 30cm below expected)
                if (pos1.Y < expectedY - 0.3f || pos2.Y < expectedY - 0.3f)
                {
                    Console.WriteLine($"\n   ⚠️ WARNING: Agent sinking detected!");
                    Console.WriteLine($"   Agent 1 Y: {pos1.Y:F2} (expected ~{expectedY:F2})");
                    Console.WriteLine($"   Agent 2 Y: {pos2.Y:F2} (expected ~{expectedY:F2})\n");
                }
                
                Thread.Sleep(16);
                
                // Exit if both reached destination
                if (agent1Reached == 1 && agent2Reached == 1)
                {
                    Console.WriteLine($"\n   ✓ Both agents reached their destinations!");
                    break;
                }
            }
            
            // Final verification
            var finalPos1 = physicsWorld.GetEntityPosition(agent1Entity);
            var finalPos2 = physicsWorld.GetEntityPosition(agent2Entity);
            
            Console.WriteLine("\n6. Final Verification...");
            Console.WriteLine($"   Agent 1 final position: ({finalPos1.X:F2}, {finalPos1.Y:F2}, {finalPos1.Z:F2})");
            Console.WriteLine($"   Agent 2 final position: ({finalPos2.X:F2}, {finalPos2.Y:F2}, {finalPos2.Z:F2})");
            Console.WriteLine($"   Closest distance during crossing: {closestDistance:F2}");
            Console.WriteLine($"   Lowest Y positions: Agent1={lowestY1:F2}, Agent2={lowestY2:F2}");
            
            // Check if agents reached their destinations
            float dist1 = Vector3.Distance(finalPos1, new Vector3(8, finalPos1.Y, 0));
            float dist2 = Vector3.Distance(finalPos2, new Vector3(-8, finalPos2.Y, 0));
            
            bool agent1Success = dist1 < 1.0f;
            bool agent2Success = dist2 < 1.0f;
            
            Console.WriteLine("\n7. Test Results:");
            
            if (agent1Success)
            {
                Console.WriteLine("   ✓ Agent 1 reached destination (within 1m)");
            }
            else
            {
                Console.WriteLine($"   ✗ Agent 1 did NOT reach destination (distance: {dist1:F2}m)");
            }
            
            if (agent2Success)
            {
                Console.WriteLine("   ✓ Agent 2 reached destination (within 1m)");
            }
            else
            {
                Console.WriteLine($"   ✗ Agent 2 did NOT reach destination (distance: {dist2:F2}m)");
            }
            
            if (closestDistance >= 0.8f)
            {
                Console.WriteLine($"   ✓ Agents maintained safe distance (closest: {closestDistance:F2}m)");
            }
            else
            {
                Console.WriteLine($"   ⚠️ Agents got very close (closest: {closestDistance:F2}m)");
            }
            
            if (lowestY1 >= expectedY - 0.15f && lowestY2 >= expectedY - 0.15f)
            {
                Console.WriteLine($"   ✓ No ground sinking detected");
            }
            else
            {
                Console.WriteLine($"   ⚠️ Ground sinking detected (Agent1={lowestY1:F2}, Agent2={lowestY2:F2}, expected ~{expectedY:F2})");
            }
            
            // Final summary
            Console.WriteLine("\n═══════════════════════════════════════════════════");
            Console.WriteLine("SUMMARY:");
            Console.WriteLine($"- Agents crossed paths: {(agent1Success && agent2Success ? "SUCCESS" : "PARTIAL/FAILED")}");
            Console.WriteLine($"- Avoidance worked: {(closestDistance >= 0.8f ? "YES" : "NEEDS IMPROVEMENT")}");
            Console.WriteLine($"- Ground stability: {(lowestY1 >= expectedY - 0.15f && lowestY2 >= expectedY - 0.15f ? "GOOD" : "ISSUES DETECTED")}");
            Console.WriteLine("═══════════════════════════════════════════════════\n");
            
            // Cleanup
            physicsWorld.Dispose();
            
            Console.WriteLine("✅ LOCAL AVOIDANCE TEST COMPLETED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ LOCAL AVOIDANCE TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
}
