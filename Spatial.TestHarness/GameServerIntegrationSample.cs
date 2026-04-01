// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║          SPATIAL — GAME SERVER INTEGRATION SAMPLE                           ║
// ║                                                                              ║
// ║  This file is a reference guide for game server developers integrating       ║
// ║  the Spatial physics + pathfinding system. Each static method is a          ║
// ║  self-contained runnable scenario. Read top-to-bottom for a full tour,      ║
// ║  or copy individual methods for the specific feature you need.              ║
// ║                                                                              ║
// ║  Run via:  dotnet run --project Spatial.TestHarness -- sample               ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

using System.Numerics;
using System.Diagnostics;
using Spatial.Integration;
using Spatial.Pathfinding;
using Spatial.Physics;

namespace Spatial.TestHarness;

/// <summary>
/// Comprehensive game server integration sample for the Spatial framework.
///
/// WORLD MESH FILES (.obj):
///   Place your world mesh files in:  Spatial.TestHarness/worlds/
///   They are automatically copied to the output directory by the .csproj file.
///   Load them at runtime with the path helper below or AppContext.BaseDirectory.
///
///   Supported format: Wavefront OBJ (.obj)
///   Tip: Export your level geometry from your 3D tool as OBJ before baking.
///
/// TWO-STEP WORKFLOW:
///   ┌─────────────────────────────────────────────────────────────┐
///   │  STEP 1 — BakeNavMesh  (expensive: 100–500 ms per map)     │
///   │    • Loads the .obj file                                    │
///   │    • Voxelises geometry via DotRecast                       │
///   │    • Produces a read-only NavMeshData                       │
///   │    • Do this ONCE per map version, at server startup        │
///   │    • Reuse the result for every room using that map         │
///   ├─────────────────────────────────────────────────────────────┤
///   │  STEP 2 — new World(baked, agentConfig)  (cheap: < 5 ms)   │
///   │    • Creates an isolated physics simulation                 │
///   │    • Wires up pathfinding and movement subsystems           │
///   │    • One instance per game room / dungeon run / match       │
///   └─────────────────────────────────────────────────────────────┘
/// </summary>
public static class GameServerIntegrationSample
{
    // ── Entry point ───────────────────────────────────────────────────────────

    public static void Run(string meshPath)
    {
        Console.WriteLine("\n" + new string('═', 70));
        Console.WriteLine("  SPATIAL — GAME SERVER INTEGRATION SAMPLE");
        Console.WriteLine(new string('═', 70));
        Console.WriteLine($"  Mesh: {Path.GetFileName(meshPath)}");
        Console.WriteLine();

        RunScenario("01 — Basic: Single World Setup",        () => Scenario01_SingleWorldSetup(meshPath));
        RunScenario("02 — Basic: Unit Configurations",       () => Scenario02_UnitConfigurations(meshPath));
        RunScenario("03 — Intermediate: Multiple Worlds",    () => Scenario03_MultipleWorlds(meshPath));
        RunScenario("04 — Intermediate: Agent Lifecycle",    () => Scenario04_AgentLifecycle(meshPath));
        RunScenario("05 — Intermediate: Movement Events",    () => Scenario05_MovementEvents(meshPath));
        RunScenario("06 — Intermediate: Runtime Queries",    () => Scenario06_RuntimeQueries(meshPath));
        RunScenario("07 — Advanced: Runtime NavMesh Update", () => Scenario07_RuntimeNavMeshUpdate(meshPath));
        RunScenario("08 — Advanced: Knockback & Abilities",  () => Scenario08_KnockbackAndAbilities(meshPath));
        RunScenario("09 — Advanced: Non-Pushable Objects",   () => Scenario09_NonPushableObjects(meshPath));
        RunScenario("10 — Advanced: Hot-Swap World Mesh",    () => Scenario10_HotSwapWorldMesh(meshPath));

        Console.WriteLine("\n" + new string('═', 70));
        Console.WriteLine("  ALL SCENARIOS COMPLETE");
        Console.WriteLine(new string('═', 70) + "\n");
    }

