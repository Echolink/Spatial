using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;
using System.Numerics;
using System.Diagnostics;
using System;
using System.Linq;

namespace Spatial.TestHarness;

/// <summary>
/// Comparison test between velocity-based and motor-based character controllers.
/// 
/// This test runs Agent-3 (the problematic multi-level climb scenario) with both approaches
/// side-by-side to compare:
/// - Stability on steep slopes
/// - Success rate reaching goal
/// - CPU usage and performance
/// - Physics behavior (bouncing, launching, etc.)
/// - Ground contact maintenance
/// 
/// Agent-3 Setup:
/// - Start: Lower island at Y=-2.17m
/// - Goal: Upper island at Y=7.83m
/// - Challenge: 10m vertical climb over 14m horizontal (71.5% grade)
/// - Expected: Motor approach handles this smoothly, velocity approach fails
/// </summary>
public static class TestMotorVsVelocity
{
    public enum TestMode
    {
        Both,           // Run both controllers and compare
        VelocityOnly,   // Run only velocity-based controller
        MotorOnly       // Run only motor-based controller
    }

    private class ApproachResult
    {
        public string ApproachName { get; set; } = string.Empty;
        public bool ReachedGoal { get; set; }
        public float FinalDistanceToGoal { get; set; }
        public float MinDistanceToGoal { get; set; } = float.MaxValue;
        public float MaxYPosition { get; set; } = float.MinValue;
        public float MinYPosition { get; set; } = float.MaxValue;
        public float TotalDistanceTraveled { get; set; }
        public int SimulationSteps { get; set; }
        public bool HadPhysicsExplosion { get; set; } // Launched into air (Y > 20m)
        public bool FellThroughWorld { get; set; } // Y < -50m
        public int ReplanCount { get; set; }
        public List<(float Step, Vector3 Position, Vector3 Velocity)> Trajectory { get; set; } = new();
    }

    public static void Run(VisualizationServer vizServer, string? meshPath = null, TestMode mode = TestMode.Both)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   MOTOR VS VELOCITY CHARACTER CONTROLLER COMPARISON          ║");
        Console.WriteLine("║   Testing Agent-3 Multi-Level Climb Scenario                 ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        meshPath ??= ResolvePath("worlds/seperated_land.obj");
        
        ApproachResult? velocityResult = null;
        ApproachResult? motorResult = null;

        // Run velocity test if requested
        if (mode == TestMode.Both || mode == TestMode.VelocityOnly)
        {
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST: VELOCITY-BASED CHARACTER CONTROLLER");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            velocityResult = RunTest(vizServer, meshPath, useMotor: false);
            Console.WriteLine();
        }
        
        // Run motor test if requested
        if (mode == TestMode.Both || mode == TestMode.MotorOnly)
        {
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST: MOTOR-BASED CHARACTER CONTROLLER");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            motorResult = RunTest(vizServer, meshPath, useMotor: true);
            Console.WriteLine();
        }
        
        // Print comparison only if both were run
        if (mode == TestMode.Both && velocityResult != null && motorResult != null)
        {
            PrintComparison(velocityResult, motorResult);
        }
        else if (velocityResult != null)
        {
            PrintSingleResult(velocityResult);
        }
        else if (motorResult != null)
        {
            PrintSingleResult(motorResult);
        }
    }

