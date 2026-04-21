// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║          SPATIAL — EXAMPLE GAME SERVER                                       ║
// ║                                                                              ║
// ║  This file is a skeleton game server that shows how to integrate the         ║
// ║  Spatial physics + pathfinding system. Sections marked with:                 ║
// ║                                                                              ║
// ║    // TODO [GAME SERVER]: ...                                                ║
// ║                                                                              ║
// ║  are placeholders where you wire in your own game logic. Everything else     ║
// ║  is real Spatial API usage that you can copy as-is.                          ║
// ║                                                                              ║
// ║  Read INTEGRATION_GUIDE.md before editing this file.                         ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

using System.Numerics;
using System.Diagnostics;
using Spatial.Integration;
using Spatial.Pathfinding;
using Spatial.Physics;

namespace Spatial.TestHarness;

// ═════════════════════════════════════════════════════════════════════════════
// DATA TYPES — adjust or replace with your own
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Represents a connected player in your game.</summary>
public class PlayerSession
{
    public int     PlayerId    { get; init; }
    public string  PlayerName  { get; init; } = "";
    public Vector3 SpawnPoint  { get; init; }

    // TODO [GAME SERVER]: Add your own player state here.
    // Examples: team, class, equipment, health, mana, connection handle, etc.
}

/// <summary>A move command sent by the player (e.g. right-click on ground).</summary>
public class MoveCommand
{
    public int     PlayerId { get; init; }
    public Vector3 Target   { get; init; }
    public float   Speed    { get; init; } = 5f;
}

/// <summary>A request to use an ability that causes knockback (e.g. a spell hit).</summary>
public class KnockbackCommand
{
    public int     SourcePlayerId { get; init; }  // who caused it
    public int     TargetPlayerId { get; init; }  // who gets hit
    public Vector3 Direction      { get; init; }
    public float   Force          { get; init; } = 8f;
}

// TODO [GAME SERVER]: Add more command types for your abilities, spells, etc.
// Examples: StopCommand, JumpCommand, TeleportCommand (for GM tools), etc.

// ═════════════════════════════════════════════════════════════════════════════
// GAME ROOM — one active match / dungeon instance
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A single running game room. Each room owns one World instance with fully
/// isolated physics. Rooms sharing the same map reuse the same baked NavMesh.
/// </summary>
public class GameRoom : IDisposable
{
    // ── Spatial world ─────────────────────────────────────────────────────────

    private readonly World _world;
    private readonly float _tickRate = 0.016f;  // 60 Hz — change to 0.008f for 125 Hz

    // ── Room state ────────────────────────────────────────────────────────────

    private readonly int _roomId;
    private readonly Dictionary<int, PlayerSession> _players = new();
    private bool _isRunning;

    // TODO [GAME SERVER]: Add room-level state here.
    // Examples: round timer, score, monster list, loot table, room phase, etc.

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a game room using a pre-baked NavMesh.
    /// This is cheap — the expensive baking already happened at server startup.
    /// </summary>
    public GameRoom(int roomId, NavMeshData bakedNavMesh, AgentConfig agentConfig)
    {
        _roomId = roomId;
        _world  = new World(bakedNavMesh, agentConfig);

        // Subscribe to movement events before spawning any agents.
        _world.OnMovementStarted   += HandleMovementStarted;
        _world.OnDestinationReached += HandleDestinationReached;
        _world.OnPathReplanned     += HandlePathReplanned;
        _world.OnMovementProgress  += HandleMovementProgress;
    }

    // ── Player lifecycle ──────────────────────────────────────────────────────

    /// <summary>
    /// Called when a player joins this room (connection established, map loaded).
    /// </summary>
    public void PlayerJoin(PlayerSession player)
    {
        _players[player.PlayerId] = player;

        // Spawn the physics capsule at the player's assigned spawn point.
        // Spatial automatically snaps the position to the nearest NavMesh surface.
        _world.Spawn(player.PlayerId, player.SpawnPoint);

        // TODO [GAME SERVER]: Send initial game state to the joining player.
        // Examples: room layout, existing player positions, item locations, etc.

        Console.WriteLine($"[Room {_roomId}] Player {player.PlayerName} ({player.PlayerId}) joined.");
    }