    private static void RunScenario(string name, Action action)
    {
        Console.WriteLine(new string('─', 70));
        Console.WriteLine($"  SCENARIO: {name}");
        Console.WriteLine(new string('─', 70));
        try
        {
            action();
            Console.WriteLine($"  [OK] {name}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] {name}");
            Console.WriteLine($"  Error: {ex.Message}\n");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BASIC SCENARIOS
    // ═════════════════════════════════════════════════════════════════════════

    #region BASIC

    /// <summary>
    /// SCENARIO 01 — BASIC: Single World Setup
    ///
    /// The minimal complete workflow: bake a NavMesh, create a world, spawn one unit,
    /// command it to move, run the simulation loop, then clean up.
    ///
    /// This is the foundation every other scenario builds on.
    /// </summary>
    static void Scenario01_SingleWorldSetup(string meshPath)
    {
        // ── STEP 1: Define how your unit behaves in the world ─────────────────
        //
        // AgentConfig is the SINGLE SOURCE OF TRUTH for unit dimensions.
        // The same object drives:
        //   • NavMesh generation  — how much clearance to require around obstacles
        //   • Path validation     — MaxClimb/MaxSlope checks on each path segment
        //   • Physics capsule     — size of the collision body
        //
        // NEVER mix different configs between subsystems. If NavMesh was baked with
        // Radius=0.4 but the capsule has Radius=0.8, the unit will clip geometry.
        var agentConfig = new AgentConfig
        {
            Radius   = 0.4f,  // capsule radius (meters) — unit half-width
            Height   = 2.0f,  // capsule cylinder length  — roughly torso height
            MaxClimb = 0.5f,  // max step height the unit can climb (meters)
            MaxSlope = 45.0f, // max walkable incline (degrees)
        };

        // ── STEP 2: Bake the NavMesh ──────────────────────────────────────────
        //
        // Baking processes the raw triangle mesh into a navigation mesh of walkable
        // polygons. It applies AgentConfig constraints so that NavMesh edges already
        // respect clearance, slope, and step-height limits.
        //
        // This step takes 100–500 ms depending on mesh complexity.
        // DO THIS ONCE AT SERVER STARTUP — not on every room creation.
        //
        // The returned NavMeshData is read-only and thread-safe to share across
        // multiple World instances running the same map simultaneously.
        Console.WriteLine("  Baking NavMesh...");
        var sw = Stopwatch.StartNew();
        NavMeshData baked = World.BakeNavMesh(meshPath, agentConfig);
        sw.Stop();
        Console.WriteLine($"  NavMesh baked in {sw.ElapsedMilliseconds} ms");

        // ── STEP 3: Create a World instance ───────────────────────────────────
        //
        // World creation is cheap (< 5 ms). Create one per game room / instance.
        // Each World has its own isolated BepuPhysics simulation — entities in
        // different worlds cannot interact with each other.
        //
        // The 'using' statement ensures world.Dispose() is called automatically
        // when the room ends, releasing all BepuPhysics memory.
        using var world = new World(baked, agentConfig);

        // ── STEP 4: Spawn a unit ──────────────────────────────────────────────
        //
        // Spawn automatically:
        //   1. Snaps the position to the nearest NavMesh surface.
        //   2. Computes the correct physics capsule center Y so feet touch ground.
        //   3. Registers a BepuPhysics dynamic body with gravity enabled.
        //
        // Entity IDs must be unique within this World but NOT globally — two separate
        // World instances can both have an entity with id=1.
        var spawnPos = new Vector3(-18f, 5f, -18f);
        var unit = world.Spawn(entityId: 1, spawnPos);
        Console.WriteLine($"  Unit 1 spawned near {spawnPos}");
        Console.WriteLine($"  Actual spawn Y (after NavMesh snap): {world.GetPosition(1).Y:F2}");

        // ── STEP 5: Let the unit settle for a few ticks before pathfinding ────
        //
        // Immediately after spawning the physics body may still be in free-fall.
        // Running a few Update ticks first lets gravity and the motor controller
        // find a stable grounded state.
        const float DT = 0.016f; // 60 Hz fixed timestep
        for (int i = 0; i < 30; i++)
            world.Update(DT);

        // ── STEP 6: Command movement ──────────────────────────────────────────
        //
        // Move() runs the full pathfinding pipeline:
        //   • Snaps start + target to NavMesh
        //   • A* search via DotRecast
        //   • Path validation (MaxClimb / MaxSlope per segment)
        //   • Auto-fix: inserts waypoints on steep segments
        //   • Begins physics-driven movement along the waypoint list
        //
        // The response tells you whether pathfinding succeeded, the snapped positions,
        // the estimated path length, and the ETA at the requested speed.
        var target = new Vector3(10f, 5f, 10f);
        var response = world.Move(entityId: 1, target, speed: 5f);

        if (!response.Success)
        {
            Console.WriteLine($"  Move failed: {response.Message}");
            return;
        }
        Console.WriteLine($"  Move requested → Path: {response.PathResult?.Waypoints.Count} waypoints, " +
                          $"{response.EstimatedPathLength:F1} m, ETA {response.EstimatedTime:F1} s");

        // ── STEP 7: Game loop ─────────────────────────────────────────────────
        //
        // Call world.Update(deltaTime) every server tick.
        //
        // USE A FIXED TIMESTEP. A fixed DT makes the simulation deterministic —
        // every server instance processing the same commands in the same order
        // produces bit-identical positions. This is required for server-authoritative
        // multiplayer with dead-reckoning or lag-compensation on the client.
        //
        // Recommended values:
        //   0.016f  ≈  60 Hz  — standard game server tick rate
        //   0.008f  ≈ 125 Hz  — higher fidelity for competitive games
        //
        // UPDATE ORDER (enforced inside World.Update):
        //   1. Movement controller runs first  → sets velocity goals
        //   2. BepuPhysics runs second         → integrates velocities into positions
        bool arrived = false;
        world.OnDestinationReached += (id, pos) =>
        {
            arrived = true;
            Console.WriteLine($"  Unit {id} arrived at {pos}");
        };

        int steps = 0;
        const int MaxSteps = 800;
        while (!arrived && steps < MaxSteps)
        {
            world.Update(DT);
            steps++;
        }

        var finalPos = world.GetPosition(1);
        Console.WriteLine($"  Final position after {steps} ticks: {finalPos}");

        // ── STEP 8: Despawn ───────────────────────────────────────────────────
        //
        // Always despawn before the world is disposed.
        // Despawn order: stop movement → remove character controller state → remove physics body.
        world.Despawn(entityId: 1);

        // world.Dispose() is called automatically by 'using var world'
    }

    /// <summary>
    /// SCENARIO 02 — BASIC: Unit Configurations
    ///
    /// Different unit archetypes need different AgentConfig settings. This scenario
    /// demonstrates three presets and explains why each parameter matters.
    ///
    /// KEY RULE: Every unit type that needs to navigate separately (different size,
    /// different mobility) should be baked with its own AgentConfig and use its own
    /// World (or at least its own PathfindingService). Sharing a NavMesh between a
    /// human and a giant creature will cause the creature to walk into geometry that
    /// the human-sized NavMesh does not clear.
    /// </summary>
    static void Scenario02_UnitConfigurations(string meshPath)
    {
        // ── Preset definitions ────────────────────────────────────────────────

        // Standard humanoid player character.
        var playerConfig = new AgentConfig
        {
            Radius   = 0.4f,   // narrow enough to fit through doorways
            Height   = 2.0f,   // typical human height (cylinder, not total capsule)
            MaxClimb = 0.5f,   // can step up curbs and ledges
            MaxSlope = 45.0f,  // can climb steep hills
        };

        // Large slow enemy (e.g. knight in heavy armour, boss add).
        // Its larger radius means more clearance is required around walls and corners,
        // so it may not be able to enter narrow corridors that the player can.
        var heavyUnitConfig = new AgentConfig
        {
            Radius   = 0.8f,   // wide — requires more clearance
            Height   = 2.5f,   // taller
            MaxClimb = 0.3f,   // cannot climb as high — heavy, stiff legs
            MaxSlope = 30.0f,  // cannot handle steep slopes
        };

        // Small agile creature (e.g. rat, imp, spider).
        // A small radius lets it navigate areas that block larger units.
        var creatureConfig = new AgentConfig
        {
            Radius   = 0.2f,   // tiny — can squeeze through tight gaps
            Height   = 0.8f,   // low to the ground
            MaxClimb = 0.8f,   // can scramble over tall obstacles
            MaxSlope = 55.0f,  // can climb very steep surfaces
        };

        // ── Bake and test each archetype independently ────────────────────────
        //
        // Each config produces a different NavMesh:
        //   playerConfig    → standard coverage, normal doorways passable
        //   heavyUnitConfig → reduced coverage near walls, tight corridors excluded
        //   creatureConfig  → maximum coverage, tiny gaps included
        //
        // In production cache these NavMeshData objects in a dictionary keyed by
        // unit type — do not re-bake every time a room opens.
        //
        // NOTE: If a config's clearance requirements are too large for the mesh,
        // DotRecast may produce an empty or degenerate NavMesh.  This is expected —
        // not every unit type can navigate every piece of geometry.  Use a larger,
        // flatter map for heavy units in production.
        var archetypeConfigs = new[]
        {
            (Name: "Player",    Config: playerConfig),
            (Name: "HeavyUnit", Config: heavyUnitConfig),
            (Name: "Creature",  Config: creatureConfig),
        };

        var sw = Stopwatch.StartNew();
        foreach (var (Name, Config) in archetypeConfigs)
        {
            try
            {
                var navMesh = World.BakeNavMesh(meshPath, Config);
                using var world = new World(navMesh, Config);

                world.Spawn(entityId: 1, new Vector3(-18f, 5f, -18f));
                for (int i = 0; i < 30; i++) world.Update(0.016f); // settle

                var response = world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);
                Console.WriteLine($"  [{Name}]  Radius={Config.Radius}  MaxSlope={Config.MaxSlope}°  " +
                                  $"Path: {(response.Success ? $"{response.PathResult?.Waypoints.Count} waypoints" : "FAILED — " + response.Message)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{Name}]  Radius={Config.Radius}  " +
                                  $"Skipped — mesh too constrained for this config ({ex.Message})");
            }
        }
        Console.WriteLine($"  Archetype tests finished in {sw.ElapsedMilliseconds} ms");
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // INTERMEDIATE SCENARIOS
    // ═════════════════════════════════════════════════════════════════════════

    #region INTERMEDIATE

    /// <summary>
    /// SCENARIO 03 — INTERMEDIATE: Multiple Worlds
    ///
    /// A game server typically runs many simultaneous instances of the same map
    /// (e.g. 50 dungeon runs at once). The correct pattern is:
    ///   1. Bake the NavMesh ONCE and store it.
    ///   2. Create a NEW World for every room — each has isolated physics.
    ///   3. Entity IDs need only be unique per-world, not globally.
    ///   4. Update each world independently (separate threads or sequential in the same loop).
    /// </summary>
    static void Scenario03_MultipleWorlds(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };

        // ── Bake ONCE ─────────────────────────────────────────────────────────
        //
        // NavMeshData is read-only after creation. It is safe to share between
        // any number of concurrent World instances without locking.
        Console.WriteLine("  Baking shared NavMesh...");
        var baked = World.BakeNavMesh(meshPath, agentConfig);

        // ── Create two independent game rooms ─────────────────────────────────
        //
        // Each room gets its own PhysicsWorld (inside World). Physics objects in
        // room1 are completely invisible to room2 — they cannot collide or interact.
        using var room1 = new World(baked, agentConfig);
        using var room2 = new World(baked, agentConfig);

        Console.WriteLine("  Room 1 and Room 2 created from shared NavMesh");

        // ── Spawn different players in each room ──────────────────────────────
        //
        // Entity ID 1 exists in BOTH rooms. That is fine — IDs are local to a World.
        // If your game server uses globally-unique player IDs (e.g. database row IDs),
        // pass those directly — there is no need for a separate local remapping.
        room1.Spawn(entityId: 1, new Vector3(-18f, 5f, -18f));  // player Alice
        room1.Spawn(entityId: 2, new Vector3(-15f, 5f, -15f));  // player Bob

        room2.Spawn(entityId: 1, new Vector3(-18f, 5f, -18f));  // player Charlie  (same ID as Alice — OK)
        room2.Spawn(entityId: 2, new Vector3(-15f, 5f, -15f));  // player Dave

        // Settle both rooms
        for (int i = 0; i < 30; i++) { room1.Update(0.016f); room2.Update(0.016f); }

        // ── Issue different movement commands to each room ─────────────────────
        room1.Move(1, new Vector3(10f, 5f, 10f));
        room1.Move(2, new Vector3(8f,  5f, 12f));

        room2.Move(1, new Vector3(-5f,  5f, 15f));
        room2.Move(2, new Vector3(-10f, 5f, 5f));

        // ── Run both rooms in the same server tick loop ───────────────────────
        //
        // In production you would typically run each room on its own thread or
        // use async tasks. Here we update them sequentially for simplicity.
        int room1Arrived = 0, room2Arrived = 0;
        room1.OnDestinationReached += (id, _) => { room1Arrived++; Console.WriteLine($"  Room1 entity {id} arrived"); };
        room2.OnDestinationReached += (id, _) => { room2Arrived++; Console.WriteLine($"  Room2 entity {id} arrived"); };

        for (int step = 0; step < 800 && (room1Arrived < 2 || room2Arrived < 2); step++)
        {
            room1.Update(0.016f);
            room2.Update(0.016f);
        }

        Console.WriteLine($"  Room1 arrivals: {room1Arrived}/2   Room2 arrivals: {room2Arrived}/2");
    }

    /// <summary>
    /// SCENARIO 04 — INTERMEDIATE: Agent Lifecycle
    ///
    /// Complete lifecycle: spawn → wait for stability → move → track → teleport → despawn.
    ///
    /// Covers the two operations that do not involve pathfinding:
    ///   • Teleport — instant position change for respawn, GM commands, cutscenes.
    ///   • Despawn  — correct cleanup order to avoid dangling physics references.
    /// </summary>
    static void Scenario04_AgentLifecycle(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
        var baked = World.BakeNavMesh(meshPath, agentConfig);
        using var world = new World(baked, agentConfig);

        // ── SPAWN ─────────────────────────────────────────────────────────────
        //
        // The position is snapped to the NavMesh automatically.
        // You do not need to compute the capsule Y offset — World.Spawn() handles it.
        //
        // If the raw position is far above the ground (e.g. a UI click raycast that
        // missed the mesh), SnapToNavMesh() can be used first to find the surface:
        //
        //   var snapped = world.SnapToNavMesh(rawClickPos);
        //   if (snapped != null) world.Spawn(id, snapped.Value);
        Console.WriteLine("  Spawning unit...");
        world.Spawn(entityId: 1, new Vector3(-18f, 5f, -18f));

        // Stability phase: let physics settle before issuing pathfinding.
        for (int i = 0; i < 40; i++) world.Update(0.016f);
        Console.WriteLine($"  Spawn settled at Y={world.GetPosition(1).Y:F2}");

        // ── MOVE ──────────────────────────────────────────────────────────────
        bool arrived = false;
        world.OnDestinationReached += (id, pos) =>
        {
            Console.WriteLine($"  Entity {id} reached destination at {pos}");
            arrived = true;
        };

        var response = world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);
        Console.WriteLine($"  Move response: {response.Message}  " +
                          $"ETA: {response.EstimatedTime:F1}s");

        int steps = 0;
        while (!arrived && steps++ < 600) world.Update(0.016f);

        // ── TELEPORT ──────────────────────────────────────────────────────────
        //
        // Teleport moves the unit instantly to a new position.
        // Internally it:
        //   1. Calls StopMovement — cancels current path.
        //   2. Snaps the target to the NavMesh surface.
        //   3. Sets the physics body position directly (no arc, no gravity during teleport).
        //
        // Use cases:
        //   • Respawn after death         — teleport to a spawn pad
        //   • GM command "goto"           — teleport a player to a coordinate
        //   • Checkpoint warp             — move player after completing a section
        //   • Zone transition             — teleport to an entry point in a new room
        //
        // After teleporting, issue Move() again if the unit should resume navigation:
        var respawnPoint = new Vector3(-18f, 5f, -18f);
        Console.WriteLine($"  Teleporting unit to {respawnPoint}...");
        world.Teleport(1, respawnPoint);

        // Allow physics to register the new position
        world.Update(0.016f);
        Console.WriteLine($"  After teleport: Y={world.GetPosition(1).Y:F2}  " +
                          $"State={world.GetState(1)}");

        // Resume movement after teleport
        world.Move(1, new Vector3(5f, 5f, 5f), speed: 5f);
        for (int i = 0; i < 200; i++) world.Update(0.016f);

        // ── DESPAWN ───────────────────────────────────────────────────────────
        //
        // Always call Despawn before Dispose.
        //
        // Despawn order (enforced inside World.Despawn):
        //   1. StopMovement(id)  — clears path state, removes from character controller
        //   2. UnregisterEntity  — removes the BepuPhysics body
        //
        // NEVER call physicsWorld.UnregisterEntity while movement is still active.
        // The movement controller may still hold a reference to the entity on that
        // same frame, leading to a null-reference or access-violation in native code.
        Console.WriteLine("  Despawning unit...");
        world.Despawn(1);
        Console.WriteLine("  Unit despawned cleanly");
    }

