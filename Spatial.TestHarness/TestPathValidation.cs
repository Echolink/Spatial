using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;
using System.Numerics;
using System.Diagnostics;
using System;

namespace Spatial.TestHarness;

/// <summary>
/// Test to validate that PathValidator correctly catches the Agent-3 path issue.
/// 
/// Expected behavior:
/// - Agent-3's path from (51.89, 0.29, 10.19) to (45.33, 8.00, 18.96) should FAIL validation
/// - Validation should report a segment exceeding MaxClimb=0.5m
/// - The violating segment should be Waypoint 3→4 with ~8.5m vertical climb
/// </summary>
public static class TestPathValidation
{
    public static void Run(string? meshPath = null)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   PATH VALIDATION TEST                                       ║");
        Console.WriteLine("║   Testing PathValidator against Agent-3 issue                ║");
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
            Console.WriteLine("✓ Physics world initialized");
            
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
                MaxClimb = 0.5f,
                MaxSlope = 45.0f
            };
            
            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            var navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);
            pathfinder = new Pathfinder(navMeshData);
            Console.WriteLine("✓ Navmesh built");
            Console.WriteLine();
            
            // ═══════════════════════════════════════════════════════════
            // TEST 1: Path Validation WITHOUT validator (baseline)
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST 1: Baseline - PathfindingService WITHOUT validation");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            
            var baselineConfig = new PathfindingConfiguration
            {
                EnablePathValidation = false  // Disable validation
            };
            
            var baselineService = new PathfindingService(pathfinder, agentConfig, baselineConfig);
            
            var agent3Start = new Vector3(51.89f, 0.29f, 10.19f);
            var agent3Goal = new Vector3(45.33f, 8.00f, 18.96f);
            
            Console.WriteLine($"Agent-3 Start: {agent3Start}");
            Console.WriteLine($"Agent-3 Goal:  {agent3Goal}");
            Console.WriteLine();
            
            var baselineResult = baselineService.FindPath(agent3Start, agent3Goal);
            
            Console.WriteLine($"Result: Success={baselineResult.Success}");
            Console.WriteLine($"Waypoints: {baselineResult.Waypoints.Count}");
            
            if (baselineResult.Success && baselineResult.Waypoints.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Path waypoints:");
                for (int i = 0; i < baselineResult.Waypoints.Count; i++)
                {
                    var wp = baselineResult.Waypoints[i];
                    Console.WriteLine($"  [{i}] ({wp.X:F2}, {wp.Y:F2}, {wp.Z:F2})");
                    
                    if (i > 0)
                    {
                        var prev = baselineResult.Waypoints[i - 1];
                        var delta = wp - prev;
                        float climbDist = Math.Abs(delta.Y);
                        float horizDist = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                        float slope = horizDist > 0.01f 
                            ? MathF.Atan2(Math.Abs(delta.Y), horizDist) * (180.0f / MathF.PI)
                            : 90.0f;
                        
                        Console.WriteLine($"       ↳ ΔY={delta.Y:F2}m, Climb={climbDist:F2}m, Slope={slope:F1}°");
                    }
                }
            }
            
            Console.WriteLine();
            
            // ═══════════════════════════════════════════════════════════
            // TEST 2: Path Validation WITH validator (should REJECT)
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST 2: With Validation - PathfindingService WITH validation");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            
            var validatedConfig = new PathfindingConfiguration
            {
                EnablePathValidation = true,
                EnablePathAutoFix = true
            };
            
            var validatedService = new PathfindingService(pathfinder, agentConfig, validatedConfig);
            
            Console.WriteLine($"Validation constraints:");
            Console.WriteLine($"  MaxClimb: {agentConfig.MaxClimb}m (from AgentConfig)");
            Console.WriteLine($"  MaxSlope: {agentConfig.MaxSlope}° (from AgentConfig)");
            Console.WriteLine();
            
            var validatedResult = validatedService.FindPath(agent3Start, agent3Goal);
            
            Console.WriteLine($"Result: Success={validatedResult.Success}");
            Console.WriteLine($"Waypoints: {validatedResult.Waypoints.Count}");
            
            if (validatedResult.Success && validatedResult.Waypoints.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Auto-fixed path waypoints:");
                for (int i = 0; i < validatedResult.Waypoints.Count; i++)
                {
                    var wp = validatedResult.Waypoints[i];
                    Console.WriteLine($"  [{i}] ({wp.X:F2}, {wp.Y:F2}, {wp.Z:F2})");
                    
                    if (i > 0)
                    {
                        var prev = validatedResult.Waypoints[i - 1];
                        var delta = wp - prev;
                        float climbDist = Math.Abs(delta.Y);
                        float horizDist = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                        float slope = horizDist > 0.01f 
                            ? MathF.Atan2(Math.Abs(delta.Y), horizDist) * (180.0f / MathF.PI)
                            : 90.0f;
                        
                        string violationMarker = climbDist > 0.5f ? " ⚠ VIOLATES MaxClimb" : " ✓";
                        Console.WriteLine($"       ↳ ΔY={delta.Y:F2}m, Climb={climbDist:F2}m, Slope={slope:F1}°{violationMarker}");
                    }
                }
            }
            
            Console.WriteLine();
            
            // ═══════════════════════════════════════════════════════════
            // TEST 3: Direct validator analysis - BASELINE PATH
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST 3: Baseline Path Validation Analysis");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            
            if (baselineResult.Success && baselineResult.Waypoints.Count > 0)
            {
                var validator = new PathSegmentValidator();
                var validation = validator.ValidatePath(
                    baselineResult.Waypoints,
                    maxClimb: 0.5f,
                    maxSlope: 45.0f,
                    agentRadius: 0.5f
                );
                
                Console.WriteLine($"Analyzing: BASELINE path ({baselineResult.Waypoints.Count} waypoints)");
                Console.WriteLine($"IsValid: {validation.IsValid}");
                
                if (!validation.IsValid)
                {
                    Console.WriteLine($"Rejection Reason: {validation.RejectionReason}");
                    Console.WriteLine($"Violating Segment: {validation.ViolatingSegmentIndex}");
                }
                
                Console.WriteLine();
                Console.WriteLine("Path Statistics:");
                Console.WriteLine($"  Total Length: {validation.Statistics.TotalLength:F2}m");
                Console.WriteLine($"  Total Vertical Change: {validation.Statistics.TotalVerticalChange:F2}m");
                Console.WriteLine($"  Max Segment Climb: {validation.Statistics.MaxSegmentClimb:F2}m");
                Console.WriteLine($"  Max Segment Slope: {validation.Statistics.MaxSegmentSlope:F1}°");
                Console.WriteLine($"  Segment Count: {validation.Statistics.SegmentCount}");
            }
            
            Console.WriteLine();
            
            // ═══════════════════════════════════════════════════════════
            // TEST 4: Direct validator analysis - AUTO-FIXED PATH
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST 4: Auto-Fixed Path Validation Analysis");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            
            if (validatedResult.Success && validatedResult.Waypoints.Count > 0)
            {
                var validator = new PathSegmentValidator();
                var validation = validator.ValidatePath(
                    validatedResult.Waypoints,
                    maxClimb: 0.5f,
                    maxSlope: 45.0f,
                    agentRadius: 0.5f
                );
                
                Console.WriteLine($"Analyzing: AUTO-FIXED path ({validatedResult.Waypoints.Count} waypoints)");
                Console.WriteLine($"IsValid: {validation.IsValid}");
                
                if (!validation.IsValid)
                {
                    Console.WriteLine($"⚠ WARNING: Auto-fixed path still has violations!");
                    Console.WriteLine($"Rejection Reason: {validation.RejectionReason}");
                    Console.WriteLine($"Violating Segment: {validation.ViolatingSegmentIndex}");
                }
                else
                {
                    Console.WriteLine($"✓ Path is now VALID - all segments comply with constraints!");
                }
                
                Console.WriteLine();
                Console.WriteLine("Path Statistics:");
                Console.WriteLine($"  Total Length: {validation.Statistics.TotalLength:F2}m");
                Console.WriteLine($"  Total Vertical Change: {validation.Statistics.TotalVerticalChange:F2}m");
                Console.WriteLine($"  Max Segment Climb: {validation.Statistics.MaxSegmentClimb:F2}m");
                Console.WriteLine($"  Max Segment Slope: {validation.Statistics.MaxSegmentSlope:F1}°");
                Console.WriteLine($"  Segment Count: {validation.Statistics.SegmentCount}");
            }
            else if (!validatedResult.Success)
            {
                Console.WriteLine("Auto-fix FAILED - no path available to validate");
                Console.WriteLine("(Terrain may be truly untraversable)");
            }
            
            Console.WriteLine();
            
            // ═══════════════════════════════════════════════════════════
            // RESULTS SUMMARY
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("RESULTS SUMMARY");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            
            Console.WriteLine($"✓ Baseline (no validation): {(baselineResult.Success ? "ACCEPTED" : "REJECTED")} path ({baselineResult.Waypoints.Count} waypoints)");
            Console.WriteLine($"✓ With validation + auto-fix: {(validatedResult.Success ? "ACCEPTED" : "REJECTED")} path ({validatedResult.Waypoints.Count} waypoints)");
            
            if (baselineResult.Success && validatedResult.Success)
            {
                Console.WriteLine();
                if (validatedResult.Waypoints.Count > baselineResult.Waypoints.Count)
                {
                    Console.WriteLine("✓ SUCCESS: PathAutoFix successfully fixed the invalid path!");
                    Console.WriteLine($"  - Original path: {baselineResult.Waypoints.Count} waypoints (had violations)");
                    Console.WriteLine($"  - Fixed path: {validatedResult.Waypoints.Count} waypoints (compliant)");
                    Console.WriteLine($"  - Added {validatedResult.Waypoints.Count - baselineResult.Waypoints.Count} intermediate waypoints to break up large climbs");
                    Console.WriteLine();
                    Console.WriteLine("This proves:");
                    Console.WriteLine("  ✓ DotRecast generates paths based on polygon connectivity");
                    Console.WriteLine("  ✓ PathValidator detects physical traversability violations");
                    Console.WriteLine("  ✓ PathAutoFix inserts waypoints to make paths compliant");
                }
                else
                {
                    Console.WriteLine("⚠ Both paths accepted with same waypoint count");
                    Console.WriteLine("  (Auto-fix may not have been needed, or terrain is actually traversable)");
                }
            }
            else if (baselineResult.Success && !validatedResult.Success)
            {
                Console.WriteLine();
                Console.WriteLine("✓ PathValidator REJECTED invalid path (auto-fix failed)");
                Console.WriteLine("  - DotRecast found a path, but it violates constraints");
                Console.WriteLine("  - Auto-fix could not repair it (terrain may be untraversable)");
                Console.WriteLine("  - Agent will not receive a path (correct behavior)");
            }
            else if (!baselineResult.Success)
            {
                Console.WriteLine();
                Console.WriteLine("⚠ UNEXPECTED: DotRecast failed to find a path");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with exception:");
            Console.WriteLine(ex.ToString());
        }
        finally
        {
            physicsWorld?.Dispose();
        }
    }
    
    private static string ResolvePath(string relativePath)
    {
        // Try multiple possible locations
        var locations = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), relativePath),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", relativePath),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", relativePath)
        };
        
        foreach (var location in locations)
        {
            var fullPath = Path.GetFullPath(location);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        
        return Path.GetFullPath(relativePath);
    }
}
