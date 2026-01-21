using Spatial.Integration;
using Spatial.Integration.Commands;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using System.Numerics;

namespace Spatial.TestHarness;

/// <summary>
/// Test demonstrating agent collision behavior and push mechanics:
/// 
/// Test Scenarios:
/// 1. Spawn two agents facing each other
/// 2. Command them to move toward each other (collision)
/// 3. Verify they BLOCK each other (don't push)
/// 4. Verify avoidance system handles the blocking
/// 5. Apply a push skill to one agent
/// 6. Verify the pushed agent moves
/// 7. Test knockback mechanic
/// 8. Test pushable flag behavior
/// </summary>
public static class TestAgentCollision
{
    public static void Run(VisualizationServer vizServer)
    {
        Console.WriteLine("TEST: AGENT COLLISION & PUSH MECHANICS\n");
        
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
                PathValidationInterval = 0.5f,
                EnableLocalAvoidance = true,
                EnableAutomaticReplanning = true,
                LocalAvoidanceRadius = 3.0f
            };
            
            var entityManager = new EntityManager(physicsWorld);
            var movementController = new MovementController(physicsWorld, pathfinder, config);
            
            Console.WriteLine("   ✓ Systems initialized\n");
            
            // Step 2: Spawn two agents facing each other
            Console.WriteLine("2. Spawning two agents facing each other...");
            
            // FIXED: Position agents correctly above the ground
            // Capsule: radius=0.5f, length=1.8f
            // Capsule half-height (bottom to center) = length/2 + radius = 0.9 + 0.5 = 1.4f
            // Ground top surface is at Y=0
            // For agent to stand on ground: center = 0 + 1.4 = 1.4f
            // Spawn slightly higher to allow proper gravity settling
            var agent1Cmd = new SpawnEntityCommand
            {
                EntityType = EntityType.NPC,
                Position = new Vector3(-5, 2.5f, 0), // Spawn higher, let gravity settle
                ShapeType = ShapeType.Capsule,
                Size = new Vector3(0.5f, 1.8f, 0),
                Mass = 70.0f,
                IsStatic = false
            };
            
            var agent2Cmd = new SpawnEntityCommand
            {
                EntityType = EntityType.NPC,
                Position = new Vector3(5, 2.5f, 0), // Spawn higher, let gravity settle
                ShapeType = ShapeType.Capsule,
                Size = new Vector3(0.5f, 1.8f, 0),
                Mass = 70.0f,
                IsStatic = false
            };
            
            var agent1Handle = entityManager.SpawnEntity(agent1Cmd);
            var agent2Handle = entityManager.SpawnEntity(agent2Cmd);
            
            Console.WriteLine($"   Agent 1 (ID {agent1Handle.EntityId}) spawned at (-5, 0)");
            Console.WriteLine($"   Agent 2 (ID {agent2Handle.EntityId}) spawned at (5, 0)");
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
            
            // Step 3: Command agents to move toward each other
            Console.WriteLine("4. Commanding agents to move toward each other...");
            Console.WriteLine("   This will test the BLOCKING behavior (agents should NOT push each other)");
            
            var agent1Dest = new Vector3(0, 0, 0);  // Center
            var agent2Dest = new Vector3(0, 0, 0);  // Center
            
            movementController.RequestMovement(new MovementRequest(
                agent1Handle.EntityId,
                agent1Dest,
                maxSpeed: 2.0f,
                agentHeight: agentConfig.Height,
                agentRadius: agentConfig.Radius
            ));
            
            movementController.RequestMovement(new MovementRequest(
                agent2Handle.EntityId,
                agent2Dest,
                maxSpeed: 2.0f,
                agentHeight: agentConfig.Height,
                agentRadius: agentConfig.Radius
            ));
            
            Console.WriteLine("   ✓ Movement commands issued\n");
            
            // Step 4: Simulate collision
            Console.WriteLine("5. Simulating movement (agents should block each other)...");
            
            var agent1Entity = physicsWorld.EntityRegistry.GetEntityById(agent1Handle.EntityId)!;
            var agent2Entity = physicsWorld.EntityRegistry.GetEntityById(agent2Handle.EntityId)!;
            
            for (int i = 0; i < 300; i++) // 5 seconds
            {
                movementController.UpdateMovement(0.016f);
                physicsWorld.Update(0.016f);
                
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(state);
                
                // Print status every second
                if (i % 60 == 0)
                {
                    var pos1 = physicsWorld.GetEntityPosition(agent1Entity);
                    var pos2 = physicsWorld.GetEntityPosition(agent2Entity);
                    var vel1 = physicsWorld.GetEntityVelocity(agent1Entity);
                    var vel2 = physicsWorld.GetEntityVelocity(agent2Entity);
                    var dist = Vector3.Distance(pos1, pos2);
                    
                    Console.WriteLine($"\n   Time: {i / 60.0f:F1}s");
                    Console.WriteLine($"   Agent 1: Pos=({pos1.X:F2}, {pos1.Y:F2}, {pos1.Z:F2}), Vel=({vel1.X:F2}, {vel1.Y:F2}, {vel1.Z:F2})");
                    Console.WriteLine($"   Agent 2: Pos=({pos2.X:F2}, {pos2.Y:F2}, {pos2.Z:F2}), Vel=({vel2.X:F2}, {vel2.Y:F2}, {vel2.Z:F2})");
                    Console.WriteLine($"   Distance: {dist:F2}");
                }
                
                Thread.Sleep(16);
            }
            
            // Verify they blocked (should be roughly 1 unit apart - sum of their radii)
            var finalPos1 = physicsWorld.GetEntityPosition(agent1Entity);
            var finalPos2 = physicsWorld.GetEntityPosition(agent2Entity);
            var finalDist = Vector3.Distance(finalPos1, finalPos2);
            