    private static ApproachResult RunTest(VisualizationServer vizServer, string meshPath, bool useMotor)
    {
        var result = new ApproachResult
        {
            ApproachName = useMotor ? "Motor-Based" : "Velocity-Based"
        };

        PhysicsWorld? physicsWorld = null;
        MovementController? movementController = null;

        try
        {
            // ═══════════════════════════════════════════════════════════
            // PHASE 1: WORLD SETUP
            // ═══════════════════════════════════════════════════════════
            var config = new PhysicsConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            };
            physicsWorld = new PhysicsWorld(config);
            Console.WriteLine("✓ Physics world initialized");

            // Load world geometry
            WorldData? worldData = null;
            if (File.Exists(meshPath))
            {
                Console.WriteLine($"✓ Loading world from: {Path.GetFileName(meshPath)}");
                var meshLoader = new MeshLoader();
                var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);

                string? metadataPath = meshPath + ".json";
                if (!File.Exists(metadataPath)) metadataPath = null;

                worldData = worldBuilder.LoadAndBuildWorld(meshPath, metadataPath);
                
                int totalTris = worldData.Meshes.Sum(m => m.TriangleCount);
                Console.WriteLine($"✓ Loaded {worldData.Meshes.Count} meshes with {totalTris} triangles");
            }