    /// <summary>
    /// Called when a player leaves (disconnect, logout, death with no respawn).
    /// </summary>
    public void PlayerLeave(int playerId)
    {
        if (!_players.ContainsKey(playerId)) return;

        // Remove the physics capsule. Spatial stops movement before removing the body.
        _world.Despawn(playerId);
        _players.Remove(playerId);

        // TODO [GAME SERVER]: Notify remaining players that this player left.
        // TODO [GAME SERVER]: Save player progress / inventory to your database.

        Console.WriteLine($"[Room {_roomId}] Player {playerId} left.");
    }

    // ── Command handlers ──────────────────────────────────────────────────────

    /// <summary>
    /// Processes a move command issued by a player (e.g. click-to-move).
    /// Call this before world.Update() in the same tick.
    /// </summary>
    public void HandleMoveCommand(MoveCommand cmd)
    {
        // TODO [GAME SERVER]: Validate the command before applying it.
        // Examples: check the player is alive, not stunned, not in a cinematic, etc.

        var response = _world.Move(cmd.PlayerId, cmd.Target, cmd.Speed);

        if (!response.Success)
        {
            // No walkable path found — tell the player their click was invalid.
            // TODO [GAME SERVER]: Send a "cannot move there" response to the client.
            Console.WriteLine($"[Room {_roomId}] Player {cmd.PlayerId}: no path to target.");
            return;
        }

        if (response.WasTargetAdjusted)
        {
            // Target was unreachable but Spatial found a nearby valid point.
            // Optionally notify the client so it can show the adjusted destination.
            Console.WriteLine($"[Room {_roomId}] Player {cmd.PlayerId}: target adjusted — {response.AdjustmentReason}");

            // TODO [GAME SERVER]: Send adjusted destination to client for visual feedback.
        }

        // TODO [GAME SERVER]: Broadcast move confirmation to all players in the room
        //   so each client can start animating the mover toward the destination.
        //   Include: response.ActualTarget, response.EstimatedTime
    }

    /// <summary>
    /// Processes a knockback ability (e.g. spell hits a target player).
    /// </summary>
    public void HandleKnockbackCommand(KnockbackCommand cmd)
    {
        // TODO [GAME SERVER]: Validate the knockback (source has the ability, target is valid, etc.)
        // TODO [GAME SERVER]: Apply damage, crowd-control flags, or status effects.

        // Add a slight upward component so the arc looks natural.
        var dir = Vector3.Normalize(cmd.Direction + new Vector3(0, 0.3f, 0));
        _world.Knockback(cmd.TargetPlayerId, dir, cmd.Force);

        Console.WriteLine($"[Room {_roomId}] Player {cmd.TargetPlayerId} knocked back by {cmd.SourcePlayerId}.");

        // TODO [GAME SERVER]: Broadcast the knockback event to clients for visual effects.
    }

    // ── Main simulation loop ──────────────────────────────────────────────────

    /// <summary>
    /// Runs the room simulation until the room ends.
    /// In a production server, run this on a dedicated thread or Task per room.
    /// </summary>
    public void RunLoop()
    {
        _isRunning = true;

        // Let physics settle after the first wave of spawns.
        // This lets gravity push capsules onto the ground before movement starts.
        for (int i = 0; i < 20; i++)
            _world.Update(_tickRate);

        var stopwatch = Stopwatch.StartNew();

        while (_isRunning)
        {
            // ── 1. Collect commands from your networking layer ─────────────────

            // TODO [GAME SERVER]: Replace this with commands from your actual network layer.
            // Examples of what goes here:
            //   var commands = _networkLayer.DequeueCommandsForThisTick();
            //   foreach (var cmd in commands) { ... }
            var moveCommands     = GetPendingMoveCommands();
            var knockbackCommands = GetPendingKnockbackCommands();

            // ── 2. Apply commands ─────────────────────────────────────────────

            foreach (var cmd in moveCommands)
                HandleMoveCommand(cmd);

            foreach (var cmd in knockbackCommands)
                HandleKnockbackCommand(cmd);

            // TODO [GAME SERVER]: Handle other command types here (Jump, UseAbility, etc.)

            // ── 3. Tick the simulation ────────────────────────────────────────

            // This advances physics and pathfinding by one fixed timestep.
            // Movement first, then physics — the order is enforced internally.
            _world.Update(_tickRate);

            // ── 4. Read authoritative positions ──────────────────────────────

            var snapshot = BuildStateSnapshot();

            // ── 5. Broadcast state to clients ─────────────────────────────────

            // TODO [GAME SERVER]: Send snapshot to all connected clients.
            // Each client uses the positions for interpolation/rendering.
            // Include at minimum: entityId, position, velocity, characterState.
            BroadcastSnapshot(snapshot);

            // ── 6. Run game logic ─────────────────────────────────────────────

            // TODO [GAME SERVER]: Run your server-side game logic here.
            // Examples: check win conditions, tick status effects, spawn mobs,
            //           check aggro ranges, advance quest states, etc.
            RunGameLogic();

            // ── 7. Frame rate regulation ──────────────────────────────────────

            // Busy-wait is fine for demo purposes.
            // In production use a timer or Thread.Sleep with accumulator.
            var targetElapsed = TimeSpan.FromSeconds(_tickRate);
            while (stopwatch.Elapsed < targetElapsed) { /* spin */ }
            stopwatch.Restart();
        }
    }

