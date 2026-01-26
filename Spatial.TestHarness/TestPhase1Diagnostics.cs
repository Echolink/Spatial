using Spatial.Integration;
using Spatial.Integration.Diagnostics;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;
using System.Numerics;
using System.Diagnostics;
using System;
using BepuPhysics;

namespace Spatial.TestHarness;

/// <summary>
/// Phase 1 Diagnostic Test: Comprehensive logging and analysis of Agent-3's failing path.
/// 
/// This test enables all diagnostic utilities to capture:
/// - Mesh interpretation differences between BepuPhysics and DotRecast
/// - Ground contact details (normals, penetration, timing)
/// - Grounding force effectiveness vs gravity
/// 
/// Goal: Understand WHY Agent-3 falls, not just observe that it does.
/// </summary>
public static class TestPhase1Diagnostics
{
    private const int AGENT3_ENTITY_ID = 103;
    private const int DIAGNOSTIC_SAMPLE_INTERVAL = 30; // Log every 30 steps
    
    public static void Run(VisualizationServer vizServer, string? meshPath = null)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   PHASE 1: DIAGNOSTIC ANALYSIS TEST                          ║");
        Console.WriteLine("║   Investigating Agent-3 Falling Issue                        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        PhysicsWorld? physicsWorld = null;
        Pathfinder? pathfinder = null;
        
        try
        {
            // ═══════════════════════════════════════════════════════════
            // SETUP: World and Physics
            // ═══════════════════════════════════════════════════════════
            var config = new PhysicsConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            };
            physicsWorld = new PhysicsWorld(config);
            Console.WriteLine("✓ Physics world initialized (Gravity: -9.81 m/s²)");
            
            // Load world
            meshPath ??= ResolvePath("worlds/seperated_land.obj");
            if (!File.Exists(meshPath))
            {
                Console.WriteLine($"❌ Mesh file not found: {meshPath}");
                return;
            }
            
            Console.WriteLine($"✓ Loading world: {Path.GetFileName(meshPath)}");
            var meshLoader = new MeshLoader();
            var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);
            
            var worldData = worldBuilder.LoadAndBuildWorld(meshPath);
            if (worldData == null)
            {
                Console.WriteLine("❌ Failed to load world");
                return;
            }
            
            Console.WriteLine($"✓ World loaded: {worldData.Meshes.Count} meshes");
            