    /// <summary>
    /// SCENARIO 05 — INTERMEDIATE: Movement Events
    ///
    /// The World exposes four events that the game server should subscribe to before
    /// issuing Move commands. Events are fired from inside world.Update().
    /// </summary>
    static void Scenario05_MovementEvents(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
        var baked = World.BakeNavMesh(meshPath, agentConfig);
        using var world = new World(baked, agentConfig);

        // ── Subscribe to all events BEFORE calling Move() ─────────────────────

        // Fired when pathfinding succeeds and movement begins.
        // 'actualStart' and 'actualTarget' are NavMesh-snapped — they may differ
        // from what the game server requested. Log the delta to debug snap issues.
        world.OnMovementStarted += (id, actualStart, actualTarget) =>
        {
            Console.WriteLine($"  [Event] OnMovementStarted  entity={id}  " +
                              $"start={actualStart}  target={actualTarget}");
        };

        // Fired when the unit reaches the final waypoint.
        // This is the right place to:
        //   • Start idle/patrol AI behaviour
        //   • Trigger quest completion logic
        //   • Begin an NPC dialogue sequence
        //   • Grant a reward for reaching a zone
        world.OnDestinationReached += (id, pos) =>
        {
            Console.WriteLine($"  [Event] OnDestinationReached  entity={id}  pos={pos}");
            // Example: StartIdleAI(id);
        };

        // Fired when the system automatically replanned around an obstacle.
        // The game server does NOT need to re-issue Move() — replanning is fully
        // automatic. Subscribe here for logging or UI feedback only.
        world.OnPathReplanned += id =>
        {
            Console.WriteLine($"  [Event] OnPathReplanned  entity={id}  (auto-handled, no action needed)");
        };

        // Fired each time the unit advances to the next waypoint.
        // fraction: 0.0 = just started, 1.0 = arrived.
        // Use for:
        //   • Progress bars on client HUD
        //   • Smooth animation blending (walk → run when fraction > 0.5)
        //   • Predictive client-side interpolation
        float lastProgress = 0f;
        world.OnMovementProgress += (id, fraction) =>
        {
            // Only log on significant jumps to keep output readable
            if (fraction - lastProgress > 0.19f)
            {
                Console.WriteLine($"  [Event] OnMovementProgress  entity={id}  {fraction * 100:F0}%");
                lastProgress = fraction;
            }
        };

        // ── Spawn, settle, move ───────────────────────────────────────────────
        world.Spawn(1, new Vector3(-18f, 5f, -18f));
        for (int i = 0; i < 30; i++) world.Update(0.016f);

        // Move() triggers OnMovementStarted immediately (before returning).
        var response = world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);
        if (!response.Success) { Console.WriteLine($"  Move failed: {response.Message}"); return; }

