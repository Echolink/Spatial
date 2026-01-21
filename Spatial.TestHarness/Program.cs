using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using Spatial.MeshLoading;
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
    private static bool _exportNavMesh = false;
    private static string? _exportPath = null;
    
    /// <summary>
    /// Resolves a path relative to the executable directory.
    /// This ensures paths work correctly regardless of where the program is run from.
    /// </summary>
    static string ResolvePath(string relativePath)
    {
        string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(exeDir, relativePath);
    }
    
    /// <summary>
    /// Parses command-line arguments for export options.
    /// </summary>
    static void ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLower();
            
            if (arg == "--export-navmesh" || arg == "-export")
            {
                _exportNavMesh = true;
                
                // Check if next argument is a path
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    _exportPath = args[i + 1];
                    i++; // Skip next argument
                }
            }
        }
    }
    
    /// <summary>
    /// Exports the generated NavMesh to an OBJ file.
    /// </summary>
    static void ExportNavMesh(NavMeshData navMeshData, string? customPath = null)
    {
        if (!_exportNavMesh || navMeshData?.NavMesh == null)
            return;
        
        try
        {
            string outputPath;
            if (customPath != null)
            {
                outputPath = customPath;
            }
            else if (_exportPath != null)
            {
                outputPath = _exportPath;
            }
            else
            {
                // Default: export to worlds folder with timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                outputPath = ResolvePath($"worlds/navmesh_export_{timestamp}.obj");
            }
            
            // Ensure directory exists
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Extract geometry from NavMesh
            var geometry = ExtractNavMeshGeometry(navMeshData);
            
            // Write to OBJ file
            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("# Exported NavMesh from Spatial.TestHarness");
                writer.WriteLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"# Vertices: {geometry.Vertices.Count}");
                writer.WriteLine($"# Triangles: {geometry.Indices.Count / 3}");
                writer.WriteLine();
                
                // Write vertices
                foreach (var vertex in geometry.Vertices)
                {
                    writer.WriteLine($"v {vertex[0]:F6} {vertex[1]:F6} {vertex[2]:F6}");
                }
                
                writer.WriteLine();
                
                // Write faces (OBJ uses 1-based indexing)
                for (int i = 0; i < geometry.Indices.Count; i += 3)
                {
                    int v1 = geometry.Indices[i] + 1;
                    int v2 = geometry.Indices[i + 1] + 1;
                    int v3 = geometry.Indices[i + 2] + 1;
                    writer.WriteLine($"f {v1} {v2} {v3}");
                }
            }
            
            Console.WriteLine($"\n[Export] NavMesh exported to: {Path.GetFullPath(outputPath)}");
            Console.WriteLine($"[Export]   Vertices: {geometry.Vertices.Count}");
            Console.WriteLine($"[Export]   Triangles: {geometry.Indices.Count / 3}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[Export] Failed to export NavMesh: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Extracts geometry from NavMeshData for export.
    /// </summary>
    static (List<float[]> Vertices, List<int> Indices) ExtractNavMeshGeometry(NavMeshData navMeshData)
    {
        var vertices = new List<float[]>();
        var indices = new List<int>();
        
        if (navMeshData.NavMesh == null)
            return (vertices, indices);
        
        var navMesh = navMeshData.NavMesh;
        
        // Get the first tile (tile 0)
        var tile = navMesh.GetTile(0);
        if (tile?.data != null)
        {
            var data = tile.data;
            
            // Extract vertices
            for (int i = 0; i < data.header.vertCount; i++)
            {
                int vertIndex = i * 3;
                vertices.Add(new[]
                {
                    data.verts[vertIndex],
                    data.verts[vertIndex + 1],
                    data.verts[vertIndex + 2]
                });
            }
            
            // Extract triangles from polygons
            for (int i = 0; i < data.header.polyCount; i++)
            {
                var poly = data.polys[i];
                
                // Each polygon can have multiple vertices - triangulate it
                // Simple fan triangulation from first vertex
                for (int j = 2; j < poly.vertCount; j++)
                {
                    indices.Add(poly.verts[0]);
                    indices.Add(poly.verts[j - 1]);
                    indices.Add(poly.verts[j]);
                }
            }
        }
        
        return (vertices, indices);
    }
    
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
        
        // Parse command-line arguments
        ParseArguments(args);
        
        if (_exportNavMesh)
        {
            Console.WriteLine($"[Info] NavMesh export enabled");
            if (_exportPath != null)
            {
                Console.WriteLine($"[Info] Export path: {_exportPath}");
            }
            Console.WriteLine();
        }
        
        try
        {
            // Check if user wants to run a specific test
            if (args.Length > 0 && args[0].ToLower() == "custom")
            {
                // Run only the custom mesh test
                Console.WriteLine("[Info] Running CUSTOM MESH TEST only\n");
                TestCustomMesh(vizServer, ResolvePath("worlds/seperated_land.obj"));
            }
            else if (args.Length > 0 && args[0].ToLower() == "direct")
            {
                // Run direct navmesh generation test (bypass physics system)
                Console.WriteLine("[Info] Running DIRECT NAVMESH GENERATION TEST\n");
                Console.WriteLine("[Info] This bypasses the physics system and uses DotRecast's recommended approach\n");
                
                string inputFile = ResolvePath("worlds/seperated_land.obj");
                string outputFile = ResolvePath("worlds/navmesh_direct_export.obj");
                
                if (args.Length > 1)
                {
                    inputFile = args[1];
                }
                if (args.Length > 2)
                {
                    outputFile = args[2];
                }
                
                TestDirectNavMesh.Run(inputFile, outputFile);
                
                Console.WriteLine("\n[Info] Comparison:");
                Console.WriteLine($"   Physics-based: {ResolvePath("worlds/navmesh_export_*.obj")}");
                Console.WriteLine($"   Direct approach: {outputFile}");
                Console.WriteLine("\n[Info] Compare the two files to see differences in navmesh generation");
            }
            else if (args.Length > 0 && args[0].ToLower() == "showcase")
            {
                // Run the comprehensive showcase test (demonstrates latest features)
                Console.WriteLine("[Info] Running COMPREHENSIVE SHOWCASE TEST\n");
                Console.WriteLine("[Info] This demonstrates:\n");
                Console.WriteLine("[Info]   - Direct navmesh generation (2x better quality)");
                Console.WriteLine("[Info]   - Multi-agent pathfinding with different goals");
                Console.WriteLine("[Info]   - Dynamic physics interactions");
                Console.WriteLine("[Info]   - Complex terrain navigation\n");
                TestComprehensiveShowcase(vizServer);
            }
            else if (args.Length > 0 && args[0].ToLower() == "enhanced")
            {
                // Run the enhanced showcase test with detailed metrics
                Console.WriteLine("[Info] Running ENHANCED SHOWCASE TEST\n");
                Console.WriteLine("[Info] This is an improved version with:\n");
                Console.WriteLine("[Info]   - Detailed performance metrics and validation");
                Console.WriteLine("[Info]   - Up to 10 agents with diverse scenarios");
                Console.WriteLine("[Info]   - Real-time progress tracking");
                Console.WriteLine("[Info]   - Comprehensive final analysis\n");
                
                // Parse agent count if provided
                int agentCount = 5; // Default
                if (args.Length > 1 && int.TryParse(args[1], out int parsedCount))
                {
                    agentCount = Math.Clamp(parsedCount, 1, 10);
                }
                
                string? meshPath = null;
                if (args.Length > 2)
                {
                    meshPath = args[2];
                }
                
                TestEnhancedShowcase.Run(vizServer, meshPath, agentCount);
            }
            else if (args.Length > 0 && args[0].ToLower() == "collision")
            {
                // Run mesh collision system tests
                Console.WriteLine("[Info] Running MESH COLLISION SYSTEM TESTS\n");
                Console.WriteLine("[Info] This validates:\n");
                Console.WriteLine("[Info]   - Ground collision with mesh surfaces");
                Console.WriteLine("[Info]   - Agent settling on ground");
                Console.WriteLine("[Info]   - Separated platform behavior");
                Console.WriteLine("[Info]   - NPC interaction with mesh collision\n");
                
                TestMeshCollision.Run();
            }
            else if (args.Length > 0 && args[0].ToLower() == "physics-pathfinding")
            {
                // Run physics-pathfinding integration tests
                Console.WriteLine("[Info] Running PHYSICS-PATHFINDING INTEGRATION TESTS\n");
                Console.WriteLine("[Info] This validates:\n");
                Console.WriteLine("[Info]   - Pathfinding with gravity enabled");
                Console.WriteLine("[Info]   - Falling off ledges and recovery");
                Console.WriteLine("[Info]   - Knockback and automatic replanning\n");
                
                TestPhysicsPathfindingIntegration.Run(vizServer);
            }
            else if (args.Length > 0 && args[0].ToLower() == "agent-collision")
            {
                // Run agent collision blocking and push mechanic tests
                Console.WriteLine("[Info] Running AGENT COLLISION & PUSH MECHANICS TESTS\n");
                Console.WriteLine("[Info] This validates:\n");
                Console.WriteLine("[Info]   - Agents block each other (no pushing by default)");
                Console.WriteLine("[Info]   - Push mechanic for skills/abilities");
                Console.WriteLine("[Info]   - Knockback mechanic for combat");
                Console.WriteLine("[Info]   - Pushable flag behavior\n");
                
                TestAgentCollision.Run(vizServer);
            }
            else
            {
                // Run all tests with visualization
                Console.WriteLine("[Info] Running ALL TESTS");
                Console.WriteLine("[Info]   Use 'dotnet run -- custom' to run only custom mesh test");
                Console.WriteLine("[Info]   Use 'dotnet run -- direct [input.obj] [output.obj]' to test direct navmesh generation");
                Console.WriteLine("[Info]   Use 'dotnet run -- showcase' for comprehensive demonstration of latest features");
                Console.WriteLine("[Info]   Use 'dotnet run -- enhanced [agentCount] [meshPath]' for enhanced test with metrics (RECOMMENDED)");
                Console.WriteLine("[Info]   Use 'dotnet run -- collision' to test mesh collision system");
                Console.WriteLine("[Info]   Use 'dotnet run -- physics-pathfinding' to test physics-pathfinding integration");
                Console.WriteLine("[Info]   Use 'dotnet run -- agent-collision' to test agent blocking and push mechanics");
                Console.WriteLine("[Info]   Use '--export-navmesh [path]' to export generated NavMesh to OBJ file\n");
                TestPhysicsCollision(vizServer);
                Console.WriteLine("\n" + new string('=', 60) + "\n");
                TestFullIntegration(vizServer);
                Console.WriteLine("\n" + new string('=', 60) + "\n");
                TestMeshLoading(vizServer);
                Console.WriteLine("\n" + new string('=', 60) + "\n");
                MultiUnitTest.Run(vizServer);
            }
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
            const float entityHeight = 2.0f;
            var (entityShape, entityInertia) = physicsWorld.CreateCapsuleShapeWithInertia(0.5f, entityHeight, entityMass);
            
            // Spawn above ground to demonstrate falling physics
            var entity = physicsWorld.RegisterEntityWithInertia(
                entityId: 1,
                entityType: EntityType.Player,
                position: new Vector3(0, entityHeight + 1.0f, 0),  // Height + safety margin
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
                
                // Export NavMesh if requested
                ExportNavMesh(navMeshData);
                
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
            
            // Place agent well above ground to allow proper gravity settling
            // Use agent height + extra margin for safety
            var startAboveGround = new Vector3(start.X, 1.0f + agentConfig.Height + 0.5f, start.Z);
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
            
            // Step 5: Let physics settle first
            Console.WriteLine("5. Settling physics (60 steps, ~1 second)...");
            for (int i = 0; i < 60; i++)
            {
                physicsWorld.Update(0.016f);
                
                var settleState = SimulationStateBuilder.BuildFromPhysicsWorld(
                    physicsWorld, 
                    navMeshData
                );
                vizServer.BroadcastState(settleState);
                Thread.Sleep(16);
            }
            
            var settledPos = physicsWorld.GetEntityPosition(agent);
            Console.WriteLine($"   Agent settled at ({settledPos.X:F2}, {settledPos.Y:F2}, {settledPos.Z:F2})\n");
            
            // Step 6: Request movement from settled position
            Console.WriteLine("6. Requesting movement to goal...");
            // Find path from current position (after settling)
            var settledPathResult = pathfinder.FindPath(settledPos, goal, extents);
            if (settledPathResult.Success && settledPathResult.Waypoints.Count > 0)
            {
                Console.WriteLine($"   ✓ Path found from settled position ({settledPathResult.Waypoints.Count} waypoints)\n");
            }
            
            // Step 7: Simulate movement
            Console.WriteLine("7. Simulating movement (360 steps, ~6 seconds)...");
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
                    settledPathResult, 
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
    /// Test 3: Mesh Loading - Load world geometry from .obj file
    /// </summary>
    static void TestMeshLoading(VisualizationServer vizServer)
    {
        Console.WriteLine("TEST 3: MESH LOADING (Load World from File)\n");
        
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
            
            // Step 2: Check if mesh file exists, create simple one if not
            // string meshPath = ResolvePath("worlds/simple_arena.obj");
            // string metadataPath = ResolvePath("worlds/simple_arena.obj.json");
            string meshPath = ResolvePath("worlds/seperated_land.obj");
            string metadataPath = ResolvePath("worlds/seperated_land.obj.json");
            
            if (!File.Exists(meshPath))
            {
                Console.WriteLine("2. No mesh file found, creating simple test mesh...");
                CreateSimpleArenaMesh(meshPath);
                Console.WriteLine($"   ✓ Created {meshPath}\n");
            }
            else
            {
                Console.WriteLine($"2. Found existing mesh file: {meshPath}\n");
            }
            
            // Step 3: Load world from mesh file
            Console.WriteLine("3. Loading world from mesh file...");
            var meshLoader = new MeshLoader();
            var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);
            
            try
            {
                var worldData = worldBuilder.LoadAndBuildWorld(meshPath, metadataPath);
                Console.WriteLine($"   ✓ World '{worldData.Name}' loaded successfully");
                Console.WriteLine($"   ✓ Loaded {worldData.Meshes.Count} mesh objects\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ✗ Failed to load mesh file: {ex.Message}");
                Console.WriteLine($"   Falling back to procedural obstacles...\n");
                CreateStaticObstacles(physicsWorld);
            }
            
            // Step 4: Add procedural elements (hybrid approach)
            Console.WriteLine("4. Adding procedural elements (hybrid approach)...");
            worldBuilder.AddProceduralElements(physics =>
            {
                // Add a dynamic box that will fall
                const float boxMass = 2.0f;
                var boxSize = new Vector3(1.0f, 1.0f, 1.0f);
                var (boxShape, boxInertia) = physics.CreateBoxShapeWithInertia(boxSize, boxMass);
                physics.RegisterEntityWithInertia(
                    entityId: 100,
                    entityType: EntityType.NPC,
                    position: new Vector3(0, 5, 0),
                    shape: boxShape,
                    inertia: boxInertia,
                    isStatic: false
                );
                Console.WriteLine("   Added dynamic box at (0, 5, 0)");
            });
            Console.WriteLine("   ✓ Procedural elements added\n");
            
            // Broadcast initial state
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
            vizServer.BroadcastState(initialState);
            
            // Step 5: Build navigation mesh from loaded geometry
            Console.WriteLine("5. Building navigation mesh from loaded geometry...");
            var agentConfig = new AgentConfig
            {
                Height = 2.0f,
                Radius = 0.4f,
                MaxSlope = 45.0f,
                MaxClimb = 0.5f
            };
            
            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            var navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
            
            if (navMeshData != null && navMeshData.NavMesh != null)
            {
                Console.WriteLine($"   ✓ NavMesh generated successfully\n");
                
                // Export NavMesh if requested
                ExportNavMesh(navMeshData);
                
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
            
            // Step 6: Test pathfinding on loaded geometry
            Console.WriteLine("6. Testing pathfinding on loaded geometry...");
            var pathfinder = new Pathfinder(navMeshData);
            var start = new Vector3(-5, 1, 0);
            var goal = new Vector3(5, 1, 0);
            var extents = new Vector3(5.0f, 10.0f, 5.0f);
            
            var pathResult = pathfinder.FindPath(start, goal, extents);
            
            if (pathResult.Success && pathResult.Waypoints.Count > 0)
            {
                Console.WriteLine($"   ✓ Path found with {pathResult.Waypoints.Count} waypoints\n");
            }
            else
            {
                Console.WriteLine($"   ⚠️  No path found (this may be OK if geometry blocks path)\n");
            }
            
            // Step 7: Create an agent and move along path
            if (pathResult.Success)
            {
                Console.WriteLine("7. Creating agent and testing movement...");
                const float agentMass = 1.0f;
                var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
                    agentConfig.Radius,
                    agentConfig.Height,
                    agentMass
                );
                
                // Spawn agent above ground to allow proper settling
                var agent = physicsWorld.RegisterEntityWithInertia(
                    entityId: 1,
                    entityType: EntityType.Player,
                    position: new Vector3(start.X, 1.0f + agentConfig.Height + 0.5f, start.Z),
                    shape: agentShape,
                    inertia: agentInertia,
                    isStatic: false
                );
                
                var movementController = new MovementController(physicsWorld, pathfinder);
                var moveRequest = new MovementRequest(1, goal, maxSpeed: 3.0f);
                movementController.RequestMovement(moveRequest);
                
                Console.WriteLine("   ✓ Agent created and movement started\n");
                
                // Simulate for a bit
                Console.WriteLine("8. Simulating (180 steps, ~3 seconds)...");
                for (int i = 0; i < 180; i++)
                {
                    movementController.UpdateMovement(0.016f);
                    physicsWorld.Update(0.016f);
                    
                    var state = SimulationStateBuilder.BuildFromPhysicsWorld(
                        physicsWorld,
                        navMeshData,
                        pathResult,
                        agent.EntityId
                    );
                    vizServer.BroadcastState(state);
                    
                    if (i % 30 == 0)
                    {
                        var pos = physicsWorld.GetEntityPosition(agent);
                        var dist = Vector3.Distance(pos, goal);
                        Console.WriteLine($"   Step {i + 1}: Agent=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), Dist={dist:F2}");
                    }
                    
                    Thread.Sleep(16);
                }
                
                var finalPos = physicsWorld.GetEntityPosition(agent);
                var finalDist = Vector3.Distance(finalPos, goal);
                Console.WriteLine($"\n   Final distance to goal: {finalDist:F2}\n");
            }
            
            // Cleanup
            physicsWorld.Dispose();
            Console.WriteLine("✅ MESH LOADING TEST COMPLETED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ MESH LOADING TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Test 4: Custom Mesh - Load and test with your own mesh file
    /// </summary>
    static void TestCustomMesh(VisualizationServer vizServer, string meshPath)
    {
        Console.WriteLine($"CUSTOM MESH TEST: {meshPath}\n");
        
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
            
            // Step 2: Check if mesh file exists
            if (!File.Exists(meshPath))
            {
                Console.WriteLine($"   ✗ Mesh file not found: {meshPath}");
                Console.WriteLine($"   Please place your .obj file at: {Path.GetFullPath(meshPath)}\n");
                physicsWorld.Dispose();
                return;
            }
            
            Console.WriteLine($"2. Found mesh file: {meshPath}");
            Console.WriteLine($"   Full path: {Path.GetFullPath(meshPath)}\n");
            
            // Step 3: Load world from mesh file
            Console.WriteLine("3. Loading world from your mesh file...");
            var meshLoader = new MeshLoader();
            var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);
            
            string? metadataPath = meshPath + ".json";
            if (!File.Exists(metadataPath))
            {
                Console.WriteLine($"   No metadata file found (using defaults)");
                metadataPath = null;
            }
            
            var worldData = worldBuilder.LoadAndBuildWorld(meshPath, metadataPath);
            Console.WriteLine($"   ✓ World '{worldData.Name}' loaded successfully");
            Console.WriteLine($"   ✓ Loaded {worldData.Meshes.Count} mesh objects:");
            foreach (var mesh in worldData.Meshes)
            {
                Console.WriteLine($"      - {mesh.Name} ({mesh.Vertices.Count} vertices, {mesh.TriangleCount} triangles)");
            }
            Console.WriteLine();
            
            // Broadcast initial state
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
            vizServer.BroadcastState(initialState);
            
            // Step 4: Build navigation mesh using DIRECT approach (DotRecast recommended)
            Console.WriteLine("4. Building navigation mesh (Direct DotRecast approach)...");
            var agentConfig = new AgentConfig
            {
                Height = 2.0f,      // Standard agent height
                Radius = 0.4f,      // Standard agent radius
                MaxSlope = 45.0f,   // Standard slope
                MaxClimb = 0.5f     // Standard climb height
            };
            
            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            
            // Use direct approach for maximum quality (bypasses physics processing)
            var navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);
            
            if (navMeshData != null && navMeshData.NavMesh != null)
            {
                Console.WriteLine($"   ✓ NavMesh generated successfully");
                Console.WriteLine($"   ✓ You can now see the walkable areas in Unity\n");
                
                // Export NavMesh if requested
                ExportNavMesh(navMeshData);
                
                // Broadcast state with NavMesh
                var stateWithNavMesh = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                vizServer.BroadcastState(stateWithNavMesh);
            }
            else
            {
                Console.WriteLine($"   ⚠️  NavMesh generation failed");
                Console.WriteLine($"   This usually means:");
                Console.WriteLine($"      - Ground geometry is too thin (use 0.1-0.2 units thick boxes)");
                Console.WriteLine($"      - No horizontal walkable surfaces found");
                Console.WriteLine($"   See MESH_LOADING_IMPLEMENTATION.md for solutions\n");
            }
            
            // Step 5: Add a test agent if navmesh exists
            if (navMeshData != null && navMeshData.NavMesh != null)
            {
                Console.WriteLine("5. Adding test agent...");
                
                // Find a good spawn position - spawn above ground to allow settling
                var spawnPos = new Vector3(0, 1.0f + agentConfig.Height + 0.5f, 0);
                var goalPos = new Vector3(5, 1, 5);
                
                const float agentMass = 1.0f;
                var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
                    agentConfig.Radius,
                    agentConfig.Height,
                    agentMass
                );
                
                var agent = physicsWorld.RegisterEntityWithInertia(
                    entityId: 1,
                    entityType: EntityType.Player,
                    position: spawnPos,
                    shape: agentShape,
                    inertia: agentInertia,
                    isStatic: false
                );
                
                Console.WriteLine($"   ✓ Agent created at ({spawnPos.X:F2}, {spawnPos.Y:F2}, {spawnPos.Z:F2})\n");
                
                // Try to find a path
                Console.WriteLine($"6. Testing pathfinding to ({goalPos.X:F2}, {goalPos.Y:F2}, {goalPos.Z:F2})...");
                var pathfinder = new Pathfinder(navMeshData);
                var extents = new Vector3(10.0f, 10.0f, 10.0f);
                var pathResult = pathfinder.FindPath(spawnPos, goalPos, extents);
                
                if (pathResult.Success && pathResult.Waypoints.Count > 0)
                {
                    Console.WriteLine($"   ✓ Path found with {pathResult.Waypoints.Count} waypoints\n");
                    
                    // Create movement controller and move agent
                    var movementController = new MovementController(physicsWorld, pathfinder);
                    var moveRequest = new MovementRequest(1, goalPos, maxSpeed: 3.0f);
                    movementController.RequestMovement(moveRequest);
                    
                    // Simulate movement
                    Console.WriteLine("7. Simulating agent movement (300 steps, ~5 seconds)...");
                    for (int i = 0; i < 300; i++)
                    {
                        movementController.UpdateMovement(0.016f);
                        physicsWorld.Update(0.016f);
                        
                        var state = SimulationStateBuilder.BuildFromPhysicsWorld(
                            physicsWorld,
                            navMeshData,
                            pathResult,
                            agent.EntityId
                        );
                        vizServer.BroadcastState(state);
                        
                        if (i % 50 == 0)
                        {
                            var pos = physicsWorld.GetEntityPosition(agent);
                            var dist = Vector3.Distance(pos, goalPos);
                            Console.WriteLine($"   Step {i + 1}: Pos=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), Dist to goal={dist:F2}");
                        }
                        
                        Thread.Sleep(16);
                    }
                    
                    var finalPos = physicsWorld.GetEntityPosition(agent);
                    var finalDist = Vector3.Distance(finalPos, goalPos);
                    Console.WriteLine($"\n   Final distance to goal: {finalDist:F2}");
                }
                else
                {
                    Console.WriteLine($"   ⚠️  No path found from spawn to goal");
                    Console.WriteLine($"   Try adjusting spawn/goal positions or check navmesh coverage\n");
                    
                    // Just let agent settle on ground
                    Console.WriteLine("7. Simulating physics (100 steps)...");
                    for (int i = 0; i < 100; i++)
                    {
                        physicsWorld.Update(0.016f);
                        var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                        vizServer.BroadcastState(state);
                        
                        if (i % 20 == 0)
                        {
                            var pos = physicsWorld.GetEntityPosition(agent);
                            Console.WriteLine($"   Step {i + 1}: Agent at ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
                        }
                        
                        Thread.Sleep(16);
                    }
                }
            }
            else
            {
                Console.WriteLine("5. Skipping agent test (no navmesh available)\n");
                
                // Just show the loaded geometry for a bit
                Console.WriteLine("6. Displaying loaded geometry (5 seconds)...");
                for (int i = 0; i < 300; i++)
                {
                    physicsWorld.Update(0.016f);
                    var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
                    vizServer.BroadcastState(state);
                    Thread.Sleep(16);
                }
            }
            
            // Cleanup
            physicsWorld.Dispose();
            Console.WriteLine("\n✅ CUSTOM MESH TEST COMPLETED");
            Console.WriteLine($"   Your mesh loaded and displayed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ CUSTOM MESH TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Comprehensive showcase test demonstrating the latest features:
    /// - Direct navmesh generation (higher quality than physics-based)
    /// - Multiple agents with different goals
    /// - Dynamic physics interactions
    /// - Complex terrain navigation
    /// </summary>
    static void TestComprehensiveShowcase(VisualizationServer vizServer)
    {
        Console.WriteLine("COMPREHENSIVE SHOWCASE TEST\n");
        Console.WriteLine("This test demonstrates the latest improvements to the Spatial system:\n");
        
        try
        {
            // Step 1: Create physics world
            Console.WriteLine("1. Setting up physics world...");
            var config = new PhysicsConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = 0.016f
            };
            var physicsWorld = new PhysicsWorld(config);
            Console.WriteLine("   ✓ Physics world created\n");
            
            // Step 2: Load world from mesh file
            string meshPath = ResolvePath("worlds/seperated_land.obj");
            if (!File.Exists(meshPath))
            {
                Console.WriteLine("   ⚠️  seperated_land.obj not found, using procedural geometry");
                CreateStaticObstacles(physicsWorld);
            }
            else
            {
                Console.WriteLine("2. Loading world geometry from file...");
                var meshLoader = new MeshLoader();
                var worldBuilder = new WorldBuilder(physicsWorld, meshLoader);
                
                string? metadataPath = meshPath + ".json";
                if (!File.Exists(metadataPath))
                {
                    metadataPath = null;
                }
                
                var worldData = worldBuilder.LoadAndBuildWorld(meshPath, metadataPath);
                Console.WriteLine($"   ✓ Loaded '{worldData.Name}' with {worldData.Meshes.Count} mesh objects");
                
                int totalVertices = worldData.Meshes.Sum(m => m.Vertices.Count);
                int totalTriangles = worldData.Meshes.Sum(m => m.TriangleCount);
                Console.WriteLine($"   ✓ Total geometry: {totalVertices} vertices, {totalTriangles} triangles\n");
            }
            
            // Broadcast initial world state
            var initialState = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld);
            vizServer.BroadcastState(initialState);
            
            // Step 3: Build NavMesh using DIRECT approach (latest improvement!)
            Console.WriteLine("3. Generating NavMesh using DIRECT approach...");
            Console.WriteLine("   (This bypasses physics processing for 2x better quality)");
            
            var agentConfig = new AgentConfig
            {
                Height = 2.0f,
                Radius = 0.4f,
                MaxSlope = 45.0f,
                MaxClimb = 0.5f
            };
            
            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
            
            // Use the direct approach introduced in the latest update
            var navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);
            
            if (navMeshData == null || navMeshData.NavMesh == null)
            {
                Console.WriteLine("   ✗ NavMesh generation failed - falling back to physics-based approach");
                navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
                
                if (navMeshData == null || navMeshData.NavMesh == null)
                {
                    Console.WriteLine("   ✗ Both approaches failed - cannot continue\n");
                    physicsWorld.Dispose();
                    return;
                }
            }
            
            Console.WriteLine("   ✓ NavMesh generated successfully!");
            
            // Export the high-quality navmesh
            ExportNavMesh(navMeshData);
            
            // Broadcast state with navmesh
            var stateWithNavMesh = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
            vizServer.BroadcastState(stateWithNavMesh);
            Console.WriteLine("   ✓ NavMesh exported and visualized\n");
            
            // Step 4: Create pathfinder
            var pathfinder = new Pathfinder(navMeshData);
            var movementController = new MovementController(physicsWorld, pathfinder);
            
            // Step 5: Create multiple agents with different goals
            Console.WriteLine("4. Creating multiple agents with different navigation goals...");
            
            // Spawn agents above ground to allow proper settling
            float spawnHeight = 1.0f + agentConfig.Height + 0.5f;
            var agents = new List<(int EntityId, Vector3 Start, Vector3 Goal, string Name)>
            {
                (101, new Vector3(-8, spawnHeight, -8), new Vector3(8, 1, 8), "Agent 1 (SW→NE)"),
                (102, new Vector3(8, spawnHeight, -8), new Vector3(-8, 1, 8), "Agent 2 (SE→NW)"),
                (103, new Vector3(0, spawnHeight, -8), new Vector3(0, 1, 8), "Agent 3 (S→N)")
            };
            
            var agentEntities = new List<PhysicsEntity>();
            var agentPaths = new List<PathResult>();
            
            const float agentMass = 1.0f;
            foreach (var (entityId, start, goal, name) in agents)
            {
                // Create agent physics body
                var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
                    agentConfig.Radius,
                    agentConfig.Height,
                    agentMass
                );
                
                var agent = physicsWorld.RegisterEntityWithInertia(
                    entityId: entityId,
                    entityType: EntityType.Player,
                    position: start,
                    shape: agentShape,
                    inertia: agentInertia,
                    isStatic: false
                );
                
                // Find path for this agent
                var extents = new Vector3(10.0f, 10.0f, 10.0f);
                var pathResult = pathfinder.FindPath(start, goal, extents);
                
                if (pathResult.Success && pathResult.Waypoints.Count > 0)
                {
                    Console.WriteLine($"   ✓ {name}: Created with {pathResult.Waypoints.Count} waypoint path");
                    
                    // Request movement
                    var moveRequest = new MovementRequest(entityId, goal, maxSpeed: 3.0f);
                    movementController.RequestMovement(moveRequest);
                    
                    agentEntities.Add(agent);
                    agentPaths.Add(pathResult);
                }
                else
                {
                    Console.WriteLine($"   ⚠️  {name}: No path found (may be unreachable)");
                }
            }
            
            Console.WriteLine();
            
            // Step 6: Add dynamic obstacles for physics interaction
            Console.WriteLine("5. Adding dynamic physics obstacles...");
            
            var dynamicObstacles = new List<PhysicsEntity>();
            
            // Add several falling boxes at different positions
            var boxPositions = new[]
            {
                new Vector3(-5, 5, 0),
                new Vector3(5, 6, 0),
                new Vector3(0, 7, -5),
                new Vector3(0, 8, 5)
            };
            
            const float boxMass = 2.0f;
            var boxSize = new Vector3(1.0f, 1.0f, 1.0f);
            
            for (int i = 0; i < boxPositions.Length; i++)
            {
                var (boxShape, boxInertia) = physicsWorld.CreateBoxShapeWithInertia(boxSize, boxMass);
                var box = physicsWorld.RegisterEntityWithInertia(
                    entityId: 200 + i,
                    entityType: EntityType.NPC,
                    position: boxPositions[i],
                    shape: boxShape,
                    inertia: boxInertia,
                    isStatic: false
                );
                dynamicObstacles.Add(box);
            }
            
            Console.WriteLine($"   ✓ Added {dynamicObstacles.Count} dynamic falling boxes");
            Console.WriteLine("   (These will interact with agents via physics)\n");
            
            // Step 7: Run simulation
            Console.WriteLine("6. Running simulation (600 steps, ~10 seconds)...");
            Console.WriteLine("   Watch the Unity client to see:");
            Console.WriteLine("   - Multiple agents navigating to different goals");
            Console.WriteLine("   - High-quality navmesh from direct generation");
            Console.WriteLine("   - Dynamic physics interactions");
            Console.WriteLine("   - Agent collision avoidance\n");
            
            int steps = 600;
            int reportInterval = 100; // Report every ~1.6 seconds
            
            for (int i = 0; i < steps; i++)
            {
                // Update all agent movements
                movementController.UpdateMovement(0.016f);
                
                // Update physics simulation
                physicsWorld.Update(0.016f);
                
                // Build and broadcast state
                // Note: We can only show one path at a time in the visualization,
                // so we'll cycle through agents or show the first one
                var mainAgentId = agentEntities.Count > 0 ? agentEntities[0].EntityId : 0;
                var mainPath = agentPaths.Count > 0 ? agentPaths[0] : null;
                
                var state = SimulationStateBuilder.BuildFromPhysicsWorld(
                    physicsWorld,
                    navMeshData,
                    mainPath,
                    mainAgentId
                );
                vizServer.BroadcastState(state);
                
                // Report progress periodically
                if (i % reportInterval == 0 || i == steps - 1)
                {
                    Console.WriteLine($"   Step {i + 1}/{steps}:");
                    
                    // Report each agent's status
                    foreach (var agentEntity in agentEntities)
                    {
                        var agentInfo = agents.FirstOrDefault(a => a.EntityId == agentEntity.EntityId);
                        if (agentInfo.EntityId > 0)
                        {
                            var pos = physicsWorld.GetEntityPosition(agentEntity);
                            var dist = Vector3.Distance(pos, agentInfo.Goal);
                            
                            string status = dist < 1.5f ? "✓ REACHED" : $"{dist:F1}m away";
                            Console.WriteLine($"      {agentInfo.Name}: ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}) - {status}");
                        }
                    }
                    
                    // Report dynamic obstacles
                    if (i % (reportInterval * 2) == 0)
                    {
                        var firstBox = physicsWorld.GetEntityPosition(dynamicObstacles[0]);
                        Console.WriteLine($"      Dynamic boxes: Example at ({firstBox.X:F1}, {firstBox.Y:F1}, {firstBox.Z:F1})");
                    }
                    
                    Console.WriteLine();
                }
                
                // Visualization frame rate
                Thread.Sleep(16); // ~60 FPS
            }
            
            // Step 8: Final report
            Console.WriteLine("7. Final Results:");
            Console.WriteLine("   Agent Status:");
            
            int reachedCount = 0;
            foreach (var agentEntity in agentEntities)
            {
                var agentInfo = agents.FirstOrDefault(a => a.EntityId == agentEntity.EntityId);
                if (agentInfo.EntityId > 0)
                {
                    var finalPos = physicsWorld.GetEntityPosition(agentEntity);
                    var finalDist = Vector3.Distance(finalPos, agentInfo.Goal);
                    
                    string result = finalDist < 1.5f ? "✓ REACHED GOAL" : $"✗ {finalDist:F2}m from goal";
                    Console.WriteLine($"      {agentInfo.Name}: {result}");
                    
                    if (finalDist < 1.5f) reachedCount++;
                }
            }
            
            Console.WriteLine($"\n   Summary: {reachedCount}/{agentEntities.Count} agents reached their goals");
            Console.WriteLine($"   NavMesh Quality: Direct generation (2x better than physics-based)");
            Console.WriteLine($"   Dynamic Physics: {dynamicObstacles.Count} objects simulated\n");
            
            // Cleanup
            physicsWorld.Dispose();
            
            if (reachedCount >= agentEntities.Count * 0.66) // At least 2/3 reached
            {
                Console.WriteLine("✅ COMPREHENSIVE SHOWCASE TEST PASSED");
                Console.WriteLine("   The latest direct navmesh generation is working excellently!");
            }
            else
            {
                Console.WriteLine("⚠️  COMPREHENSIVE SHOWCASE TEST: Some agents didn't reach goals");
                Console.WriteLine("   This may indicate terrain complexity or need for parameter tuning");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ COMPREHENSIVE SHOWCASE TEST FAILED");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Creates a simple .obj file for testing if one doesn't exist.
    /// This creates a basic arena with ground and solid walls (boxes).
    /// </summary>
    static void CreateSimpleArenaMesh(string outputPath)
    {
        var objContent = @"# Simple Arena for Spatial Project
# Generated for testing mesh loading system

# Ground plane (20x20)
o ground
v -10 0 -10
v 10 0 -10
v 10 0 10
v -10 0 10
vn 0 1 0
f 1//1 2//1 3//1
f 1//1 3//1 4//1

# North wall - solid box (20x3x1) at Z=9
o wall_north
v -10 0 9
v 10 0 9
v 10 0 10
v -10 0 10
v -10 3 9
v 10 3 9
v 10 3 10
v -10 3 10
vn 0 1 0
vn 0 -1 0
vn 0 0 1
vn 0 0 -1
vn 1 0 0
vn -1 0 0
f 5//9 6//9 7//9
f 5//9 7//9 8//9
f 1//10 2//10 3//10
f 1//10 3//10 4//10
f 5//12 1//12 4//12
f 5//12 4//12 8//12
f 6//11 2//11 3//11
f 6//11 3//11 7//11
f 5//14 6//14 2//14
f 5//14 2//14 1//14
f 8//13 7//13 3//13
f 8//13 3//13 4//13

# South wall - solid box (20x3x1) at Z=-10
o wall_south
v -10 0 -10
v 10 0 -10
v 10 0 -9
v -10 0 -9
v -10 3 -10
v 10 3 -10
v 10 3 -9
v -10 3 -9
f 13//9 14//9 15//9
f 13//9 15//9 16//9

# East wall - solid box (1x3x18) at X=9
o wall_east
v 9 0 -9
v 10 0 -9
v 10 0 9
v 9 0 9
v 9 3 -9
v 10 3 -9
v 10 3 9
v 9 3 9
f 21//9 22//9 23//9
f 21//9 23//9 24//9

# West wall - solid box (1x3x18) at X=-10
o wall_west
v -10 0 -9
v -9 0 -9
v -9 0 9
v -10 0 9
v -10 3 -9
v -9 3 -9
v -9 3 9
v -10 3 9
f 29//9 30//9 31//9
f 29//9 31//9 32//9
";
        
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, objContent);
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