    /// <summary>Signals the simulation loop to stop.</summary>
    public void Stop() => _isRunning = false;

    // ── State snapshot ────────────────────────────────────────────────────────

    private RoomSnapshot BuildStateSnapshot()
    {
        var entities = new List<EntitySnapshot>();

        foreach (var (playerId, _) in _players)
        {
            entities.Add(new EntitySnapshot
            {
                EntityId = playerId,
                Position = _world.GetPosition(playerId),
                Velocity = _world.GetVelocity(playerId),
                State    = _world.GetState(playerId),
            });
        }

        // TODO [GAME SERVER]: Add non-player entities here (NPCs, monsters, projectiles).

        return new RoomSnapshot { Entities = entities };
    }

    private void BroadcastSnapshot(RoomSnapshot snapshot)
    {
        // TODO [GAME SERVER]: Serialize the snapshot and send it to all clients.
        // Typical approach: JSON or a binary protocol, sent over UDP or WebSocket.
        //
        // Example fields to include per entity:
        //   snapshot.Entities[i].EntityId   — who this data belongs to
        //   snapshot.Entities[i].Position   — authoritative server position
        //   snapshot.Entities[i].Velocity   — for client-side interpolation
        //   snapshot.Entities[i].State      — GROUNDED / AIRBORNE / RECOVERING
        //                                     (drives animation state machine on client)
    }

    // ── Game logic tick ───────────────────────────────────────────────────────

    private void RunGameLogic()
    {
        // TODO [GAME SERVER]: This runs every server tick after positions are updated.
        // Put your game-specific logic here. Examples:

        // ── Win / loss condition check ─────────────────────────────────────
        // if (CheckWinCondition()) { Stop(); NotifyClients("game_over"); }

        // ── Status effect ticks (poison, burn, slow) ───────────────────────
        // foreach (var effect in activeStatusEffects) { effect.Tick(_tickRate); }

        // ── NPC/mob AI tick ────────────────────────────────────────────────
        // foreach (var npc in _npcs) {
        //     if (npc.ShouldChaseTarget()) {
        //         var target = FindNearestPlayer(npc.Position);
        //         _world.Move(npc.EntityId, target.Position, speed: npc.MoveSpeed);
        //     }
        // }

        // ── Proximity / trigger checks ─────────────────────────────────────
        // foreach (var trigger in _triggers) {
        //     foreach (var (playerId, _) in _players) {
        //         var pos = _world.GetPosition(playerId);
        //         if (trigger.Contains(pos)) trigger.OnEnter(playerId);
        //     }
        // }
    }

    // ── Movement event handlers ───────────────────────────────────────────────

    private void HandleMovementStarted(int entityId, Vector3 start, Vector3 target)
    {
        // Fired when a unit begins moving along a newly planned path.
        // start and target are NavMesh-snapped positions (may differ from original click).

        // TODO [GAME SERVER]: Notify the moving player's client of the adjusted positions
        //   so it can draw the movement indicator at the correct location.

        Console.WriteLine($"[Room {_roomId}] Entity {entityId} started moving → {target}");
    }