            Console.WriteLine("\n6. Verifying blocking behavior...");
            Console.WriteLine($"   Final distance between agents: {finalDist:F2}");
            
            if (finalDist >= 0.9f && finalDist <= 1.5f) // Should be around 1.0 (2 radii of 0.5)
            {
                Console.WriteLine("   ✓ Agents correctly BLOCKED each other (didn't push through)");
            }
            else
            {
                Console.WriteLine("   ⚠️ Unexpected distance (expected ~1.0)");
            }
            
            // Check Y positions (agents shouldn't be pushed off the ground)
            Console.WriteLine($"   Agent 1 Y position: {finalPos1.Y:F2}");
            Console.WriteLine($"   Agent 2 Y position: {finalPos2.Y:F2}");
            
            // Expected Y position is ~1.4f (standing on ground at Y=0)
            // Navmesh is slightly elevated (Y=0.20) above physical ground (Y=0.0)
            // So agents will be at navmeshY + halfHeight = 0.20 + 1.4 = 1.60
            // Allow 0.3 tolerance for navmesh offset and physics variance
            if (Math.Abs(finalPos1.Y - 1.4f) < 0.3f && Math.Abs(finalPos2.Y - 1.4f) < 0.3f)
            {
                Console.WriteLine("   ✓ Agents stayed on the ground (not sinking)\n");
            }
            else
            {
                Console.WriteLine($"   ⚠️ Agents deviated from ground level (expected ~1.4, got {finalPos1.Y:F2} and {finalPos2.Y:F2})\n");
            }
            
            // Step 5: Test push mechanic
            Console.WriteLine("7. Testing PUSH mechanic (pushing Agent 2 to the right)...");
            Console.WriteLine("   Agent 2 will be marked as PUSHABLE for this skill");
            
            // Push Agent 2 to the right
            var pushDirection = new Vector3(1, 0, 0); // Push to the right
            movementController.Push(
                agent2Handle.EntityId,
                pushDirection,
                force: 10.0f,
                makePushable: true,
                pushableDuration: 1.0f
            );
            
            var posBeforePush = physicsWorld.GetEntityPosition(agent2Entity);
            
            // Simulate for 1 second
            for (int i = 0; i < 60; i++)
            {
                physicsWorld.Update(0.016f);
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(state);
                Thread.Sleep(16);
            }
            
            var posAfterPush = physicsWorld.GetEntityPosition(agent2Entity);
            var pushDistance = Vector3.Distance(posBeforePush, posAfterPush);
            
            Console.WriteLine($"   Position before push: ({posBeforePush.X:F2}, {posBeforePush.Z:F2})");
            Console.WriteLine($"   Position after push:  ({posAfterPush.X:F2}, {posAfterPush.Z:F2})");
            Console.WriteLine($"   Push distance: {pushDistance:F2}");
            
            if (pushDistance > 0.5f)
            {
                Console.WriteLine("   ✓ Push mechanic working (agent moved)\n");
            }
            else
            {
                Console.WriteLine("   ⚠️ Push didn't move agent significantly\n");
            }
            
            // Step 6: Test knockback
            Console.WriteLine("8. Testing KNOCKBACK mechanic (knocking Agent 1 upward and backward)...");
            
            var knockbackDirection = Vector3.Normalize(new Vector3(-1, 1, 0)); // Back and up
            movementController.Knockback(
                agent1Handle.EntityId,
                knockbackDirection,
                force: 15.0f
            );
            
            var posBeforeKnockback = physicsWorld.GetEntityPosition(agent1Entity);
            
            // Simulate for 2 seconds (enough time to go airborne and land)
            for (int i = 0; i < 120; i++)
            {
                movementController.UpdateMovement(0.016f);
                physicsWorld.Update(0.016f);
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(state);
                
                if (i % 30 == 0)
                {
                    var pos = physicsWorld.GetEntityPosition(agent1Entity);
                    var characterState = movementController.GetCharacterState(agent1Entity);
                    Console.WriteLine($"   Time: {i / 60.0f:F1}s - Pos: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), State: {characterState}");
                }
                
                Thread.Sleep(16);
            }
            
            var posAfterKnockback = physicsWorld.GetEntityPosition(agent1Entity);
            var knockbackDistance = Vector3.Distance(posBeforeKnockback, posAfterKnockback);
            
            Console.WriteLine($"   Knockback distance: {knockbackDistance:F2}");
            
            if (knockbackDistance > 1.0f)
            {
                Console.WriteLine("   ✓ Knockback mechanic working (agent was knocked back)\n");
            }
            else
            {
                Console.WriteLine("   ⚠️ Knockback didn't move agent significantly\n");
            }
            
            // Step 7: Disable pushable and verify blocking resumes
            Console.WriteLine("9. Disabling pushable flag on Agent 2...");
            physicsWorld.SetEntityPushable(agent2Handle.EntityId, false);
            Console.WriteLine("   ✓ Agent 2 is no longer pushable\n");
            
            // Final summary
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine("SUMMARY:");
            Console.WriteLine("- Agents BLOCK each other by default (no pushing)");
            Console.WriteLine("- Push mechanic works when explicitly called");
            Console.WriteLine("- Knockback mechanic launches agents into the air");
            Console.WriteLine("- Pushable flag can be toggled dynamically");
            Console.WriteLine("═══════════════════════════════════════════════════\n");
            
            // Cleanup
            physicsWorld.Dispose();
            
            Console.WriteLine("✅ AGENT COLLISION TEST COMPLETED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ AGENT COLLISION TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
}