            // Build navmesh
            Console.WriteLine("Building navmesh...");
            var agentConfig = new AgentConfig
            {
                Height = 1.8f,
                Radius = 0.5f,
                MaxClimb = 0.5f,      // Agent can step up 0.5m (stairs/curbs)
                MaxSlope = 45.0f,     // Agent can walk 45° slopes
                CellSize = 0.25f,     // Finer voxelization for better accuracy
                CellHeight = 0.12f    // Smaller vertical cells for steep terrain
            };
            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            var navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);
            pathfinder = new Pathfinder(navMeshData);
            
            if (pathfinder == null)
            {
                Console.WriteLine("❌ Failed to build navmesh");
                return;
            }
            
            Console.WriteLine($"✓ NavMesh generated");
            
            // Export navmesh for visual inspection
            var navmeshExportPath = Path.Combine(Path.GetDirectoryName(meshPath) ?? ".", "exported_navmesh.obj");
            navMeshData.ExportToObj(navmeshExportPath);
            Console.WriteLine($"✓ NavMesh exported to: {navmeshExportPath}");
            
            // Analyze navmesh slopes
            var slopeAnalysis = navMeshData.AnalyzeSlopes();
            Console.WriteLine($"✓ NavMesh slope analysis:");
            Console.WriteLine($"    Polygons: {slopeAnalysis.PolyCount}");
            Console.WriteLine($"    Y Range: [{slopeAnalysis.MinY:F2}, {slopeAnalysis.MaxY:F2}]");
            Console.WriteLine($"    Slope Range: [{slopeAnalysis.MinSlope:F2}°, {slopeAnalysis.MaxSlope:F2}°]");
            Console.WriteLine($"    Average Slope: {slopeAnalysis.AvgSlope:F2}°");
            Console.WriteLine();
            
            // ═══════════════════════════════════════════════════════════
            // VALIDATION: Check if path is possible given agent constraints
            // ═══════════════════════════════════════════════════════════
            var agent3Start = new Vector3(51.89f, 0.29f, 10.19f);
            var agent3Goal = new Vector3(45.33f, 8.0f, 18.96f);
            var verticalChange = Math.Abs(agent3Goal.Y - agent3Start.Y);
            
            Console.WriteLine("──────────────────────────────────────────────────────────────");
            Console.WriteLine("CONFIGURATION VALIDATION");
            Console.WriteLine("──────────────────────────────────────────────────────────────");
            Console.WriteLine($"Agent Configuration:");
            Console.WriteLine($"  • MaxClimb: {agentConfig.MaxClimb}m");
            Console.WriteLine($"  • MaxSlope: {agentConfig.MaxSlope}°");
            Console.WriteLine($"  • Cell Size: {agentConfig.CellSize}m");
            Console.WriteLine($"  • Cell Height: {agentConfig.CellHeight}m");
            Console.WriteLine();
            Console.WriteLine($"Path Requirements:");
            Console.WriteLine($"  • Start Y: {agent3Start.Y:F2}m");
            Console.WriteLine($"  • Goal Y: {agent3Goal.Y:F2}m");
            Console.WriteLine($"  • Vertical Change: {verticalChange:F2}m");
            Console.WriteLine();
            
            if (verticalChange > agentConfig.MaxClimb * 2.0f)
            {
                Console.WriteLine($"⚠️  WARNING: Vertical change ({verticalChange:F2}m) significantly exceeds MaxClimb ({agentConfig.MaxClimb}m)");
                Console.WriteLine($"   This path requires gradual slopes or stairs, not single steps.");
                Console.WriteLine($"   The navmesh may create invalid paths if terrain is too steep.");
            }
            Console.WriteLine();
            
            // ═══════════════════════════════════════════════════════════
            // PHASE 1a: MESH ANALYSIS
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 1a: MESH INTERPRETATION ANALYSIS");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            
            var meshDiagnostics = new MeshDiagnostics(physicsWorld, pathfinder);
            
            // Analyze Agent-3's path area (center of the ramp region)
            var pathMidpoint = (agent3Start + agent3Goal) / 2.0f;
            
            Console.WriteLine("Analyzing Agent-3's path area...");
            Console.WriteLine($"  Start: ({agent3Start.X:F2}, {agent3Start.Y:F2}, {agent3Start.Z:F2})");
            Console.WriteLine($"  Goal: ({agent3Goal.X:F2}, {agent3Goal.Y:F2}, {agent3Goal.Z:F2})");
            Console.WriteLine($"  Midpoint: ({pathMidpoint.X:F2}, {pathMidpoint.Y:F2}, {pathMidpoint.Z:F2})");
            Console.WriteLine();
            
            var report = meshDiagnostics.AnalyzeArea(pathMidpoint, 15.0f, "Agent-3 Path Region");
            MeshDiagnostics.PrintReport(report);
            
            // ═══════════════════════════════════════════════════════════
            // PHASE 1b: AGENT-3 SIMULATION WITH DIAGNOSTICS
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 1b: AGENT-3 SIMULATION WITH CONTACT & GROUNDING LOGGING");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            
            // Enable contact diagnostics
            ContactDiagnostics.IsEnabled = true;
            ContactDiagnostics.TrackedEntityId = AGENT3_ENTITY_ID;
            ContactDiagnostics.Clear();
            
            // Enable grounding diagnostics
            GroundingDiagnostics.IsEnabled = true;
            GroundingDiagnostics.TrackedEntityId = AGENT3_ENTITY_ID;
            GroundingDiagnostics.Clear();
            
            Console.WriteLine($"✓ Contact diagnostics enabled for Entity {AGENT3_ENTITY_ID}");
            Console.WriteLine($"✓ Grounding diagnostics enabled for Entity {AGENT3_ENTITY_ID}");
            Console.WriteLine();
            
            // Create movement controller
            var pathfindingConfig = new PathfindingConfiguration
            {
                EnableLocalAvoidance = false, // Disable for simpler analysis
                FloorLevelTolerance = 3.0f
            };
            var movementController = new MovementController(physicsWorld, pathfinder, agentConfig, pathfindingConfig);
            
            // Spawn Agent-3 - Use navmesh to find proper spawn position
            var agentHeight = 1.8f;
            var agentRadius = 0.5f;
            var agentMass = 1.0f;
            
            // Find the actual navmesh surface at spawn XZ coordinates
            var pathfindingService = new PathfindingService(pathfinder, agentConfig);
            var spawnSearchPoint = new Vector3(agent3Start.X, agent3Start.Y + 5.0f, agent3Start.Z); // Search from above
            var spawnSearchExtents = new Vector3(2.0f, 10.0f, 2.0f);
            var navmeshSpawnPos = pathfindingService.FindNearestValidPosition(spawnSearchPoint, spawnSearchExtents);
            
            Vector3 spawnPosition;
            if (navmeshSpawnPos.HasValue)
            {
                // Spawn with capsule offset (half-height + radius above navmesh surface)
                var capsuleOffset = (agentHeight / 2.0f) + agentRadius;
                spawnPosition = new Vector3(navmeshSpawnPos.Value.X, navmeshSpawnPos.Value.Y + capsuleOffset, navmeshSpawnPos.Value.Z);
                Console.WriteLine($"[Spawn] NavMesh surface at ({navmeshSpawnPos.Value.X:F2}, {navmeshSpawnPos.Value.Y:F2}, {navmeshSpawnPos.Value.Z:F2})");
                Console.WriteLine($"[Spawn] Capsule offset: {capsuleOffset:F2}m (half-height + radius)");
            }
            else
            {
                Console.WriteLine($"[Spawn] WARNING: Could not find navmesh surface, using original position");
                spawnPosition = agent3Start;
            }
            
            var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(agentRadius, agentHeight, agentMass);
            var agent3 = physicsWorld.RegisterEntityWithInertia(
                AGENT3_ENTITY_ID,
                EntityType.Player,
                spawnPosition,
                agentShape,
                agentInertia,
                isStatic: false
            );
            
            Console.WriteLine($"✓ Agent-3 spawned at ({spawnPosition.X:F2}, {spawnPosition.Y:F2}, {spawnPosition.Z:F2})");
            
            // Settling phase WITH grounding enabled
            Console.WriteLine("Settling phase (190 steps) with grounding...");
            for (int i = 0; i < 190; i++)
            {
                physicsWorld.Update(config.Timestep);
                movementController.UpdateMovement(config.Timestep); // Enable grounding during settling
                
                if (i % 50 == 0)
                {
                    var currentPos = physicsWorld.GetEntityPosition(agent3);
                    Console.WriteLine($"  [Settling {i:000}] Y={currentPos.Y:F2}");
                }
            }
            
            var settledPos = physicsWorld.GetEntityPosition(agent3);
            Console.WriteLine($"✓ Agent settled at ({settledPos.X:F2}, {settledPos.Y:F2}, {settledPos.Z:F2})");
            Console.WriteLine();
            
            // Request movement
            Console.WriteLine($"Requesting movement to goal ({agent3Goal.X:F2}, {agent3Goal.Y:F2}, {agent3Goal.Z:F2})...");
            var moveRequest = new MovementRequest(
                AGENT3_ENTITY_ID,
                agent3Goal,
                maxSpeed: 5.0f,
                agentHeight,
                agentRadius
            );
            
            var moveResponse = movementController.RequestMovement(moveRequest);
            if (!moveResponse.Success)
            {
                Console.WriteLine($"❌ Movement request failed: {moveResponse.Message}");
                return;
            }
            
            Console.WriteLine($"✓ Movement started, path length: {moveResponse.EstimatedPathLength:F2}m");
            Console.WriteLine($"  Waypoints: {moveResponse.PathResult?.Waypoints.Count}");
            
            // Analyze the generated path in detail
            if (moveResponse.PathResult != null && moveResponse.PathResult.Waypoints.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("──────────────────────────────────────────────────────────────");
                Console.WriteLine("PATH ANALYSIS: Examining DotRecast's Generated Path");
                Console.WriteLine("──────────────────────────────────────────────────────────────");
                
                var waypoints = moveResponse.PathResult.Waypoints;
                float totalVertical = 0f;
                float maxSingleClimb = 0f;
                float maxSegmentSlope = 0f;
                
                for (int i = 0; i < waypoints.Count; i++)
                {
                    var wp = waypoints[i];
                    Console.WriteLine($"  Waypoint {i}: ({wp.X:F2}, {wp.Y:F2}, {wp.Z:F2})");
                    
                    if (i > 0)
                    {
                        var prev = waypoints[i - 1];
                        var deltaY = wp.Y - prev.Y;
                        var deltaXZ = MathF.Sqrt((wp.X - prev.X) * (wp.X - prev.X) + (wp.Z - prev.Z) * (wp.Z - prev.Z));
                        var segmentSlope = deltaXZ > 0.01f ? (float)(Math.Atan(Math.Abs(deltaY) / deltaXZ) * 180.0 / Math.PI) : 90f;
                        
                        Console.WriteLine($"    Δ from prev: Y={deltaY:F2}m, XZ={deltaXZ:F2}m, Slope={segmentSlope:F1}°");
                        
                        totalVertical += Math.Abs(deltaY);
                        maxSingleClimb = Math.Max(maxSingleClimb, Math.Abs(deltaY));
                        maxSegmentSlope = Math.Max(maxSegmentSlope, segmentSlope);
                    }
                }
                
                Console.WriteLine();
                Console.WriteLine($"Path Summary:");
                Console.WriteLine($"  Total vertical change: {totalVertical:F2}m");
                Console.WriteLine($"  Max single climb between waypoints: {maxSingleClimb:F2}m");
                Console.WriteLine($"  Max segment slope: {maxSegmentSlope:F1}°");
                Console.WriteLine($"  Agent MaxClimb setting: {agentConfig.MaxClimb}m");
                Console.WriteLine($"  Agent MaxSlope setting: {agentConfig.MaxSlope}°");
                
                if (maxSingleClimb > agentConfig.MaxClimb)
                {
                    Console.WriteLine($"  ⚠️  Path has segment exceeding MaxClimb by {(maxSingleClimb - agentConfig.MaxClimb):F2}m");
                }
                if (maxSegmentSlope > agentConfig.MaxSlope)
                {
                    Console.WriteLine($"  ⚠️  Path has segment exceeding MaxSlope by {(maxSegmentSlope - agentConfig.MaxSlope):F1}°");
                }
                
                Console.WriteLine("──────────────────────────────────────────────────────────────");
            }
            Console.WriteLine();
            
            // Simulate with diagnostic logging
            Console.WriteLine("Simulating Agent-3 movement (max 500 steps)...");
            Console.WriteLine("Will log detailed state every 30 steps");
            Console.WriteLine();
            
            int maxSteps = 500;
            bool goalReached = false;
            bool agentFell = false;
            int fellAtStep = -1;
            
            for (int step = 0; step < maxSteps; step++)
            {
                // Update physics and movement
                physicsWorld.Update(config.Timestep);
                movementController.UpdateMovement(config.Timestep);
                
                var currentPos = physicsWorld.GetEntityPosition(agent3);
                var currentVel = physicsWorld.GetEntityVelocity(agent3);
                var characterState = movementController.GetCharacterState(agent3);
                
                // Periodic logging
                if (step % DIAGNOSTIC_SAMPLE_INTERVAL == 0)
                {
                    var groundY = currentPos.Y - (agentHeight / 2.0f + agentRadius);
                    Console.WriteLine($"[Step {step:000}] Pos:({currentPos.X:F2},{currentPos.Y:F2},{currentPos.Z:F2}) " +
                                    $"GroundY:{groundY:F2} Vel:({currentVel.X:F2},{currentVel.Y:F2},{currentVel.Z:F2}) " +
                                    $"State:{characterState}");
                }
                
                // Check if agent fell
                if (currentPos.Y < -10.0f && !agentFell)
                {
                    agentFell = true;
                    fellAtStep = step;
                    Console.WriteLine();
                    Console.WriteLine($"🚨 AGENT FELL at step {step}!");
                    Console.WriteLine($"   Position: ({currentPos.X:F2}, {currentPos.Y:F2}, {currentPos.Z:F2})");
                    Console.WriteLine($"   Velocity: ({currentVel.X:F2}, {currentVel.Y:F2}, {currentVel.Z:F2})");
                    Console.WriteLine();
                    break;
                }
                
                // Check if goal reached
                if (Vector3.Distance(currentPos, agent3Goal) < 1.0f)
                {
                    goalReached = true;
                    Console.WriteLine();
                    Console.WriteLine($"✅ GOAL REACHED at step {step}!");
                    Console.WriteLine();
                    break;
                }
            }
            
            // ═══════════════════════════════════════════════════════════
            // PHASE 1c: DIAGNOSTIC REPORTS
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 1c: DIAGNOSTIC ANALYSIS REPORTS");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            
            // Print contact diagnostics
            ContactDiagnostics.PrintSummary();
            
            // Print grounding diagnostics
            GroundingDiagnostics.PrintSummary();
            
            // ═══════════════════════════════════════════════════════════
            // SUMMARY
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("PHASE 1 DIAGNOSTIC TEST SUMMARY");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            
            if (goalReached)
            {
                Console.WriteLine("✅ Test Result: SUCCESS - Agent reached goal");
            }
            else if (agentFell)
            {
                Console.WriteLine($"❌ Test Result: FAILED - Agent fell at step {fellAtStep}");
            }
            else
            {
                Console.WriteLine("⚠️ Test Result: TIMEOUT - Agent didn't reach goal or fall");
            }
            
            Console.WriteLine();
            Console.WriteLine("Key Findings:");
            Console.WriteLine($"  - Mesh discrepancies: {report.Discrepancies.Count}");
            Console.WriteLine($"  - Ground contacts recorded: {ContactDiagnostics.GetContactHistory().Count}");
            Console.WriteLine($"  - Grounding attempts: {GroundingDiagnostics.GetGroundingHistory().Count}");
            Console.WriteLine();
            
            Console.WriteLine("Next Steps:");
            Console.WriteLine("  1. Review mesh discrepancies above");
            Console.WriteLine("  2. Analyze contact loss patterns");
            Console.WriteLine("  3. Check if grounding corrections are increasing over time");
            Console.WriteLine("  4. Proceed to Phase 2: Motor-based controller comparison");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            // Cleanup
            physicsWorld?.Dispose();
        }
    }
    
    private static string ResolvePath(string relativePath)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var worldsDir = Path.Combine(baseDir, "worlds");
        
        // Try base directory first
        var fullPath = Path.Combine(baseDir, relativePath);
        if (File.Exists(fullPath))
            return fullPath;
        
        // Try worlds subdirectory
        var fileName = Path.GetFileName(relativePath);
        fullPath = Path.Combine(worldsDir, fileName);
        if (File.Exists(fullPath))
            return fullPath;
        
        // Try going up directories to find workspace root
        var currentDir = baseDir;
        for (int i = 0; i < 5; i++)
        {
            fullPath = Path.Combine(currentDir, relativePath);
            if (File.Exists(fullPath))
                return fullPath;
            
            var parentDir = Directory.GetParent(currentDir);
            if (parentDir == null)
                break;
            currentDir = parentDir.FullName;
        }
        
        return relativePath; // Return as-is if not found
    }
}