    private void HandleDestinationReached(int entityId, Vector3 finalPos)
    {
        // Fired when a unit arrives at its destination.

        // TODO [GAME SERVER]: Trigger destination-dependent logic here. Examples:
        //   — Player reached an NPC: start dialogue
        //   — Player reached a quest object: progress the quest
        //   — NPC reached patrol point: start idle timer, pick next waypoint
        //   — Monster reached player: begin melee attack sequence

        Console.WriteLine($"[Room {_roomId}] Entity {entityId} arrived at {finalPos}");
    }

    private void HandlePathReplanned(int entityId)
    {
        // Fired when Spatial automatically replanned a path because the original route
        // became invalid (dynamic obstacle, terrain change, etc.).
        // You do NOT need to re-issue a Move() — the agent is already on the new path.

        // TODO [GAME SERVER]: Optionally send the updated path to the client for visualization.

        Console.WriteLine($"[Room {_roomId}] Entity {entityId} path was replanned.");
    }

    private void HandleMovementProgress(int entityId, float fraction)
    {
        // Fired each time the agent advances to the next waypoint.
        // fraction: 0.0 = just started, 1.0 = arrived.

        // TODO [GAME SERVER]: Use this for progress bars, sound cues, or camera tracking.
        //   Example: play a footstep sound when fraction crosses 0.5 (halfway).
    }

    // ── Pending command queues (replace with your networking layer) ───────────

    private List<MoveCommand> GetPendingMoveCommands()
    {
        // TODO [GAME SERVER]: Return commands that arrived from clients this tick.
        // This is where your UDP/TCP/WebSocket input queue feeds into the simulation.
        return new List<MoveCommand>();
    }

