using System.Linq;
using System.Numerics;
using Spatial.Integration;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.Server;
using Spatial.MeshLoading;

namespace Spatial.TestHarness;

/// <summary>
/// Scenario 1 — Local NavMesh tile rebuild visible in Unity.
///
/// A single agent walks across simple_arena.obj.
/// Phase A (2s): free walk (-6,0,0) → (6,0,0) — direct route crosses the centre tile.
/// Obstacle spawns at (0,0,0): 3×3×3 physics box added + centre NavMesh tile erased.
/// Phase B (5s): agent replans around the gap (centre tile gone, detour via Z-side tiles).
/// Obstacle despawns: centre tile rebuilt from original geometry.
/// Phase C (3s): agent resumes direct route.
///
/// Why TileSize=8 with rebake_arena.obj?
///   The arena is 24×24 (world -12..12).  TileSize=8 divides it into an exact
///   3×3 grid: X[-12,-4), [-4,4), [4,12] / Z[-12,-4), [-4,4), [4,12].
///   Erasing only tile (1,1) leaves all 8 surrounding tiles intact so the agent
///   can detour via the top or bottom row.
/// </summary>
public static class TestObstacleRebakeVisual
{
    static string ResolvePath(string rel) =>
        Path.Combine(Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location)!, rel);

    public static void Run(VisualizationServer vizServer, string? meshPath = null)
    {
        meshPath ??= ResolvePath("worlds/rebake_arena.obj");

        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   OBSTACLE REBAKE VISUAL TEST                                ║");
        Console.WriteLine("║   Local NavMesh tile rebuild on obstacle spawn/despawn       ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        if (!File.Exists(meshPath))
        {
            Console.WriteLine($"✗ Mesh not found: {meshPath}");
            return;
        }

        // ── Physics + geometry ────────────────────────────────────────────────
        Console.WriteLine("Loading world geometry into physics world...");
        var physicsWorld = new PhysicsWorld(new PhysicsConfiguration
        {
            Gravity  = new Vector3(0, -9.81f, 0),
            Timestep = 0.008f
        });

        var worldBuilder = new WorldBuilder(physicsWorld, new MeshLoader());
        var worldData    = worldBuilder.LoadAndBuildWorld(meshPath, null);
        Console.WriteLine($"✓ Physics mesh loaded ({worldData.Meshes.Count} meshes)");

        // ── Tiled NavMesh ─────────────────────────────────────────────────────
        // TileSize=8 → 3×3 tile grid for the 20×20 arena.
        // The obstacle only erases the centre tile (1,1); detour via corner tiles still exists.
        Console.WriteLine("Baking tiled NavMesh (TileSize=8)...");
        var agentConfig = AgentConfig.Player;
        var navConfig   = new NavMeshConfiguration { EnableTileUpdates = true, TileSize = 8 };
        var navBuilder  = new NavMeshBuilder(physicsWorld, new NavMeshGenerator());
        var navMeshData = navBuilder.BuildTiledNavMeshDirect(agentConfig, navConfig);
        Console.WriteLine($"✓ NavMesh baked  isMultiTile={navMeshData.IsMultiTile}  tileSize={navMeshData.TileSize}");
        Console.WriteLine();

        vizServer.BroadcastState(SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData));

        // ── Pathfinding + movement stack ──────────────────────────────────────
        var pfConfig   = new PathfindingConfiguration();
        var pathfinder = new Pathfinder(navMeshData);
        var pfService  = new PathfindingService(pathfinder, agentConfig, pfConfig);
        var motorCtrl  = new MotorCharacterController(physicsWorld);
        var moveCtrl   = new MovementController(physicsWorld, pfService, agentConfig, pfConfig, motorCtrl);

        moveCtrl.OnPathReplanned     += id  => Console.WriteLine($"  [Event] Agent {id} replanned path");
        moveCtrl.OnDestinationReached += (id, pos) =>
            Console.WriteLine($"  [Event] Agent {id} reached goal ({pos.X:F1},{pos.Y:F1},{pos.Z:F1})");

        // ── Spawn agent ───────────────────────────────────────────────────────
        // Start/goal are placed well inside the arena (not against walls) so the
        // direct route clearly passes through the centre tile where the obstacle spawns.
        var agentStart = new Vector3(-6f, 0f, 0f);
        var agentGoal  = new Vector3( 6f, 0f, 0f);

        var snapped    = pfService.FindNearestValidPosition(agentStart, new Vector3(5f, 10f, 5f)) ?? agentStart;
        float halfH    = (agentConfig.Height / 2f) + agentConfig.Radius;
        var spawnCenter = new Vector3(snapped.X, snapped.Y + halfH, snapped.Z);

        var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
            agentConfig.Radius, agentConfig.Height, mass: 1f);
        var agentEntity = physicsWorld.RegisterEntityWithInertia(
            1, EntityType.Player, spawnCenter, agentShape, agentInertia,
            isStatic: false, disableGravity: false);

        Console.WriteLine($"Agent spawned at ({snapped.X:F1},{snapped.Y:F1},{snapped.Z:F1})  goal: {agentGoal}");

        // Brief settle: hold agent in place while physics initialises (0.5 s)
        for (int i = 0; i < 62; i++)
        {
            physicsWorld.SetEntityVelocity(agentEntity, Vector3.Zero);
            physicsWorld.SetEntityPosition(agentEntity, spawnCenter);
            physicsWorld.Update(0.008f);
            vizServer.BroadcastState(SimulationStateBuilder.BuildFromPhysicsWorld(physicsWorld, navMeshData));
            Thread.Sleep(8);
        }

        // Issue first move
        var r = moveCtrl.RequestMovement(new MovementRequest(1, agentGoal, maxSpeed: 4f));
        Console.WriteLine($"Move issued: {(r.Success ? "OK" : "FAIL — " + r.Message)}");
        Console.WriteLine();

        // ── Phase A: free walk ────────────────────────────────────────────────
        Console.WriteLine("══ Phase A: Walking freely (2 s) ═══════════════════════════════");
        RunLoop(physicsWorld, navMeshData, moveCtrl, motorCtrl, vizServer, agentEntity,
                steps: 250, dt: 0.008f, label: "A");

        // ── Spawn obstacle ────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("══ Spawning obstacle at centre ══════════════════════════════════");

        var obstacleBase  = new Vector3(0f,  0f, 0f);
        var obstacleSize  = new Vector3(3f,  3f, 3f); // large so it's visible and clearly blocks the path
        var obstacleCenter = new Vector3(obstacleBase.X,
                                         obstacleBase.Y + obstacleSize.Y * 0.5f,
                                         obstacleBase.Z);

        var (obShape, obInertia) = physicsWorld.CreateBoxShapeWithInertia(obstacleSize, mass: 1f);
        var obstacleEntity = physicsWorld.RegisterEntityWithInertia(
            100, EntityType.Obstacle, obstacleCenter, obShape, obInertia, isStatic: true);

        // Rebuild the NavMesh tile(s) with source terrain + obstacle box geometry so only
        // the obstacle footprint becomes non-walkable; the surrounding tile stays open.
        float obRadius = MathF.Max(obstacleSize.X, obstacleSize.Z) * 0.5f + 0.5f;
        var (obVerts, obInds) = BoxGeometry(obstacleCenter, obstacleSize);
        int srcVertCount = navMeshData.SourceVertices!.Length / 3;
        var combinedVerts = navMeshData.SourceVertices!.Concat(obVerts).ToArray();
        var combinedInds  = navMeshData.SourceIndices!
            .Concat(obInds.Select(i => i + srcVertCount)).ToArray();
        int tilesRebuilt = pfService.RebuildNavMeshRegion(
            obstacleBase, obRadius, combinedVerts, combinedInds, navMeshData.NavConfig!);
        Console.WriteLine($"  Obstacle spawned  tilesRebuilt={tilesRebuilt}  radius={obRadius:F1}m");

        // Re-issue move so the planner routes around the erased tile
        moveCtrl.RequestMovement(new MovementRequest(1, agentGoal, maxSpeed: 4f));
        Console.WriteLine("  Move re-issued — agent should detour via Z-side tiles");
        Console.WriteLine();

        // ── Phase B: detour ───────────────────────────────────────────────────
        Console.WriteLine("══ Phase B: Detouring around obstacle (5 s) ═════════════════════");
        RunLoop(physicsWorld, navMeshData, moveCtrl, motorCtrl, vizServer, agentEntity,
                steps: 625, dt: 0.008f, label: "B");

        // ── Despawn obstacle ──────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("══ Despawning obstacle ══════════════════════════════════════════");
        physicsWorld.UnregisterEntity(obstacleEntity);

        int tilesRestored = pfService.RebuildNavMeshRegion(
            obstacleBase, obRadius,
            navMeshData.SourceVertices!, navMeshData.SourceIndices!,
            navMeshData.NavConfig!);
        Console.WriteLine($"  Obstacle removed  tilesRestored={tilesRestored}");

        moveCtrl.RequestMovement(new MovementRequest(1, agentGoal, maxSpeed: 4f));
        Console.WriteLine("  Move re-issued — direct route available again");
        Console.WriteLine();

        // ── Phase C: direct route ─────────────────────────────────────────────
        Console.WriteLine("══ Phase C: Taking direct route (3 s) ═══════════════════════════");
        RunLoop(physicsWorld, navMeshData, moveCtrl, motorCtrl, vizServer, agentEntity,
                steps: 375, dt: 0.008f, label: "C");

        physicsWorld.Dispose();
        Console.WriteLine();
        Console.WriteLine("✅ OBSTACLE REBAKE VISUAL TEST COMPLETE");
    }

    private static void RunLoop(
        PhysicsWorld physicsWorld, NavMeshData navMeshData,
        MovementController moveCtrl, MotorCharacterController motorCtrl,
        VisualizationServer vizServer, PhysicsEntity agentEntity,
        int steps, float dt, string label)
    {
        for (int i = 0; i < steps; i++)
        {
            physicsWorld.Update(dt);
            moveCtrl.UpdateMovement(dt);

            var state = SimulationStateBuilder.BuildFromPhysicsWorld(
                physicsWorld, navMeshData,
                getTraversalInfo: id => {
                    var info = motorCtrl.GetTraversalInfo(id);
                    return info.HasValue ? (info.Value.Type.ToString(), info.Value.T) : null;
                });

            var waypoints = moveCtrl.GetWaypoints(1);
            if (waypoints?.Count > 0)
            {
                state.AgentPaths.Add(new PathData
                {
                    EntityId  = 1,
                    Waypoints = waypoints.Select(w => new[] { w.X, w.Y, w.Z }).ToList()
                });
            }

            vizServer.BroadcastState(state);

            if (i % 125 == 0)
            {
                var pos = physicsWorld.GetEntityPosition(agentEntity);
                float feetY = pos.Y - (AgentConfig.Player.Height / 2f) - AgentConfig.Player.Radius;
                Console.WriteLine($"  [{label}] t={i * dt:F1}s  center=({pos.X:F1},{pos.Y:F1},{pos.Z:F1})  feet.Y={feetY:F2}");
            }

            Thread.Sleep(8);
        }
    }

    // Returns flat vertex (x,y,z triplets) and index arrays for a solid box.
    // Used to include obstacle geometry in NavMesh tile rebuilds so only the
    // footprint becomes non-walkable while the rest of the tile is preserved.
    private static (float[] verts, int[] inds) BoxGeometry(Vector3 center, Vector3 size)
    {
        float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
        float cx = center.X,      cy = center.Y,        cz = center.Z;
        var verts = new float[]
        {
            cx-hx, cy-hy, cz-hz,  cx+hx, cy-hy, cz-hz,
            cx+hx, cy-hy, cz+hz,  cx-hx, cy-hy, cz+hz,
            cx-hx, cy+hy, cz-hz,  cx+hx, cy+hy, cz-hz,
            cx+hx, cy+hy, cz+hz,  cx-hx, cy+hy, cz+hz,
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