        // Also available on the response directly — useful for client-side ETA display:
        Console.WriteLine($"  EstimatedPathLength: {response.EstimatedPathLength:F1} m");
        Console.WriteLine($"  EstimatedTime:       {response.EstimatedTime:F1} s  at speed 5 m/s");

        bool done = false;
        world.OnDestinationReached += (_, __) => done = true;
        for (int step = 0; !done && step < 800; step++) world.Update(0.016f);
    }

    /// <summary>
    /// SCENARIO 06 — INTERMEDIATE: Runtime Queries
    ///
    /// The World exposes several query methods that the game server uses to make
    /// informed decisions without mutating any state.
    /// </summary>
    static void Scenario06_RuntimeQueries(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
        var baked = World.BakeNavMesh(meshPath, agentConfig);
        using var world = new World(baked, agentConfig);

        world.Spawn(1, new Vector3(-18f, 5f, -18f));
        for (int i = 0; i < 30; i++) world.Update(0.016f);

        // ── IsValidPosition ───────────────────────────────────────────────────
        //
        // Returns true if the position is on the walkable NavMesh.
        // Use this to validate a player's click-to-move target BEFORE issuing Move():
        //   if (!world.IsValidPosition(clickTarget)) ShowErrorFeedback();
        //
        // Also useful for spawn point validation — prefer this over always snapping.
        var testPos = new Vector3(-18f, 2f, -18f);
        bool valid = world.IsValidPosition(testPos);
        Console.WriteLine($"  IsValidPosition({testPos}): {valid}");

        // ── SnapToNavMesh ─────────────────────────────────────────────────────
        //
        // Finds the nearest NavMesh surface to the given point.
        //
        // DOWNWARD-PRIORITY SEARCH:
        //   The search prefers surfaces below the point (gravity-aligned).
        //   This is critical for multi-level maps (bridges, buildings):
        //     • If you click above a bridge, it snaps to the bridge deck.
        //     • It does NOT snap to the floor below the bridge.
        //   This ensures click-to-move works correctly in 3D environments.
        //
        // Returns null if no NavMesh surface is within the search radius.
        var rawClick = new Vector3(0f, 10f, 0f); // somewhere above the ground
        var snapped  = world.SnapToNavMesh(rawClick);
        Console.WriteLine($"  SnapToNavMesh({rawClick}): {(snapped.HasValue ? snapped.Value.ToString() : "null")}");

        // ── GetState ──────────────────────────────────────────────────────────
        //
        // Returns the current CharacterState of a unit.
        //
        //   GROUNDED   — standing on terrain; pathfinding active.
        //   AIRBORNE   — falling or knocked back; pathfinding paused.
        //   RECOVERING — just landed; motor controller stabilising.
        //
        // Use this to gate game systems:
        //   • Spell casting  — require GROUNDED
        //   • Dodge roll     — only available when GROUNDED
        //   • Landing SFX   — trigger when transitioning AIRBORNE → RECOVERING
        var state = world.GetState(1);
        Console.WriteLine($"  GetState(1): {state}");

        // ── GetPosition / GetVelocity ─────────────────────────────────────────
        //
        // Always use these (not client-reported positions) for authoritative simulation.
        // Send GetPosition() results to clients as the canonical position every tick.
        var pos = world.GetPosition(1);
        var vel = world.GetVelocity(1);
        Console.WriteLine($"  GetPosition(1): {pos}");
        Console.WriteLine($"  GetVelocity(1): speed={vel.Length():F2} m/s");

        // ── GetWaypoints (via escape hatch) ───────────────────────────────────
        //
        // Returns the current path waypoint list for a unit.
        // Uses the advanced Movement property directly (not on World surface).
        //
        // Uses:
        //   • Debug visualisation in development tools
        //   • Anti-cheat: verify the server-computed path matches the client's claim
        //   • Smooth client-side interpolation between authoritative positions
        world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);
        var waypoints = world.Movement.GetWaypoints(1);
        Console.WriteLine($"  GetWaypoints(1): {waypoints?.Count ?? 0} waypoints in active path");
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // ADVANCED SCENARIOS
    // ═════════════════════════════════════════════════════════════════════════

    #region ADVANCED

    /// <summary>
    /// SCENARIO 07 — ADVANCED: Runtime NavMesh Update (Opening a Door)
    ///
    /// Some game maps have dynamic geometry — doors that open, bridges that collapse,
    /// destructible walls. When such geometry changes, the NavMesh must be updated
    /// so the pathfinder knows the area is now passable or blocked.
    ///
    /// REQUIREMENTS:
    ///   Use a TILED NavMesh (NavMeshConfiguration.EnableTileUpdates = true).
    ///   Tile-based NavMeshes divide the world into a grid; only the affected tile(s)
    ///   are rebuilt — the rest remain unchanged. This is typically < 5 ms.
    ///
    /// TILE SIZING GUIDE:
    ///   TileSize = 32 with MaxTiles = 256  → covers a 512 × 512 m world  (16×16 grid)
    ///   TileSize = 16 with MaxTiles = 1024 → covers a 512 × 512 m world  (32×32 grid)
    ///   Smaller tiles = finer updates but more memory. Default 32 m is a good start.
    /// </summary>
    static void Scenario07_RuntimeNavMeshUpdate(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };

        // ── TILED NAVMESH CONFIG ──────────────────────────────────────────────
        //
        // Pass this same NavMeshConfiguration to BakeNavMesh AND RebuildNavMeshRegion.
        // They must agree on tile dimensions.
        var navConfig = new NavMeshConfiguration
        {
            EnableTileUpdates = true,
            TileSize          = 32f,   // world-space tile width/depth in meters
            MaxTiles          = 256,   // 16×16 grid → 512×512 m world at TileSize=32
            MaxPolysPerTile   = 2048,  // polygons per tile
        };

        Console.WriteLine("  Baking TILED NavMesh...");
        var baked = World.BakeNavMesh(meshPath, agentConfig, navConfig);
        using var world  = new World(baked, agentConfig);

        // Spawn a unit and let it start moving across the map
        world.Spawn(1, new Vector3(-18f, 5f, -18f));
        for (int i = 0; i < 30; i++) world.Update(0.016f);
        world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);

        // Simulate 1 second of movement (60 ticks) before updating the NavMesh
        for (int i = 0; i < 60; i++) world.Update(0.016f);
        Console.WriteLine($"  Unit position before NavMesh update: {world.GetPosition(1)}");

        // ── REBUILD A REGION ──────────────────────────────────────────────────
        //
        // Scenario: a door at (0, 0, 0) just opened. We pass the geometry of the
        // OPEN doorway (floor only, no blocking wall). The system finds all tiles
        // that intersect the radius and rebuilds them with the new geometry.
        //
        // To CLOSE a passage (bridge collapse, cave-in), pass empty vertex/index
        // arrays — the rebuilt tile will have no walkable surface in that region.
        //
        // After rebuilding, any agent whose active path crosses the updated tile(s)
        // will automatically replan within PathfindingConfiguration.PathValidationInterval
        // (default: 0.5 s). The game server does NOT need to re-issue Move().
        //
        // In a real game you would get newVerts and newIndices from your map geometry
        // system (e.g. exporting the open-doorway mesh from your level editor).
        float[] newVerts   = Array.Empty<float>(); // empty = remove all walkable area
        int[]   newIndices = Array.Empty<int>();

        var doorPosition = new Vector3(0f, 0f, 0f);
        int tilesRebuilt = world.Pathfinding.RebuildNavMeshRegion(
            center:     doorPosition,
            radius:     4f,
            newVertices: newVerts,
            newIndices:  newIndices,
            navConfig:  navConfig);

        Console.WriteLine($"  NavMesh region rebuilt: {tilesRebuilt} tile(s) updated");
        Console.WriteLine("  Agents crossing updated tiles will auto-replan within 0.5 s");

        // Continue simulation — affected agents replan automatically
        for (int i = 0; i < 120; i++) world.Update(0.016f);
        Console.WriteLine($"  Unit position after NavMesh update: {world.GetPosition(1)}");
    }

    /// <summary>
    /// SCENARIO 08 — ADVANCED: Knockback and Abilities
    ///
    /// Game abilities often interact with the physics system directly. Spatial supports
    /// two levels of impulse:
    ///
    ///   world.Knockback()  — HIGH-LEVEL: forces AIRBORNE state, pauses pathfinding,
    ///                        auto-replans when the unit lands.
    ///
    ///   Physics.ApplyLinearImpulse()  — LOW-LEVEL: raw impulse, no state change,
    ///                                   pathfinding continues uninterrupted.
    ///                                   Use for soft pushes that should not interrupt AI.
    ///
    /// Also demonstrates world.Jump() — a special upward impulse that triggers the
    /// same AIRBORNE → RECOVERING → GROUNDED state machine as Knockback.
    /// </summary>
    static void Scenario08_KnockbackAndAbilities(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
        var baked = World.BakeNavMesh(meshPath, agentConfig);
        using var world = new World(baked, agentConfig);

        world.Spawn(1, new Vector3(-18f, 5f, -18f));
        for (int i = 0; i < 30; i++) world.Update(0.016f);
        world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);
        for (int i = 0; i < 30; i++) world.Update(0.016f); // moving

        // ── HIGH-LEVEL KNOCKBACK ──────────────────────────────────────────────
        //
        // Use world.Knockback() for hard hits that should interrupt movement.
        //
        // The direction vector should be normalised. Including a small upward Y
        // component produces a natural arc rather than a pure horizontal slide.
        //
        // What happens internally:
        //   1. ApplyLinearImpulse(direction * force)  — adds velocity
        //   2. SetAirborne(entity)                    — pauses pathfinding
        //   3. Physics simulates the arc under gravity
        //   4. On landing: RECOVERING → GROUNDED → auto-replan resumes movement
        var knockbackDir = Vector3.Normalize(new Vector3(1f, 0.4f, 0.5f)); // forward + slightly up
        Console.WriteLine($"  Applying knockback (force=8)...");
        world.Knockback(1, knockbackDir, force: 8f);

        // Unit is now AIRBORNE — pathfinding is paused.
        Console.WriteLine($"  State immediately after knockback: {world.GetState(1)}");

        // Wait for the unit to land and recover before issuing new commands.
        // (In production you would check GetState() on each tick, not busy-wait.)
        int recoveryTicks = 0;
        while (world.GetState(1) != CharacterState.GROUNDED && recoveryTicks++ < 200)
            world.Update(0.016f);

        Console.WriteLine($"  GROUNDED again after {recoveryTicks} ticks  " +
                          $"pos={world.GetPosition(1)}");

        // Safe to issue a new movement command now.
        world.Move(1, new Vector3(5f, 5f, 5f), speed: 5f);
        for (int i = 0; i < 100; i++) world.Update(0.016f);

        // ── JUMP ─────────────────────────────────────────────────────────────
        //
        // Jump applies an upward impulse and triggers the same AIRBORNE state.
        // Returns false if the unit is not currently GROUNDED (cannot double-jump).
        bool jumped = world.Jump(1, jumpForce: 5f);
        Console.WriteLine($"  Jump attempted: {(jumped ? "success" : "failed — not grounded")}");
        Console.WriteLine($"  State after jump: {world.GetState(1)}");
        for (int i = 0; i < 60; i++) world.Update(0.016f);

        // ── LOW-LEVEL IMPULSE (no pathfinding interruption) ───────────────────
        //
        // Use Physics.ApplyLinearImpulse() for soft pushes that should not pause AI.
        // Example: a slight shoulder-bump from another player passing by.
        //
        // This bypasses the character controller state machine entirely — the unit
        // continues following its path even while the impulse is applied.
        var entity = world.Physics.EntityRegistry.GetEntityById(1);
        if (entity != null)
        {
            var gentlePush = new Vector3(1f, 0f, 0f); // nudge sideways
            world.Physics.ApplyLinearImpulse(entity, gentlePush);
            Console.WriteLine($"  Gentle impulse applied — pathfinding continues uninterrupted");
        }

        for (int i = 0; i < 60; i++) world.Update(0.016f);
    }

    /// <summary>
    /// SCENARIO 09 — ADVANCED: Non-Pushable Objects
    ///
    /// By default all dynamic entities have IsPushable = false — they act as solid
    /// obstacles that agents must path around. Explicitly setting a boss non-pushable
    /// is mostly for documentation clarity.
    ///
    /// Enable IsPushable on objects that SHOULD move when agents collide with them
    /// (barrels, crates, small props). Agents will push them aside rather than stopping.
    ///
    /// This scenario also shows spawning NPC/obstacle entity types alongside players.
    /// </summary>
    static void Scenario09_NonPushableObjects(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };
        var baked = World.BakeNavMesh(meshPath, agentConfig);
        using var world = new World(baked, agentConfig);

        // ── Spawn player ──────────────────────────────────────────────────────
        world.Spawn(entityId: 1, new Vector3(-18f, 5f, -18f), EntityType.Player);

        // ── Spawn a boss — non-pushable (default) ────────────────────────────
        //
        // Bosses should not be moved by player contact. They are obstacles that
        // the pathfinder will route agents around via the NavMesh.
        //
        // Note: the boss is registered as a dynamic body (not static) so it can
        // still be moved by Knockback or server commands. It just cannot be pushed
        // by normal agent-agent collision forces.
        var bossEntity = world.Spawn(entityId: 100, new Vector3(-5f, 5f, 0f), EntityType.NPC);
        world.Physics.SetEntityPushable(bossEntity, false); // explicit — non-pushable
        Console.WriteLine("  Boss spawned as NON-PUSHABLE obstacle");

        // ── Spawn a crate — pushable ──────────────────────────────────────────
        //
        // Crates and barrels that agents can shove out of the way.
        // When an agent walks into a pushable entity, a collision impulse is applied
        // and the crate slides away rather than blocking the agent's path.
        var crateEntity = world.Spawn(entityId: 200, new Vector3(5f, 5f, 0f), EntityType.NPC);
        world.Physics.SetEntityPushable(crateEntity, true); // can be shoved by agents
        Console.WriteLine("  Crate spawned as PUSHABLE — agents can push it aside");

        // Settle all objects
        for (int i = 0; i < 40; i++) world.Update(0.016f);

        // ── Move player toward the boss ───────────────────────────────────────
        //
        // The pathfinder routes around the boss because it occupies space on the
        // NavMesh. The boss itself does not move when the player arrives nearby
        // (non-pushable). The crate may shift slightly if the path goes near it.
        world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);

        int arrived = 0;
        world.OnDestinationReached += (id, _) => { if (id == 1) arrived = 1; };
        for (int step = 0; arrived == 0 && step < 800; step++) world.Update(0.016f);

        Console.WriteLine($"  Boss final position:  {world.GetPosition(100)}  (should be near -5,_,0 — immovable)");
        Console.WriteLine($"  Crate final position: {world.GetPosition(200)}  (may have shifted if pushed)");
    }

    /// <summary>
    /// SCENARIO 10 — ADVANCED: Hot-Swap World Mesh
    ///
    /// Occasionally a game server must replace the active world mesh at runtime:
    ///   • Content update — a new version of the map has been deployed
    ///   • Seasonal event — the map geometry changes for a holiday
    ///   • Dynamic map state — significant structural change not coverable by tile updates
    ///
    /// The correct procedure is a full re-bake + world recreation. This is disruptive
    /// (players are briefly suspended) but safe and correct. Schedule it between rounds
    /// or during low-traffic windows.
    ///
    /// SMOOTH TRANSITION TIPS:
    ///   • Save player positions before teardown.
    ///   • After recreating the world, snap saved positions to the new NavMesh.
    ///   • If a saved position has no valid snap, use the nearest spawn pad instead.
    /// </summary>
    static void Scenario10_HotSwapWorldMesh(string meshPath)
    {
        var agentConfig = new AgentConfig { Radius = 0.4f, Height = 2.0f, MaxClimb = 0.5f, MaxSlope = 45f };

        // ── Phase 1: Create and run the original world ────────────────────────
        Console.WriteLine("  [Phase 1] Creating original world...");
        var baked = World.BakeNavMesh(meshPath, agentConfig);
        var world = new World(baked, agentConfig);

        world.Spawn(1, new Vector3(-18f, 5f, -18f));
        world.Spawn(2, new Vector3(-15f, 5f, -15f));
        for (int i = 0; i < 30; i++) world.Update(0.016f);
        world.Move(1, new Vector3(10f, 5f, 10f), speed: 5f);
        world.Move(2, new Vector3(8f,  5f, 12f),  speed: 5f);
        for (int i = 0; i < 120; i++) world.Update(0.016f);

        // ── Phase 2: LOCK — stop accepting new move requests ─────────────────
        //
        // In your game server layer: set a flag so that incoming client commands
        // are queued or rejected with a "world reloading" error.
        bool acceptingMoveRequests = false; // game server flag
        Console.WriteLine("  [Phase 2] Move requests locked");

        // ── Phase 3: DRAIN — stop all active movement ─────────────────────────
        //
        // Stopping movement flushes pathfinding state cleanly so no entity holds
        // a reference into the movement controller during teardown.
        var activeEntityIds = new List<int> { 1, 2 };
        foreach (var id in activeEntityIds)
            world.StopMove(id);
        Console.WriteLine("  [Phase 3] All movement stopped");

        // ── Phase 4: SAVE — record current positions ──────────────────────────
        //
        // Store positions BEFORE disposing — GetPosition will not work after Dispose.
        // Use world-space coordinates; they are independent of the NavMesh.
        var savedPositions = activeEntityIds.ToDictionary(
            id => id,
            id => world.GetPosition(id));

        Console.WriteLine("  [Phase 4] Positions saved:");
        foreach (var (id, pos) in savedPositions)
            Console.WriteLine($"    entity {id}: {pos}");

        // ── Phase 5: TEARDOWN — dispose the old world ─────────────────────────
        //
        // Dispose releases all BepuPhysics native memory (BufferPool, Simulation).
        // After this call, the world object must not be used.
        world.Dispose();
        Console.WriteLine("  [Phase 5] Old world disposed");

        // ── Phase 6: REPLACE MESH FILE ────────────────────────────────────────
        //
        // Copy the new .obj file into the worlds/ directory.
        // In production this is typically triggered by a deployment pipeline:
        //
        //   File.Copy(newMapDownloadPath, ResolvePath("worlds/my_map.obj"), overwrite: true);
        //
        // For this demo we reuse the same file to keep the sample runnable.
        string newMeshPath = meshPath; // in practice: path to the new version
        Console.WriteLine("  [Phase 6] New mesh file in place (using same file for demo)");

        // ── Phase 7: REBAKE + RECREATE ────────────────────────────────────────
        //
        // Full bake + world creation with the new geometry.
        // If the new map has different terrain properties, update AgentConfig here.
        Console.WriteLine("  [Phase 7] Baking new NavMesh and recreating world...");
        var newBaked = World.BakeNavMesh(newMeshPath, agentConfig);
        var newWorld = new World(newBaked, agentConfig);

        // ── Phase 8: RESTORE — re-spawn entities at their saved positions ──────
        //
        // Snap each saved position to the NEW NavMesh surface.
        // If a position is no longer valid (geometry moved), SnapToNavMesh returns
        // null — in that case fall back to a designated spawn point.
        Console.WriteLine("  [Phase 8] Restoring entities...");
        var fallbackSpawn = new Vector3(-18f, 5f, -18f);

        foreach (var (id, savedPos) in savedPositions)
        {
            var snapped = newWorld.SnapToNavMesh(savedPos) ?? fallbackSpawn;
            newWorld.Spawn(id, snapped);
            Console.WriteLine($"    entity {id}: saved={savedPos}  snapped={snapped}");
        }

        for (int i = 0; i < 30; i++) newWorld.Update(0.016f);

        // ── Phase 9: UNLOCK — resume accepting move requests ──────────────────
        acceptingMoveRequests = true;
        Console.WriteLine($"  [Phase 9] Move requests unlocked  (flag={acceptingMoveRequests})");

        // Issue new moves to confirm the world is fully operational
        newWorld.Move(1, new Vector3(5f, 5f, 5f), speed: 5f);
        newWorld.Move(2, new Vector3(3f, 5f, 8f), speed: 5f);
        for (int i = 0; i < 200; i++) newWorld.Update(0.016f);

        Console.WriteLine($"  Hot-swap complete — entity 1 at {newWorld.GetPosition(1)}");
        Console.WriteLine($"                       entity 2 at {newWorld.GetPosition(2)}");

        newWorld.Dispose();
    }

    #endregion
}
