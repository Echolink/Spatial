using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;
using System.Numerics;
using System;
using System.Linq;

namespace Spatial.TestHarness;

/// <summary>
/// Diagnostic test to verify navmesh accurately represents actual mesh surface heights.
/// Compares waypoint Y positions with actual mesh surface Y at the same XZ coordinates.
/// </summary>
public static class TestNavMeshAccuracy
{
    public static void Run(string? meshPath = null)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   NAVMESH ACCURACY DIAGNOSTIC                                ║");
        Console.WriteLine("║   Comparing NavMesh Heights to Actual Mesh Surface          ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        meshPath ??= ResolvePath("worlds/seperated_land.obj");

        try
        {
            // ═══════════════════════════════════════════════════════════
            // PHASE 1: LOAD MESH AND BUILD NAVMESH
            // ═══════════════════════════════════════════════════════════
            var config = new PhysicsConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            };
            var physicsWorld = new PhysicsWorld(config);

            WorldData? worldData = null;
            if (File.Exists(meshPath))
            {
                Console.WriteLine($"Loading world from: {Path.GetFileName(meshPath)}");
                var meshLoader = new MeshLoader();
                var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);

                string? metadataPath = meshPath + ".json";
                if (!File.Exists(metadataPath)) metadataPath = null;

                worldData = worldBuilder.LoadAndBuildWorld(meshPath, metadataPath);
                
                int totalTris = worldData.Meshes.Sum(m => m.TriangleCount);
                Console.WriteLine($"✓ Loaded {worldData.Meshes.Count} meshes with {totalTris} triangles");
                
                // Print mesh Y-range
                float meshMinY = float.MaxValue;
                float meshMaxY = float.MinValue;
                
                foreach (var mesh in worldData.Meshes)
                {
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        meshMinY = Math.Min(meshMinY, mesh.Vertices[i].Y);
                        meshMaxY = Math.Max(meshMaxY, mesh.Vertices[i].Y);
                    }
                }
                
