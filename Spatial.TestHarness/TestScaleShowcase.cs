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
/// Scale stress test — validates system behavior with 50–200+ agents.
///
/// Focuses on:
/// 1. Success rate at various agent counts (10 / 25 / 50 / 100+)
/// 2. Simulation throughput (steps/second degradation under load)
/// 3. Worst-case path query time
/// 4. Replanning frequency under crowded conditions
/// 5. Physics stability (explosions / fall-throughs)
///
/// Run: dotnet run -- scale [agentCount] [meshPath]
/// Example: dotnet run -- scale 50
/// </summary>
public static class TestScaleShowcase
{
    private const int AgentIdBase = 1000;
    private const float AgentMass = 1.0f;
    private const float MoveSpeed = 4.5f;
    private const float DeltaTime = 0.008f;          // 125 FPS
    private const int SettlingSteps = 375;            // 3 seconds
    private const int SimulationSteps = 2500;         // 20 seconds

    private class AgentRecord
    {
        public int EntityId { get; init; }
        public Vector3 StartSurface { get; init; }   // NavMesh surface Y
        public Vector3 Goal { get; init; }
        public bool PathStarted { get; set; }
        public bool ReachedGoal { get; set; }
        public float TimeToGoal { get; set; }
        public float DistanceTraveled { get; set; }
        public Vector3 LastPosition { get; set; }
        public int ReplanCount { get; set; }
        public bool FellThrough { get; set; }
    }

    public static void Run(VisualizationServer vizServer, int agentCount = 50, string? meshPath = null)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║   SCALE STRESS TEST — {agentCount} AGENTS".PadRight(65) + "║");
        Console.WriteLine("║   Validates system stability and performance under load      ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        PhysicsWorld? physicsWorld = null;
        var totalSw = Stopwatch.StartNew();

        try
        {
            // ─── PHASE 1: WORLD SETUP ─────────────────────────────────────
            Console.WriteLine("── PHASE 1: WORLD SETUP ──────────────────────────────────────");
            var physicsConfig = new PhysicsConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Timestep = DeltaTime
            };
            physicsWorld = new PhysicsWorld(physicsConfig);

            meshPath ??= ResolvePath("worlds/seperated_land.obj");
            WorldData? worldData = null;

            if (File.Exists(meshPath))
            {
                var worldBuilder = new WorldBuilder(physicsWorld, new MeshLoader());
                string? metaPath = File.Exists(meshPath + ".json") ? meshPath + ".json" : null;
                worldData = worldBuilder.LoadAndBuildWorld(meshPath, metaPath);
                Console.WriteLine($"✓ Loaded: {Path.GetFileName(meshPath)}  " +
                    $"({worldData.Meshes.Sum(m => m.TriangleCount):N0} tris)");
            }
            else
            {
                Console.WriteLine("⚠ Mesh not found, using procedural geometry");
                CreateTestGeometry(physicsWorld);
            }

            vizServer.BroadcastState(SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld));
            Console.WriteLine();

            // ─── PHASE 2: NAVMESH ────────────────────────────────────────
            Console.WriteLine("── PHASE 2: NAVMESH GENERATION ───────────────────────────────");
            var agentConfig = new AgentConfig
            {
                Height = 2.0f,
                Radius = 0.4f,
                MaxSlope = 45.0f,
                MaxClimb = 0.5f
            };