    private List<KnockbackCommand> GetPendingKnockbackCommands()
    {
        // TODO [GAME SERVER]: Return knockback events generated by ability processing.
        return new List<KnockbackCommand>();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        // Releases BepuPhysics unmanaged memory.
        // Always call this (or use `using`) when a room closes.
        _world.Dispose();
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// SNAPSHOT DATA TYPES — replace with your own serializable types
// ═════════════════════════════════════════════════════════════════════════════

public class RoomSnapshot
{
    public List<EntitySnapshot> Entities { get; set; } = new();
}

public class EntitySnapshot
{
    public int            EntityId { get; set; }
    public Vector3        Position { get; set; }
    public Vector3        Velocity { get; set; }
    public CharacterState State    { get; set; }

    // TODO [GAME SERVER]: Add game-specific fields.
    // Examples: Health, AnimationState, Team, IsCasting, etc.
}

// ═════════════════════════════════════════════════════════════════════════════
// GAME SERVER — top-level lifecycle
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Top-level game server. Manages NavMesh baking at startup and room lifecycle.
/// </summary>
public static class MyGameServer
{
    // ── Shared NavMesh cache (baked once per map at startup) ──────────────────

    private static readonly Dictionary<string, NavMeshData> _bakedNavMeshes = new();

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Example entry point — shows startup, room creation, and shutdown.
    /// Adapt this to match your server's actual lifecycle (e.g. as a hosted service).
    /// </summary>
    public static void Run(string meshPath)
    {
        Console.WriteLine("=== MyGameServer starting ===");

        // ── Step 1: Server startup — bake NavMeshes ───────────────────────────

        // Define the agent configuration for your main player character type.
        // Height and Radius must match the actual character model size.
        var playerConfig = new AgentConfig
        {
            Radius   = 0.4f,   // capsule radius (meters)
            Height   = 2.0f,   // capsule cylinder height (meters)
            MaxClimb = 0.5f,   // maximum step the agent can step over
            MaxSlope = 45.0f,  // maximum incline angle
        };

        // TODO [GAME SERVER]: Define a config per unit archetype if they differ in size.
        //   Examples: a large boss creature, a small critter, a mount.
        //   Each needs its own NavMesh bake because the walkable area differs.

        // Bake is expensive (100–500 ms) — do this once at startup, not per room.
        Console.WriteLine("Baking NavMesh...");
        var bakedNavMesh = World.BakeNavMesh(meshPath, playerConfig);
        _bakedNavMeshes["arena"] = bakedNavMesh;
        Console.WriteLine("NavMesh ready.");

        // TODO [GAME SERVER]: Start your networking layer here.
        //   Examples: listen for TCP connections, start a WebSocket server, etc.

        // ── Step 2: Room lifecycle ─────────────────────────────────────────────

        // Simulate a room opening with two players joining.
        RunExampleRoom(bakedNavMesh, playerConfig);

        // TODO [GAME SERVER]: In production, rooms open/close as matches start/end.
        //   Each room runs on its own thread or Task, sharing the baked NavMesh.

        Console.WriteLine("=== MyGameServer shutdown ===");
    }

    private static void RunExampleRoom(NavMeshData bakedNavMesh, AgentConfig agentConfig)
    {
        Console.WriteLine("\n--- Opening room 1 ---");

        // Create an isolated world for this room.
        // Cheap: reuses the pre-baked NavMesh, only creates new physics sim.
        using var room = new GameRoom(roomId: 1, bakedNavMesh, agentConfig);

        // ── Players join ──────────────────────────────────────────────────────

        // TODO [GAME SERVER]: Replace hard-coded players with real connection data.
        //   SpawnPoint should come from your map's spawn configuration.
        room.PlayerJoin(new PlayerSession
        {
            PlayerId   = 101,
            PlayerName = "Alice",
            SpawnPoint = new Vector3(-18f, 5f, -18f),
        });
        room.PlayerJoin(new PlayerSession
        {
            PlayerId   = 102,
            PlayerName = "Bob",
            SpawnPoint = new Vector3(18f, 5f, 18f),
        });

        // ── Issue some example commands ───────────────────────────────────────

        // Simulate 60 ticks of gameplay (1 second at 60 Hz)
        const float DT    = 0.016f;
        const int   TICKS = 60;

        // Let physics settle first (gravity places capsules on the floor)
        for (int i = 0; i < 20; i++)
        {
            // world.Update() is called inside RunLoop(); here we tick manually for demo
        }

        // In production, RunLoop() runs the loop. Here we demonstrate direct API use:
        var world = GetWorldForDemo(bakedNavMesh, agentConfig);

        world.Spawn(101, new Vector3(-18f, 5f, -18f));
        world.Spawn(102, new Vector3(18f, 5f, 18f));

        // Let gravity settle
        for (int i = 0; i < 20; i++) world.Update(DT);

        // Issue move orders
        var r1 = world.Move(101, new Vector3(0f, 0f, 0f), speed: 5f);
        var r2 = world.Move(102, new Vector3(-5f, 0f, -5f), speed: 4f);

        Console.WriteLine($"Player 101 move: success={r1.Success}, ETA={r1.EstimatedTime:F1}s");
        Console.WriteLine($"Player 102 move: success={r2.Success}, ETA={r2.EstimatedTime:F1}s");

        // Simulate TICKS ticks and print positions
        for (int tick = 0; tick < TICKS; tick++)
        {
            world.Update(DT);

            if (tick % 20 == 0)
            {
                var pos101 = world.GetPosition(101);
                var pos102 = world.GetPosition(102);
                var st101  = world.GetState(101);
                Console.WriteLine($"  tick {tick:D3} | 101: {pos101:F1} [{st101}] | 102: {pos102:F1}");
            }
        }

        // Knockback example: player 101 knocks back player 102
        var from = world.GetPosition(101);
        var to   = world.GetPosition(102);
        var dir  = Vector3.Normalize(to - from + new Vector3(0, 0.3f, 0));
        world.Knockback(102, dir, force: 6f);
        Console.WriteLine("Applied knockback to player 102.");

        // Run a few more ticks to observe the knockback arc
        for (int i = 0; i < 30; i++) world.Update(DT);

        var finalState = world.GetState(102);
        Console.WriteLine($"Player 102 state after knockback arc: {finalState}");

        world.Despawn(101);
        world.Despawn(102);
        world.Dispose();

        Console.WriteLine("--- Room 1 closed ---\n");
    }

    // Helper for the inline demo above — in production you'd use GameRoom.
    private static World GetWorldForDemo(NavMeshData navMesh, AgentConfig config)
    {
        var world = new World(navMesh, config);
        world.OnDestinationReached += (id, pos) =>
            Console.WriteLine($"  >> Entity {id} reached destination {pos:F1}");
        return world;
    }
}