                Console.WriteLine($"✓ Mesh Y-range: [{meshMinY:F2}, {meshMaxY:F2}] (height: {meshMaxY - meshMinY:F2})");
            }

            // Build NavMesh
            var agentConfig = new AgentConfig
            {
                Height = 1.8f,
                Radius = 0.5f,
                MaxClimb = 0.5f,
                MaxSlope = 45.0f
            };

            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            
            Console.WriteLine();
            Console.WriteLine("Building NavMesh...");
            NavMeshData? navMeshData;
            if (worldData != null)
            {
                navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);
            }
            else
            {
                navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
            }

            if (navMeshData == null || navMeshData.NavMesh == null)
            {
                Console.WriteLine("✗ NavMesh generation failed!");
                return;
            }

            var pathfinder = new Pathfinder(navMeshData);
            var pathfindingService = new PathfindingService(pathfinder, agentConfig);

            // ═══════════════════════════════════════════════════════════
            // PHASE 2: TEST SPECIFIC LOCATIONS FROM AGENT-3 SCENARIO
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("TESTING WAYPOINT ACCURACY");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            // Test locations from the actual simulation
            var testPoints = new[]
            {
                new Vector3(51.89f, 0.29f, 10.19f),   // Start position
                new Vector3(45.76f, 6.74f, 11.42f),   // Mid-path waypoint
                new Vector3(45.70f, 7.05f, 12.01f),   // Another waypoint
                new Vector3(45.33f, 8.0f, 18.96f),    // Goal position
            };

            Console.WriteLine("Testing waypoint positions vs actual mesh surface:");
            Console.WriteLine();

            foreach (var testPoint in testPoints)
            {
                Console.WriteLine($"Test Point: ({testPoint.X:F2}, {testPoint.Y:F2}, {testPoint.Z:F2})");
                
                // Find nearest navmesh position
                var searchExtents = new Vector3(3.0f, 5.0f, 3.0f);
                var navmeshPos = pathfindingService.FindNearestValidPosition(testPoint, searchExtents);
                
                if (navmeshPos.HasValue)
                {
                    Console.WriteLine($"  NavMesh surface Y:      {navmeshPos.Value.Y:F3}");
                    
                    // Calculate actual mesh surface at this XZ location
                    float? actualMeshY = FindActualMeshSurfaceY(worldData, navmeshPos.Value.X, navmeshPos.Value.Z);
                    
                    if (actualMeshY.HasValue)
                    {
                        Console.WriteLine($"  Actual mesh surface Y:  {actualMeshY.Value:F3}");
                        float heightError = navmeshPos.Value.Y - actualMeshY.Value;
                        Console.WriteLine($"  Height error:           {heightError:F3}m");
                        
                        if (Math.Abs(heightError) > 0.1f)
                        {
                            Console.WriteLine($"  ⚠️ WARNING: NavMesh height differs from actual mesh by {Math.Abs(heightError):F2}m");
                        }
                        else
                        {
                            Console.WriteLine($"  ✓ Height matches within tolerance");
                        }
                        
                        // Calculate where character feet would be
                        float capsuleHalfHeight = (agentConfig.Height / 2.0f) + agentConfig.Radius;
                        float physicsCenterY = navmeshPos.Value.Y + capsuleHalfHeight;
                        float feetY = physicsCenterY - capsuleHalfHeight;
                        
                        Console.WriteLine();
                        Console.WriteLine($"  Character positioning:");
                        Console.WriteLine($"    Capsule half-height:  {capsuleHalfHeight:F3}m");
                        Console.WriteLine($"    Physics center Y:     {physicsCenterY:F3}m");
                        Console.WriteLine($"    Feet Y:               {feetY:F3}m");
                        Console.WriteLine($"    Ground Y:             {actualMeshY.Value:F3}m");
                        Console.WriteLine($"    Feet offset error:    {feetY - actualMeshY.Value:F3}m");
                        
                        if (Math.Abs(feetY - actualMeshY.Value) > 0.1f)
                        {
                            Console.WriteLine($"    ⚠️ PROBLEM: Character feet would be {feetY - actualMeshY.Value:F2}m {(feetY > actualMeshY.Value ? "above" : "below")} ground!");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠️ Could not find actual mesh surface at this XZ location");
                    }
                }
                else
                {
                    Console.WriteLine($"  ⚠️ No navmesh found at this location");
                }
                
                Console.WriteLine();
            }

            // ═══════════════════════════════════════════════════════════
            // PHASE 3: GENERATE A PATH AND CHECK ALL WAYPOINTS
            // ═══════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine("TESTING FULL PATH ACCURACY");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            var startPos = new Vector3(51.89f, 0.29f, 10.19f);
            var goalPos = new Vector3(45.33f, 8.0f, 18.96f);
            
            var pathResult = pathfinder.FindPath(startPos, goalPos, new Vector3(5.0f, 10.0f, 5.0f));
            
            if (pathResult.Success && pathResult.Waypoints.Count > 0)
            {
                Console.WriteLine($"Generated path with {pathResult.Waypoints.Count} waypoints");
                Console.WriteLine();
                
                int errorCount = 0;
                float maxError = 0;
                
                for (int i = 0; i < Math.Min(10, pathResult.Waypoints.Count); i++)
                {
                    var waypoint = pathResult.Waypoints[i];
                    float? actualY = FindActualMeshSurfaceY(worldData, waypoint.X, waypoint.Z);
                    
                    if (actualY.HasValue)
                    {
                        float error = waypoint.Y - actualY.Value;
                        maxError = Math.Max(maxError, Math.Abs(error));
                        
                        string status = Math.Abs(error) > 0.1f ? "⚠️" : "✓";
                        Console.WriteLine($"  [{i}] WP=({waypoint.X:F2}, {waypoint.Y:F2}, {waypoint.Z:F2}) " +
                                        $"Mesh={actualY.Value:F2} Error={error:F3}m {status}");
                        
                        if (Math.Abs(error) > 0.1f)
                        {
                            errorCount++;
                        }
                    }
                }
                
                Console.WriteLine();
                Console.WriteLine($"Path accuracy summary:");
                Console.WriteLine($"  Waypoints checked: {Math.Min(10, pathResult.Waypoints.Count)}");
                Console.WriteLine($"  Waypoints with error > 0.1m: {errorCount}");
                Console.WriteLine($"  Maximum height error: {maxError:F3}m");
            }
            else
            {
                Console.WriteLine("✗ Could not generate path");
            }

            physicsWorld.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// Finds the actual mesh surface Y at a given XZ location by raycasting downward.
    /// </summary>
    private static float? FindActualMeshSurfaceY(WorldData? worldData, float x, float z)
    {
        if (worldData == null || worldData.Meshes.Count == 0)
            return null;

        float? closestY = null;
        float searchStartY = 20.0f;  // Start search from above
        var rayOrigin = new Vector3(x, searchStartY, z);
        var rayDirection = new Vector3(0, -1, 0);  // Straight down

        // Check all mesh triangles for intersection
        foreach (var mesh in worldData.Meshes)
        {
            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                var v0 = mesh.Vertices[mesh.Indices[i]];
                var v1 = mesh.Vertices[mesh.Indices[i + 1]];
                var v2 = mesh.Vertices[mesh.Indices[i + 2]];

                // Ray-triangle intersection
                if (RayIntersectsTriangle(rayOrigin, rayDirection, v0, v1, v2, out float t))
                {
                    float hitY = rayOrigin.Y + rayDirection.Y * t;
                    
                    // Keep the highest surface below the ray origin (closest to ray start)
                    if (!closestY.HasValue || hitY > closestY.Value)
                    {
                        closestY = hitY;
                    }
                }
            }
        }

        return closestY;
    }

    /// <summary>
    /// Möller-Trumbore ray-triangle intersection algorithm.
    /// </summary>
    private static bool RayIntersectsTriangle(
        Vector3 rayOrigin, Vector3 rayDirection,
        Vector3 v0, Vector3 v1, Vector3 v2,
        out float t)
    {
        const float EPSILON = 0.0000001f;
        t = 0;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var h = Vector3.Cross(rayDirection, edge2);
        var a = Vector3.Dot(edge1, h);

        if (a > -EPSILON && a < EPSILON)
            return false; // Ray parallel to triangle

        var f = 1.0f / a;
        var s = rayOrigin - v0;
        var u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        var q = Vector3.Cross(s, edge1);
        var v = f * Vector3.Dot(rayDirection, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        t = f * Vector3.Dot(edge2, q);

        if (t > EPSILON) // Ray intersection
            return true;

        return false;
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