            // ═══════════════════════════════════════════════════════════
            // PHASE 2: NAVMESH GENERATION
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 2: NAVMESH GENERATION");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            var agentConfig = new AgentConfig
            {
                Height = 1.8f,
                Radius = 0.5f,
                MaxClimb = 0.5f,
                MaxSlope = 45.0f
            };

            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            
            var navMeshStopwatch = Stopwatch.StartNew();
            NavMeshData? navMeshData;
            if (worldData != null)
            {
                // Use direct approach for loaded mesh
                navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);
            }
            else
            {
                // Use physics-based approach for procedural geometry
                navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
            }
            navMeshStopwatch.Stop();

            if (navMeshData == null || navMeshData.NavMesh == null)
            {
                Console.WriteLine("✗ NavMesh generation failed!");
                return result;
            }

            var tile = navMeshData.NavMesh.GetTile(0);
            int triangleCount = tile?.data != null ? tile.data.polys.Sum(p => p.vertCount - 2) : 0;
            
            Console.WriteLine($"✓ NavMesh generated in {navMeshStopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"  - Walkable triangles: {triangleCount}");

            var pathfinder = new Pathfinder(navMeshData);
            Console.WriteLine("✓ Pathfinder initialized");
            
            // Broadcast initial state
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
            vizServer.BroadcastState(initialState);

            // ═══════════════════════════════════════════════════════════
            // PHASE 3: CONTROLLER SETUP
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 3: CHARACTER CONTROLLER SETUP");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            CharacterController? velocityController = null;
            MotorCharacterController? motorController = null;

            if (useMotor)
            {
                var motorConfig = new MotorCharacterConfig
                {
                    MotorStrength = 0.3f,
                    HeightCorrectionStrength = 10.0f,
                    VerticalDamping = 0.5f,
                    StabilityThreshold = 0.2f
                };
                motorController = new MotorCharacterController(physicsWorld, motorConfig);
                Console.WriteLine("✓ Motor-based character controller initialized");
                Console.WriteLine($"  - Motor Strength: {motorConfig.MotorStrength}");
                Console.WriteLine($"  - Height Correction: {motorConfig.HeightCorrectionStrength}");
            }
            else
            {
                var velocityConfig = new CharacterControllerConfig
                {
                    GroundedVelocityThreshold = 0.5f,
                    StabilityThreshold = 0.2f
                };
                velocityController = new CharacterController(physicsWorld, velocityConfig);
                Console.WriteLine("✓ Velocity-based character controller initialized");
            }

            var pathfindingConfig = new PathfindingConfiguration
            {
                EnablePathAutoFix = true,
                PathValidationInterval = 0.5f,
                EnableLocalAvoidance = false,  // Disable for cleaner test
                EnableAutomaticReplanning = true
            };

            // Create PathfindingService
            var pathfindingService = new PathfindingService(pathfinder, agentConfig, pathfindingConfig);

            // Create movement controller with appropriate character controller
            movementController = useMotor
                ? new MovementController(physicsWorld, pathfindingService, agentConfig, pathfindingConfig, motorController)
                : new MovementController(physicsWorld, pathfindingService, agentConfig, pathfindingConfig, velocityController);

            Console.WriteLine("✓ Movement controller initialized");
            Console.WriteLine($"  - Path Auto-Fix: {pathfindingConfig.EnablePathAutoFix}");
            Console.WriteLine($"  - MaxClimb: {agentConfig.MaxClimb}m");
            Console.WriteLine($"  - MaxSlope: {agentConfig.MaxSlope}°");

            // ═══════════════════════════════════════════════════════════
            // PHASE 4: SPAWN AGENT-3
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 4: SPAWN AGENT-3");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            // Agent-3: Multi-level climb scenario (lower island to upper island)
            // Using the same coordinates as TestEnhancedShowcase.cs
            var startPos = new Vector3(51.89f, 0.29f, 10.19f);  // Lower island
            var goalPos = new Vector3(45.33f, 8.0f, 18.96f);     // Upper island
            float directDistance = Vector3.Distance(startPos, goalPos);

            Console.WriteLine($"Agent-3: Multi-Level Climb Challenge");
            Console.WriteLine($"  Start: ({startPos.X:F2}, {startPos.Y:F2}, {startPos.Z:F2})");
            Console.WriteLine($"  Goal:  ({goalPos.X:F2}, {goalPos.Y:F2}, {goalPos.Z:F2})");
            Console.WriteLine($"  Direct Distance: {directDistance:F2}m");
            Console.WriteLine($"  Vertical Climb: {goalPos.Y - startPos.Y:F2}m");
            Console.WriteLine();

            const float agentMass = 1.0f;
            var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
                agentConfig.Radius, agentConfig.Height, agentMass
            );
            
            var agent = physicsWorld.RegisterEntityWithInertia(
                entityId: 103,
                entityType: EntityType.Player,
                position: startPos,
                shape: agentShape,
                inertia: agentInertia,
                isStatic: false,
                disableGravity: false  // Enable gravity for proper physics
            );

            // Request movement
            var movementRequest = new MovementRequest(
                entityId: agent.EntityId,
                targetPosition: goalPos,
                maxSpeed: 3.0f,
                agentHeight: agentConfig.Height,
                agentRadius: agentConfig.Radius
            );

            var movementResponse = movementController.RequestMovement(movementRequest);
            if (movementResponse.Success)
            {
                Console.WriteLine("✓ Movement request initiated");
                Console.WriteLine($"  Path: {movementResponse.PathResult!.Waypoints.Count} waypoints, {movementResponse.EstimatedPathLength:F1}m");
            }
            else
            {
                Console.WriteLine($"✗ Movement request failed: {movementResponse.Message}");
                return result;
            }

            // Subscribe to events
            int replanCount = 0;
            movementController.OnPathReplanned += (entityId) =>
            {
                if (entityId == agent.EntityId)
                {
                    replanCount++;
                    Console.WriteLine($"  [Step {result.SimulationSteps}] Path replanned (total: {replanCount})");
                }
            };

            // ═══════════════════════════════════════════════════════════
            // PHASE 5: RUN SIMULATION
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 5: SIMULATION");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            const int MAX_STEPS = 3000;  // 50 seconds at 60fps
            const float DELTA_TIME = 0.016f;
            const int REPORT_INTERVAL = 187; // ~3 seconds

            Vector3? lastPosition = null;
            int lastReportStep = 0;

            for (int step = 0; step < MAX_STEPS; step++)
            {
                result.SimulationSteps = step + 1;

                // Update physics and movement
                physicsWorld.Update(DELTA_TIME);
                movementController.UpdateMovement(DELTA_TIME);

                // Get agent position and velocity
                var position = physicsWorld.GetEntityPosition(agent);
                var velocity = physicsWorld.GetEntityVelocity(agent);
                float distanceToGoal = Vector3.Distance(position, goalPos);

                // Record trajectory (sample every 10 steps to save memory)
                if (step % 10 == 0)
                {
                    result.Trajectory.Add((step, position, velocity));
                }

                // Track metrics
                result.MinDistanceToGoal = Math.Min(result.MinDistanceToGoal, distanceToGoal);
                result.MaxYPosition = Math.Max(result.MaxYPosition, position.Y);
                result.MinYPosition = Math.Min(result.MinYPosition, position.Y);

                if (lastPosition.HasValue)
                {
                    result.TotalDistanceTraveled += Vector3.Distance(lastPosition.Value, position);
                }
                lastPosition = position;

                // Check for physics explosion (launched into air)
                if (position.Y > 20.0f)
                {
                    result.HadPhysicsExplosion = true;
                }

                // Check for falling through world
                if (position.Y < -50.0f)
                {
                    result.FellThroughWorld = true;
                    Console.WriteLine($"  [Step {step}] ⚠️ FELL THROUGH WORLD (Y={position.Y:F2}m)");
                    break;
                }

                // Check if reached goal
                if (distanceToGoal < 1.0f)
                {
                    result.ReachedGoal = true;
                    Console.WriteLine();
                    Console.WriteLine($"  [Step {step}] ✅ GOAL REACHED!");
                    Console.WriteLine($"    Position: ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");
                    Console.WriteLine($"    Distance to goal: {distanceToGoal:F2}m");
                    Console.WriteLine($"    Total distance traveled: {result.TotalDistanceTraveled:F2}m");
                    break;
                }

                // Periodic reporting
                if (step - lastReportStep >= REPORT_INTERVAL)
                {
                    Console.WriteLine($"  [Step {step}] Y={position.Y:F2}m, Vel=({velocity.X:F1}, {velocity.Y:F1}, {velocity.Z:F1}), " +
                                    $"Dist={distanceToGoal:F2}m");
                    lastReportStep = step;
                }

                // Send to visualization
                if (step % 5 == 0)  // Broadcast every 5 steps
                {
                    var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData, null, agent.EntityId);
                    
                    // Add waypoints for visualization
                    var waypoints = movementController.GetWaypoints(agent.EntityId);
                    if (waypoints != null && waypoints.Count > 0)
                    {
                        state.AgentPaths.Add(new PathData
                        {
                            EntityId = agent.EntityId,
                            Waypoints = waypoints.Select(wp => new[] { wp.X, wp.Y, wp.Z }).ToList(),
                            PathLength = 0
                        });
                        
                        // Debug log first broadcast
                        if (step == 0)
                        {
                            Console.WriteLine($"[MotorTest] Broadcasting agent path with {waypoints.Count} waypoints");
                        }
                    }
                    
                    vizServer.BroadcastState(state);
                }
                
                Thread.Sleep(16); // ~60 FPS
            }

            result.FinalDistanceToGoal = Vector3.Distance(
                physicsWorld.GetEntityPosition(agent),
                goalPos
            );
            result.ReplanCount = replanCount;

            // Final status
            Console.WriteLine();
            if (result.ReachedGoal)
            {
                Console.WriteLine($"✅ SUCCESS - Agent reached goal");
            }
            else
            {
                var finalPos = physicsWorld.GetEntityPosition(agent);
                Console.WriteLine($"❌ FAILED - Agent did not reach goal");
                Console.WriteLine($"  Final position: ({finalPos.X:F2}, {finalPos.Y:F2}, {finalPos.Z:F2})");
                Console.WriteLine($"  Final distance: {result.FinalDistanceToGoal:F2}m");
                Console.WriteLine($"  Min distance: {result.MinDistanceToGoal:F2}m");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            physicsWorld?.Dispose();
        }

        return result;
    }

    private static void PrintComparison(ApproachResult velocity, ApproachResult motor)
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   COMPARISON RESULTS                                         ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Success comparison
        Console.WriteLine("SUCCESS:");
        Console.WriteLine($"  Velocity-Based: {(velocity.ReachedGoal ? "✅ Reached Goal" : "❌ Failed")}");
        Console.WriteLine($"  Motor-Based:    {(motor.ReachedGoal ? "✅ Reached Goal" : "❌ Failed")}");
        Console.WriteLine();

        // Distance comparison
        Console.WriteLine("FINAL DISTANCE TO GOAL:");
        Console.WriteLine($"  Velocity-Based: {velocity.FinalDistanceToGoal:F2}m");
        Console.WriteLine($"  Motor-Based:    {motor.FinalDistanceToGoal:F2}m");
        Console.WriteLine($"  Improvement:    {velocity.FinalDistanceToGoal - motor.FinalDistanceToGoal:F2}m " +
                        $"({(velocity.FinalDistanceToGoal - motor.FinalDistanceToGoal) / velocity.FinalDistanceToGoal * 100:F1}%)");
        Console.WriteLine();

        // Closest approach
        Console.WriteLine("CLOSEST APPROACH TO GOAL:");
        Console.WriteLine($"  Velocity-Based: {velocity.MinDistanceToGoal:F2}m");
        Console.WriteLine($"  Motor-Based:    {motor.MinDistanceToGoal:F2}m");
        Console.WriteLine();

        // Y position range
        Console.WriteLine("Y POSITION RANGE:");
        Console.WriteLine($"  Velocity-Based: {velocity.MinYPosition:F2}m to {velocity.MaxYPosition:F2}m");
        Console.WriteLine($"  Motor-Based:    {motor.MinYPosition:F2}m to {motor.MaxYPosition:F2}m");
        Console.WriteLine();

        // Physics stability
        Console.WriteLine("PHYSICS STABILITY:");
        Console.WriteLine($"  Velocity-Based Physics Explosion: {(velocity.HadPhysicsExplosion ? "❌ YES (Y > 20m)" : "✅ No")}");
        Console.WriteLine($"  Motor-Based Physics Explosion:    {(motor.HadPhysicsExplosion ? "❌ YES (Y > 20m)" : "✅ No")}");
        Console.WriteLine($"  Velocity-Based Fell Through World: {(velocity.FellThroughWorld ? "❌ YES (Y < -50m)" : "✅ No")}");
        Console.WriteLine($"  Motor-Based Fell Through World:    {(motor.FellThroughWorld ? "❌ YES (Y < -50m)" : "✅ No")}");
        Console.WriteLine();

        // Distance traveled
        Console.WriteLine("TOTAL DISTANCE TRAVELED:");
        Console.WriteLine($"  Velocity-Based: {velocity.TotalDistanceTraveled:F2}m");
        Console.WriteLine($"  Motor-Based:    {motor.TotalDistanceTraveled:F2}m");
        Console.WriteLine();

        // Replanning
        Console.WriteLine("REPLANNING:");
        Console.WriteLine($"  Velocity-Based: {velocity.ReplanCount} replans");
        Console.WriteLine($"  Motor-Based:    {motor.ReplanCount} replans");
        Console.WriteLine();

        // Simulation steps
        Console.WriteLine("SIMULATION STEPS:");
        Console.WriteLine($"  Velocity-Based: {velocity.SimulationSteps} steps ({velocity.SimulationSteps * 0.016f:F1}s)");
        Console.WriteLine($"  Motor-Based:    {motor.SimulationSteps} steps ({motor.SimulationSteps * 0.016f:F1}s)");
        Console.WriteLine();

        // Verdict
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("VERDICT:");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        if (motor.ReachedGoal && !velocity.ReachedGoal)
        {
            Console.WriteLine("✅ MOTOR APPROACH WINS - Successfully completed the climb");
            Console.WriteLine("   Velocity approach failed, confirming motor-based control");
            Console.WriteLine("   is necessary for steep multi-level terrain.");
        }
        else if (velocity.ReachedGoal && !motor.ReachedGoal)
        {
            Console.WriteLine("⚠️ VELOCITY APPROACH WINS - Unexpected result!");
            Console.WriteLine("   Motor approach may need tuning.");
        }
        else if (motor.ReachedGoal && velocity.ReachedGoal)
        {
            Console.WriteLine("✅ BOTH APPROACHES SUCCEEDED");
            Console.WriteLine("   However, compare stability metrics to choose final approach.");
        }
        else
        {
            Console.WriteLine("❌ BOTH APPROACHES FAILED");
            Console.WriteLine("   Further investigation needed - may require NavMesh tuning");
            Console.WriteLine("   or different architectural approach.");
        }
        Console.WriteLine();
    }

    private static void PrintSingleResult(ApproachResult result)
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║   {result.ApproachName.ToUpper()} RESULTS                    ");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Success
        Console.WriteLine("SUCCESS:");
        Console.WriteLine($"  {(result.ReachedGoal ? "✅ Reached Goal" : "❌ Failed")}");
        Console.WriteLine();

        // Distance to goal
        Console.WriteLine("FINAL DISTANCE TO GOAL:");
        Console.WriteLine($"  {result.FinalDistanceToGoal:F2}m");
        Console.WriteLine();

        // Closest approach
        Console.WriteLine("CLOSEST APPROACH TO GOAL:");
        Console.WriteLine($"  {result.MinDistanceToGoal:F2}m");
        Console.WriteLine();

        // Y position range
        Console.WriteLine("Y POSITION RANGE:");
        Console.WriteLine($"  {result.MinYPosition:F2}m to {result.MaxYPosition:F2}m");
        Console.WriteLine();

        // Physics stability
        Console.WriteLine("PHYSICS STABILITY:");
        Console.WriteLine($"  Physics Explosion: {(result.HadPhysicsExplosion ? "❌ YES (Y > 20m)" : "✅ No")}");
        Console.WriteLine($"  Fell Through World: {(result.FellThroughWorld ? "❌ YES (Y < -50m)" : "✅ No")}");
        Console.WriteLine();

        // Distance traveled
        Console.WriteLine("TOTAL DISTANCE TRAVELED:");
        Console.WriteLine($"  {result.TotalDistanceTraveled:F2}m");
        Console.WriteLine();

        // Replanning
        Console.WriteLine("REPLANNING:");
        Console.WriteLine($"  {result.ReplanCount} replans");
        Console.WriteLine();

        // Simulation steps
        Console.WriteLine("SIMULATION STEPS:");
        Console.WriteLine($"  {result.SimulationSteps} steps ({result.SimulationSteps * 0.016f:F1}s)");
        Console.WriteLine();

        // Verdict
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("VERDICT:");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        if (result.ReachedGoal)
        {
            Console.WriteLine($"✅ {result.ApproachName} SUCCESSFULLY COMPLETED THE CLIMB");
            Console.WriteLine($"   Completed in {result.SimulationSteps * 0.016f:F1}s with {result.ReplanCount} replanning event(s)");
        }
        else
        {
            Console.WriteLine($"❌ {result.ApproachName} FAILED TO REACH GOAL");
            Console.WriteLine($"   Closest approach: {result.MinDistanceToGoal:F2}m");
        }
        Console.WriteLine();
    }

    private static string ResolvePath(string relativePath)
    {
        // Try workspace root first
        var workspaceRoot = FindWorkspaceRoot();
        if (workspaceRoot != null)
        {
            var fullPath = Path.Combine(workspaceRoot, "Spatial.TestHarness", relativePath);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Try current directory
        var currentPath = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        if (File.Exists(currentPath))
            return currentPath;

        // Try relative to assembly
        var assemblyPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (assemblyPath != null)
        {
            var assemblyRelativePath = Path.Combine(assemblyPath, relativePath);
            if (File.Exists(assemblyRelativePath))
                return assemblyRelativePath;
        }

        return relativePath;
    }

    private static string? FindWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "Spatial.sln")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }
}
