using System.Numerics;
using System.Diagnostics;
using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using Spatial.MeshLoading;
using Spatial.MeshLoading.Data;

namespace Spatial.TestHarness;

/// <summary>
/// Scenario 3 — both features active simultaneously:
///   - Tiled NavMesh with off-mesh links (jump + teleport)
///   - Runtime obstacle spawn triggers local tile rebuild (tight footprint, not full tile)
///
/// Agents (all three use at least one off-mesh link):
///   Agent-1 [JumpApproach]: approaches jump link from a distance; obstacle blocks direct
///           approach at t=5s, forcing a replan around the obstacle before the jump
///   Agent-2 [JumpLink]:     entry → exit of the jump gap (short, clear demo)
///   Agent-3 [TeleportLink]: elevated entry → low exit (~50m horizontal)
/// </summary>
public static class TestCombinedShowcase
{
    static string ResolvePath(string rel) =>
        Path.Combine(Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location)!, rel);

    public static void Run(VisualizationServer vizServer, string? meshPath = null)
    {
        meshPath ??= ResolvePath("worlds/seperated_land_with_link.obj");

        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   COMBINED SHOWCASE: Off-Mesh Links + Obstacle Rebake        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        if (!File.Exists(meshPath))
        {
            Console.WriteLine($"✗ Mesh not found: {meshPath}");
            return;
        }

        // ── Load world geometry ───────────────────────────────────────────────
        Console.WriteLine("Loading world geometry...");
        var physicsWorld = new PhysicsWorld(new PhysicsConfiguration
        {
            Gravity  = new Vector3(0, -9.81f, 0),
            Timestep = 0.008f
        });

        var worldBuilder = new WorldBuilder(physicsWorld, new MeshLoader());
        var worldData    = worldBuilder.LoadAndBuildWorld(meshPath, null);

        Console.WriteLine($"✓ Loaded: {worldData.Meshes.Count} meshes");
        Console.WriteLine($"  Off-mesh links: {worldData.OffMeshLinks?.Count ?? 0}");
        if (worldData.OffMeshLinks != null)
        {
            foreach (var link in worldData.OffMeshLinks)
                Console.WriteLine($"    {link.Type} '{link.Id}'  {link.Start} → {link.End}");
        }
        Console.WriteLine();

        // ── Build tiled NavMesh WITH off-mesh links ───────────────────────────
        Console.WriteLine("Building tiled NavMesh with off-mesh links...");
        var agentConfig = AgentConfig.Player;
        // TileSize=48 ensures the teleport link's start tile (1,1) and end tile (1,0) are
        // directly adjacent so DotRecast ConnectExtOffMeshLinks can wire the cross-tile link.
        // With TileSize=16 the tiles are 3 apart in Z and the link is silently dropped.
        var navConfig   = new NavMeshConfiguration { EnableTileUpdates = true, TileSize = 48 };
        var navBuilder  = new NavMeshBuilder(physicsWorld, new NavMeshGenerator());

        var sw = Stopwatch.StartNew();
        var navMeshData = navBuilder.BuildTiledNavMeshDirect(agentConfig, navConfig, worldData.OffMeshLinks);
        sw.Stop();
        Console.WriteLine($"✓ NavMesh built in {sw.ElapsedMilliseconds}ms  isMultiTile={navMeshData.IsMultiTile}");
        Console.WriteLine();

        vizServer.BroadcastState(SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData));

        // ── Pathfinding stack ─────────────────────────────────────────────────
        var pfConfig   = new PathfindingConfiguration();
        var pathfinder = new Pathfinder(navMeshData, worldData.OffMeshLinks);
        var pfService  = new PathfindingService(pathfinder, agentConfig, pfConfig);
        var motorCtrl  = new MotorCharacterController(physicsWorld);
        var moveCtrl   = new MovementController(physicsWorld, pfService, agentConfig, pfConfig, motorCtrl);

        // ── Agent positions ───────────────────────────────────────────────────
        // Jump link endpoints (from OBJ markers): start≈(53.32,-2.60,-7.03) end≈(46.27,-2.30,-16.45)
        // Teleport link endpoints:                start≈(42.83, 7.55,21.40) end≈(20.05,-2.12,-23.58)

        // Agent-1: comes from far side of jump island → must cross the jump to reach goal.
        //          Obstacle spawned in the pre-jump approach at t=5s.
        var a1Start = new Vector3(57f, -2.60f, -2f);
        var a1Goal  = new Vector3(43f, -2.30f, -22f);

        // Agent-2: clean jump-link demo, exactly entry→exit
        var a2Start = new Vector3(53.32f, -2.60f, -7.03f);
        var a2Goal  = new Vector3(46.27f, -2.30f, -16.45f);

        // Agent-3: teleport link — elevated platform to low terrain
        var a3Start = new Vector3(42.83f,  7.55f,  21.40f);
        var a3Goal  = new Vector3(20.05f, -2.12f, -23.58f);

        // ── Spawn agents ──────────────────────────────────────────────────────
        Console.WriteLine("Spawning agents...");
        float halfH = (agentConfig.Height / 2f) + agentConfig.Radius;

        var agents = new (int Id, string Name, Vector3 Start, Vector3 Goal)[]
        {
            (101, "Agent-1 [JumpApproach]",  a1Start, a1Goal),
            (102, "Agent-2 [JumpLink]",      a2Start, a2Goal),
            (103, "Agent-3 [TeleportLink]",  a3Start, a3Goal),
        };

        var agentEntities = new List<PhysicsEntity>();
        foreach (var (id, name, start, goal) in agents)
        {
            var snapped = pfService.FindNearestValidPosition(start, new Vector3(8f, 10f, 8f)) ?? start;
            var center  = new Vector3(snapped.X, snapped.Y + halfH, snapped.Z);
            var (shape, inertia) = physicsWorld.CreateCapsuleShapeWithInertia(
                agentConfig.Radius, agentConfig.Height, mass: 1f);
            var entity = physicsWorld.RegisterEntityWithInertia(
                id, EntityType.Player, center, shape, inertia, isStatic: false, disableGravity: false);
            agentEntities.Add(entity);
            Console.WriteLine($"  ✓ {name}  spawn=({snapped.X:F1},{snapped.Y:F1},{snapped.Z:F1})");
        }
        Console.WriteLine();

        // Settle phase
        Console.WriteLine("Settling (1.5 s)...");
        var expectedCenters = agentEntities.Select(e =>
            physicsWorld.GetEntityPosition(e)).ToArray();

        for (int i = 0; i < 187; i++)
        {
            for (int ai = 0; ai < agentEntities.Count; ai++)
            {
                physicsWorld.SetEntityVelocity(agentEntities[ai], Vector3.Zero);
                physicsWorld.SetEntityPosition(agentEntities[ai], expectedCenters[ai]);
            }
            physicsWorld.Update(0.008f);
            vizServer.BroadcastState(SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData));
            Thread.Sleep(8);
        }
        Console.WriteLine("✓ Settled");
        Console.WriteLine();

        // Issue initial moves
        foreach (var (id, name, start, goal) in agents)
        {
            var resp = moveCtrl.RequestMovement(new MovementRequest(id, goal, maxSpeed: 4f));
            Console.WriteLine($"  {name}: Move → {(resp.Success ? "OK" : "FAIL " + resp.Message)}");
        }
        Console.WriteLine();

        // ── Obstacle configuration ────────────────────────────────────────────
        // Placed in Agent-1's approach path, between its start and the jump link entry.
        var obstacleSize  = new Vector3(2f, 3f, 2f);
        var obstacleFloor = new Vector3(55f, -2.60f, -4.5f);   // navmesh-level floor position
        var obstacleCenter = new Vector3(
            obstacleFloor.X,
            obstacleFloor.Y + obstacleSize.Y * 0.5f,
            obstacleFloor.Z);

        PhysicsEntity? obstacleEntity = null;
        bool obstacleSpawned   = false;
        bool obstacleDespawned = false;

        // ── Simulation loop ───────────────────────────────────────────────────
        int totalSteps          = 2500;
        // Agent-1 reaches the obstacle zone (X=55, Z=-4.5) at ~t=0.8s — spawn before that
        // so Agent-1 must replan. t=0.4s gives it a fresh path that detours around the box.
        int obstacleSpawnStep   = 50;    // t ≈ 0.4s
        int obstacleDespawnStep = 1250;  // t ≈ 10s

        Console.WriteLine($"Running simulation ({totalSteps * 0.008f:F0}s, {totalSteps} steps)...");
        Console.WriteLine("  Watch Unity for:");
        Console.WriteLine("  • Agent-1 jumping the gap (uses jump link)");
        Console.WriteLine("  • Agent-1 replanning around obstacle at t=0.4s then still jumping");
        Console.WriteLine("  • Agent-2 jumping the gap (jump link entry→exit)");
        Console.WriteLine("  • Agent-3 teleporting from elevated platform to lower terrain");
        Console.WriteLine("  • Obstacle despawn and tile restore at t=10s (step 1250)");
        Console.WriteLine();

        for (int step = 0; step < totalSteps; step++)
        {
            float t = step * 0.008f;

            // ── Spawn obstacle at t≈5s ────────────────────────────────────────
            if (step == obstacleSpawnStep && !obstacleSpawned)
            {
                obstacleSpawned = true;
                Console.WriteLine($"[t={t:F1}s] Spawning obstacle at floor={obstacleFloor}...");

                var (obShape, obInertia) = physicsWorld.CreateBoxShapeWithInertia(obstacleSize, mass: 1f);
                obstacleEntity = physicsWorld.RegisterEntityWithInertia(
                    200, EntityType.Obstacle, obstacleCenter, obShape, obInertia, isStatic: true);

                // Rebuild tiles using SOURCE geometry + solid obstacle box so Recast
                // voxelizes the box as blocking geometry and erodes walkable area around it.
                float obRadius = MathF.Max(obstacleSize.X, obstacleSize.Z) * 0.5f + 1f;
                var (obVerts, obInds) = BoxGeometry(obstacleCenter, obstacleSize);
                int srcVertCount = navMeshData.SourceVertices!.Length / 3;
                var combinedVerts = navMeshData.SourceVertices!.Concat(obVerts).ToArray();
                var combinedInds  = navMeshData.SourceIndices!
                    .Concat(obInds.Select(i => i + srcVertCount)).ToArray();

                pfService.RebuildNavMeshRegion(
                    obstacleFloor, obRadius,
                    combinedVerts, combinedInds,
                    navMeshData.NavConfig!);

                moveCtrl.RequestMovement(new MovementRequest(101, a1Goal, maxSpeed: 4f));
                Console.WriteLine($"  Obstacle footprint {obstacleSize.X}×{obstacleSize.Z}m marked unwalkable (tight hole)");
                Console.WriteLine($"  Agent-1 re-issued move to replan");
            }

            // ── Despawn obstacle at t≈10s ─────────────────────────────────────
            if (step == obstacleDespawnStep && !obstacleDespawned && obstacleEntity != null)
            {
                obstacleDespawned = true;
                Console.WriteLine($"[t={t:F1}s] Despawning obstacle — restoring tile from source geometry...");

                physicsWorld.UnregisterEntity(obstacleEntity);

                float obRadius = MathF.Max(obstacleSize.X, obstacleSize.Z) * 0.5f + 1f;
                pfService.RebuildNavMeshRegion(
                    obstacleFloor, obRadius,
                    navMeshData.SourceVertices!, navMeshData.SourceIndices!,
                    navMeshData.NavConfig!);

                moveCtrl.RequestMovement(new MovementRequest(101, a1Goal, maxSpeed: 4f));
                Console.WriteLine($"  Tile restored  Agent-1 re-issued move");
            }

            moveCtrl.UpdateMovement(0.008f);
            physicsWorld.Update(0.008f);

            // Broadcast with agent paths and traversal state
            var state = SimulationStateBuilder.BuildFromPhysicsWorld(
                physicsWorld, navMeshData, getTraversalInfo:
                id => {
                    var info = motorCtrl.GetTraversalInfo(id);
                    return info.HasValue ? (info.Value.Type.ToString(), info.Value.T) : null;
                });
            foreach (var entity in agentEntities)
            {
                var wps = moveCtrl.GetWaypoints(entity.EntityId);
                if (wps?.Count > 0)
                {
                    state.AgentPaths.Add(new PathData
                    {
                        EntityId  = entity.EntityId,
                        Waypoints = wps.Select(w => new[] { w.X, w.Y, w.Z }).ToList()
                    });
                }
            }
            vizServer.BroadcastState(state);

            // Progress log every ~2s
            if (step % 250 == 0)
            {
                Console.WriteLine($"  [t={t:F1}s]");
                foreach (var entity in agentEntities)
                {
                    var pos  = physicsWorld.GetEntityPosition(entity);
                    var info = agents.First(a => a.Id == entity.EntityId);
                    float dist = Vector3.Distance(pos, info.Goal);
                    Console.WriteLine($"    {info.Name}: ({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) dist={dist:F1}m");
                }
            }

            Thread.Sleep(8);
        }

        physicsWorld.Dispose();

        Console.WriteLine();
        Console.WriteLine("✅ COMBINED SHOWCASE COMPLETE");
        Console.WriteLine("   Check console for [MovementController] BeginLinkTraversal events");
        Console.WriteLine("   Check console for [PathfindingService] RebuildNavMeshRegion events");
    }

    private static (float[] verts, int[] inds) BoxGeometry(Vector3 center, Vector3 size)
    {
        float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
        float cx = center.X, cy = center.Y, cz = center.Z;
        var verts = new float[]
        {
            cx-hx, cy-hy, cz-hz, cx+hx, cy-hy, cz-hz,
            cx+hx, cy-hy, cz+hz, cx-hx, cy-hy, cz+hz,
            cx-hx, cy+hy, cz-hz, cx+hx, cy+hy, cz-hz,
            cx+hx, cy+hy, cz+hz, cx-hx, cy+hy, cz+hz,
        };
        var inds = new int[]
        {
            0,2,1, 0,3,2,   // bottom
            4,5,6, 4,6,7,   // top
            0,1,5, 0,5,4,   // front (-z)
            2,3,7, 2,7,6,   // back  (+z)
            0,4,7, 0,7,3,   // left  (-x)
            1,2,6, 1,6,5,   // right (+x)
        };
        return (verts, inds);
    }
}