            var navMeshGenerator = new NavMeshGenerator();
            var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);

            var navMeshSw = Stopwatch.StartNew();
            NavMeshData? navMeshData = worldData != null
                ? navMeshBuilder.BuildNavMeshDirect(agentConfig)
                : navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
            navMeshSw.Stop();

            if (navMeshData?.NavMesh == null)
            {
                Console.WriteLine("✗ NavMesh generation failed — aborting.");
                return;
            }

            var tile = navMeshData.NavMesh.GetTile(0);
            int tileVerts = tile?.data?.header.vertCount ?? 0;
            int tileTris = tile?.data?.polys.Sum(p => p.vertCount - 2) ?? 0;
            Console.WriteLine($"✓ NavMesh: {tileVerts:N0} verts, {tileTris:N0} tris  [{navMeshSw.Elapsed.TotalMilliseconds:F1}ms]");

            // Bounds from navmesh tile
            Vector3 navMin = Vector3.Zero, navMax = Vector3.Zero;
            if (tile?.data != null)
            {
                navMin = new Vector3(tile.data.header.bmin.X, tile.data.header.bmin.Y, tile.data.header.bmin.Z);
                navMax = new Vector3(tile.data.header.bmax.X, tile.data.header.bmax.Y, tile.data.header.bmax.Z);
            }

            vizServer.BroadcastState(SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData));
            Console.WriteLine();

            // ─── PHASE 3: AGENT GENERATION ───────────────────────────────
            Console.WriteLine($"── PHASE 3: GENERATING {agentCount} AGENTS ────────────────────────");
            var pathfinder = new Pathfinder(navMeshData);
            var pathfindingConfig = new PathfindingConfiguration();
            var pathfindingService = new PathfindingService(pathfinder, agentConfig, pathfindingConfig);

            var motorConfig = new MotorCharacterConfig
            {
                MotorStrength = 0.15f,
                HeightCorrectionStrength = 6.5f,
                MaxVerticalCorrection = 3.5f,
                HeightErrorTolerance = 0.25f,
                VerticalDamping = 0.75f,
                IdleVerticalDamping = 0.4f
            };
            var motorController = new MotorCharacterController(physicsWorld, motorConfig);
            var movementController = new MovementController(
                physicsWorld, pathfindingService, agentConfig, pathfindingConfig, motorController);

            float capsuleHalf = (agentConfig.Height / 2f) + agentConfig.Radius;

            // Generate candidate spawn/goal pairs spread across navmesh XZ bounds
            var candidates = GenerateSpawnGoalCandidates(agentCount * 3, navMin, navMax);

            // Snap to valid navmesh positions
            Console.WriteLine("  Snapping positions to NavMesh surface...");
            var agentRecords = new List<AgentRecord>();

            movementController.OnPathReplanned += (id) =>
            {
                var rec = agentRecords.FirstOrDefault(r => r.EntityId == id);
                if (rec != null) rec.ReplanCount++;
            };
            var agentEntities = new List<PhysicsEntity>();
            var spawnPositions = new Dictionary<int, Vector3>(); // entityId -> physics center

            int candidateIndex = 0;
            int failedSnaps = 0;

            var (capsuleShape, capsuleInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
                agentConfig.Radius, agentConfig.Height, AgentMass);

            foreach (var (startCandidate, goalCandidate) in candidates)
            {
                if (agentRecords.Count >= agentCount) break;

                var startSurface = SnapToNavMesh(pathfinder, startCandidate);
                var goalSurface = SnapToNavMesh(pathfinder, goalCandidate);

                if (startSurface == null || goalSurface == null)
                {
                    failedSnaps++;
                    continue;
                }

                // Skip if start and goal are basically the same point
                if (Vector3.Distance(startSurface.Value, goalSurface.Value) < 3.0f)
                    continue;

                int entityId = AgentIdBase + agentRecords.Count + 1;
                var spawnPos = new Vector3(startSurface.Value.X, startSurface.Value.Y + capsuleHalf, startSurface.Value.Z);

                var entity = physicsWorld.RegisterEntityWithInertia(
                    entityId: entityId,
                    entityType: EntityType.Player,
                    position: spawnPos,
                    shape: capsuleShape,
                    inertia: capsuleInertia,
                    isStatic: false,
                    disableGravity: false
                );

                agentEntities.Add(entity);
                spawnPositions[entityId] = spawnPos;

                agentRecords.Add(new AgentRecord
                {
                    EntityId = entityId,
                    StartSurface = startSurface.Value,
                    Goal = goalSurface.Value,
                    LastPosition = spawnPos
                });
            }

            Console.WriteLine($"✓ Created {agentRecords.Count}/{agentCount} agents  ({failedSnaps} snaps failed)");
            if (agentRecords.Count < agentCount)
            {
                Console.WriteLine($"  ⚠ Only {agentRecords.Count} valid positions found — increase candidate multiplier or try a larger mesh.");
            }
            Console.WriteLine();

            // ─── PHASE 4: SETTLING ───────────────────────────────────────
            Console.WriteLine($"── PHASE 4: SETTLING ({SettlingSteps} steps @ 125fps = 3s) ─────────────");
            for (int i = 0; i < SettlingSteps; i++)
            {
                physicsWorld.Update(DeltaTime);

                // Pin agents to spawn position during settling
                foreach (var entity in agentEntities)
                {
                    if (spawnPositions.TryGetValue(entity.EntityId, out var pin))
                    {
                        physicsWorld.SetEntityVelocity(entity, Vector3.Zero);
                        physicsWorld.SetEntityPosition(entity, pin);
                    }
                }

                if (i % 125 == 0)
                    vizServer.BroadcastState(SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData));

                Thread.Sleep(1);
            }
            Console.WriteLine("✓ Settling complete");
            Console.WriteLine();

            // ─── PHASE 5: START MOVEMENT ──────────────────────────────────
            Console.WriteLine("── PHASE 5: STARTING MOVEMENT ────────────────────────────────");
            int pathsStarted = 0;
            var failedStarts = new List<PhysicsEntity>();

            foreach (var rec in agentRecords)
            {
                var entity = agentEntities.First(e => e.EntityId == rec.EntityId);
                var moveReq = new MovementRequest(
                    rec.EntityId, rec.Goal, MoveSpeed,
                    agentHeight: agentConfig.Height,
                    agentRadius: agentConfig.Radius);

                var result = movementController.RequestMovement(moveReq);
                if (result.Success)
                {
                    rec.PathStarted = true;
                    pathsStarted++;
                }
                else
                {
                    failedStarts.Add(entity);
                }
            }

            Console.WriteLine($"✓ Movement started: {pathsStarted}/{agentRecords.Count}");
            Console.WriteLine();

            // ─── PHASE 6: SIMULATION ─────────────────────────────────────
            Console.WriteLine($"── PHASE 6: SIMULATION ({SimulationSteps} steps @ 125fps = 20s) ──────────");

            // Milestone reporting at these step counts
            var milestones = new[] { 250, 625, 1250, 1875, 2500 };
            int milestoneIdx = 0;

            // Throughput tracking
            long totalStepTime = 0;
            long worstStepTime = 0;
            var stepSw = new Stopwatch();
            int reachedCount = 0;

            for (int step = 0; step < SimulationSteps; step++)
            {
                stepSw.Restart();

                movementController.UpdateMovement(DeltaTime);
                physicsWorld.Update(DeltaTime);

                // Keep failed agents pinned
                foreach (var e in failedStarts)
                {
                    physicsWorld.SetEntityVelocity(e, Vector3.Zero);
                    if (spawnPositions.TryGetValue(e.EntityId, out var pin))
                        physicsWorld.SetEntityPosition(e, pin);
                }

                stepSw.Stop();
                long ns = stepSw.ElapsedTicks * 1_000_000_000L / Stopwatch.Frequency;
                totalStepTime += ns;
                if (ns > worstStepTime) worstStepTime = ns;

                float simTime = (step + 1) * DeltaTime;

                // Update records
                foreach (var rec in agentRecords.Where(r => r.PathStarted && !r.ReachedGoal))
                {
                    var entity = agentEntities.FirstOrDefault(e => e.EntityId == rec.EntityId);
                    if (entity == null) continue;

                    var pos = physicsWorld.GetEntityPosition(entity);

                    // Fall-through detection
                    if (pos.Y < navMin.Y - 10f)
                    {
                        rec.FellThrough = true;
                        failedStarts.Add(entity); // Pin it
                        continue;
                    }

                    rec.DistanceTraveled += Vector3.Distance(rec.LastPosition, pos);
                    rec.LastPosition = pos;

                    float distToGoal = Vector3.Distance(
                        new Vector3(pos.X, 0, pos.Z),
                        new Vector3(rec.Goal.X, 0, rec.Goal.Z));

                    if (distToGoal < 1.5f && !rec.ReachedGoal)
                    {
                        rec.ReachedGoal = true;
                        rec.TimeToGoal = simTime;
                        reachedCount++;
                    }
                }

                // Broadcast every 5 steps (25 FPS to Unity)
                if (step % 5 == 0)
                {
                    var state = SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData);
                    foreach (var entity in agentEntities)
                    {
                        var waypoints = movementController.GetWaypoints(entity.EntityId);
                        if (waypoints != null && waypoints.Count > 0)
                        {
                            var originalTarget = movementController.GetOriginalTargetPosition(entity.EntityId);
                            var snappedTarget = movementController.GetActualTargetPosition(entity.EntityId);
                            state.AgentPaths.Add(new PathData
                            {
                                EntityId = entity.EntityId,
                                Waypoints = waypoints.Select(wp => new[] { wp.X, wp.Y, wp.Z }).ToList(),
                                PathLength = 0,
                                OriginalTarget = originalTarget.HasValue ? new[] { originalTarget.Value.X, originalTarget.Value.Y, originalTarget.Value.Z } : null,
                                SnappedTarget = snappedTarget.HasValue ? new[] { snappedTarget.Value.X, snappedTarget.Value.Y, snappedTarget.Value.Z } : null
                            });
                        }
                    }
                    vizServer.BroadcastState(state);
                }

                // Milestone reports
                if (milestoneIdx < milestones.Length && step + 1 == milestones[milestoneIdx])
                {
                    PrintMilestone(step + 1, simTime, agentRecords, reachedCount, totalStepTime, worstStepTime);
                    milestoneIdx++;
                }

                Thread.Sleep(1);
            }

            // ─── PHASE 7: RESULTS ────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("── RESULTS ───────────────────────────────────────────────────");
            PrintFinalResults(agentRecords, totalStepTime, worstStepTime, SimulationSteps);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ SCALE TEST FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            physicsWorld?.Dispose();
            totalSw.Stop();
            Console.WriteLine();
            Console.WriteLine($"Total execution time: {totalSw.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine("══════════════════════════════════════════════════════════════");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static void PrintMilestone(int step, float simTime, List<AgentRecord> records,
        int reached, long totalStepNs, long worstStepNs)
    {
        int started = records.Count(r => r.PathStarted);
        int fellThrough = records.Count(r => r.FellThrough);
        float successPct = started > 0 ? (float)reached / started * 100f : 0f;
        double avgMs = started > 0 ? totalStepNs / 1_000_000.0 / step : 0;
        double worstMs = worstStepNs / 1_000_000.0;
        int replans = records.Sum(r => r.ReplanCount);

        Console.WriteLine($"  [{simTime:F1}s / step {step}]  " +
            $"Reached: {reached}/{started} ({successPct:F0}%)  " +
            $"FellThrough: {fellThrough}  " +
            $"Replans: {replans}  " +
            $"Avg step: {avgMs:F2}ms  Worst: {worstMs:F2}ms");
    }

    private static void PrintFinalResults(List<AgentRecord> records,
        long totalStepNs, long worstStepNs, int steps)
    {
        int total = records.Count;
        int started = records.Count(r => r.PathStarted);
        int reached = records.Count(r => r.ReachedGoal);
        int fellThrough = records.Count(r => r.FellThrough);
        int replans = records.Sum(r => r.ReplanCount);

        float successRate = started > 0 ? (float)reached / started * 100f : 0f;
        var successful = records.Where(r => r.ReachedGoal).ToList();
        float avgTime = successful.Any() ? successful.Average(r => r.TimeToGoal) : 0f;
        float avgDist = successful.Any() ? successful.Average(r => r.DistanceTraveled) : 0f;

        double avgStepMs = steps > 0 ? totalStepNs / 1_000_000.0 / steps : 0;
        double worstStepMs = worstStepNs / 1_000_000.0;
        double simFps = avgStepMs > 0 ? 1000.0 / avgStepMs : 0;

        Console.WriteLine($"  Agents total:          {total}");
        Console.WriteLine($"  Paths started:         {started}");
        Console.WriteLine($"  Reached goal:          {reached} ({successRate:F1}%)");
        Console.WriteLine($"  Fell through world:    {fellThrough}");
        Console.WriteLine($"  Total replans:         {replans}  (avg {(started > 0 ? (float)replans / started : 0f):F1}/agent)");
        Console.WriteLine();
        Console.WriteLine($"  Avg time to goal:      {avgTime:F2}s");
        Console.WriteLine($"  Avg distance traveled: {avgDist:F2}m");
        Console.WriteLine();
        Console.WriteLine($"  Avg step time:         {avgStepMs:F2}ms  (~{simFps:F0} sim FPS)");
        Console.WriteLine($"  Worst step time:       {worstStepMs:F2}ms");
        Console.WriteLine();

        string verdict;
        if (successRate >= 85f && worstStepMs < 50.0)
            verdict = "✅ EXCELLENT — high success rate, stable performance";
        else if (successRate >= 70f && worstStepMs < 100.0)
            verdict = "✅ GOOD — acceptable success rate and performance";
        else if (successRate >= 50f)
            verdict = "⚠️  PARTIAL — success rate below 70%, may need tuning";
        else
            verdict = "❌ POOR — low success rate, investigate pathfinding or physics stability";

        Console.WriteLine($"  Verdict: {verdict}");
    }

    /// <summary>
    /// Generates candidate (start, goal) pairs scattered across the navmesh XZ footprint.
    /// Uses a shuffled grid to distribute agents evenly rather than clustering them.
    /// </summary>
    private static List<(Vector3 Start, Vector3 Goal)> GenerateSpawnGoalCandidates(
        int count, Vector3 navMin, Vector3 navMax)
    {
        var rng = new Random(42); // Seeded for reproducibility
        var candidates = new List<(Vector3, Vector3)>(count);

        Vector3 extent = navMax - navMin;
        float baseY = navMin.Y + 0.5f;

        // 20% safety margin from edges
        float margin = 0.2f;
        Vector3 min = navMin + extent * margin;
        Vector3 max = navMax - extent * margin;

        for (int i = 0; i < count; i++)
        {
            float startX = min.X + (float)rng.NextDouble() * (max.X - min.X);
            float startZ = min.Z + (float)rng.NextDouble() * (max.Z - min.Z);
            float goalX  = min.X + (float)rng.NextDouble() * (max.X - min.X);
            float goalZ  = min.Z + (float)rng.NextDouble() * (max.Z - min.Z);

            candidates.Add((
                new Vector3(startX, baseY, startZ),
                new Vector3(goalX, baseY, goalZ)
            ));
        }

        return candidates;
    }

    private static Vector3? SnapToNavMesh(Pathfinder pathfinder, Vector3 position)
    {
        var extents = new DotRecast.Core.Numerics.RcVec3f(5f, 20f, 5f);
        var posVec = new DotRecast.Core.Numerics.RcVec3f(position.X, position.Y, position.Z);
        var filter = new DotRecast.Detour.DtQueryDefaultFilter();
        filter.SetIncludeFlags(0x01);

        var status = pathfinder.NavMeshData.Query.FindNearestPoly(
            posVec, extents, filter,
            out var polyRef, out var nearest, out _);

        if (status.Succeeded() && polyRef != 0)
            return new Vector3(nearest.X, nearest.Y, nearest.Z);

        return null;
    }

    private static void CreateTestGeometry(PhysicsWorld physicsWorld)
    {
        // Flat ground plane as fallback when no mesh file is found
        var (shape, _) = physicsWorld.CreateBoxShapeWithInertia(new Vector3(100f, 1f, 100f), 0f);
        physicsWorld.RegisterEntityWithInertia(
            entityId: 9999, entityType: EntityType.StaticObject,
            position: new Vector3(0f, -0.5f, 0f),
            shape: shape, inertia: default, isStatic: true);
    }

    private static string ResolvePath(string relativePath)
    {
        string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(exeDir, relativePath);
    }
}
