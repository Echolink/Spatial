using System.Numerics;
using Spatial.Physics;
using Spatial.Pathfinding;
using Spatial.MeshLoading;

namespace Spatial.Integration;

/// <summary>
/// High-level façade that bundles a complete physics + pathfinding world into a single,
/// easy-to-use object for game servers.
///
/// Instead of wiring up PhysicsWorld, NavMeshBuilder, Pathfinder, PathfindingService,
/// MotorCharacterController, and MovementController by hand, a game server creates a
/// World in two lines:
///
///   var baked = World.BakeNavMesh("worlds/arena.obj", agentConfig);
///   using var world = new World(baked, agentConfig);
///
/// The baking step is intentionally separate — it is CPU-intensive (100–500 ms) and the
/// resulting NavMeshData can be reused across many simultaneous World instances (e.g.
/// all rooms running the same map share one bake).
///
/// WORLD FILE LOCATION:
///   Place .obj mesh files in:  Spatial.TestHarness/worlds/
///   They are copied to the output folder by the .csproj <None CopyToOutputDirectory/> item.
///   At runtime resolve the path with:
///     Path.Combine(AppContext.BaseDirectory, "worlds", "my_map.obj")
///
/// TYPICAL GAME SERVER WORKFLOW:
///   1. At startup  — call World.BakeNavMesh() per map, store the NavMeshData.
///   2. Room opens  — new World(baked, agentConfig).
///   3. Player joins — world.Spawn(playerId, spawnPosition).
///   4. Player moves — world.Move(playerId, clickedPosition).
///   5. Every tick  — world.Update(fixedDeltaTime).
///   6. Player leaves — world.Despawn(playerId).
///   7. Room closes — world.Dispose().
/// </summary>
public sealed class World : IDisposable
{
    // ── Private state ─────────────────────────────────────────────────────────

    private readonly AgentConfig _agentConfig;

    // Per-config PathfindingServices — populated by the multi-size constructor.
    private readonly Dictionary<AgentConfig, PathfindingService> _pathfindingServices =
        new(ReferenceEqualityComparer.Instance);

    // Maps entityId → AgentConfig so Spawn can route to the right service.
    private readonly Dictionary<int, AgentConfig> _entityConfigs = new();

    // Maps entityId → PhysicsEntity so we can look up bodies for queries.
    private readonly Dictionary<int, PhysicsEntity> _entities = new();

    // Tracks spawned obstacles for NavMesh restoration on despawn.
    private readonly Dictionary<int, ObstacleRecord> _obstacles = new();

    // ── Public subsystem access (escape hatches for advanced use) ─────────────

    /// <summary>Direct access to the BepuPhysics v2 wrapper for advanced operations.</summary>
    public PhysicsWorld Physics { get; }

    /// <summary>Direct access to the movement/pathfinding orchestrator.</summary>
    public MovementController Movement { get; }

    /// <summary>Direct access to path queries and NavMesh snapping.</summary>
    public PathfindingService Pathfinding { get; }

    /// <summary>The NavMeshData this world was built from.</summary>
    public NavMeshData NavMesh { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when a unit starts moving.
    /// Parameters: (entityId, actualStartPosition, actualTargetPosition)
    /// Note: positions are NavMesh-snapped and may differ from the original request.
    /// </summary>
    public event Action<int, Vector3, Vector3>? OnMovementStarted;

    /// <summary>
    /// Fired when a unit reaches its destination.
    /// Use this to trigger idle AI, dialogue, quest rewards, etc.
    /// Parameters: (entityId, finalPosition)
    /// </summary>
    public event Action<int, Vector3>? OnDestinationReached;

    /// <summary>
    /// Fired when the system automatically replans a path around an obstacle.
    /// The game server does NOT need to re-issue a Move() call — replanning is automatic.
    /// Parameters: (entityId)
    /// </summary>
    public event Action<int>? OnPathReplanned;

    /// <summary>
    /// Fired each time a unit advances to the next waypoint.
    /// Fraction is 0.0 (just started) → 1.0 (arrived).
    /// Use for progress bars or smooth client-side animation sync.
    /// Parameters: (entityId, progressFraction)
    /// </summary>
    public event Action<int, float>? OnMovementProgress;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a live world instance from pre-baked NavMesh data.
    ///
    /// This constructor is cheap — it creates a new PhysicsWorld and wires up
    /// all subsystems, but does NOT load or process any mesh geometry.
    /// Use World.BakeNavMesh() separately (once per map) to produce the NavMeshData.
    ///
    /// MULTIPLE INSTANCES OF THE SAME MAP:
    ///   var baked = World.BakeNavMesh("worlds/dungeon.obj", config);
    ///   var room1  = new World(baked, config);  // dungeon instance A
    ///   var room2  = new World(baked, config);  // dungeon instance B
    ///   // room1 and room2 have fully isolated physics; baked is read-only.
    /// </summary>
    /// <param name="navMeshData">Pre-baked NavMesh. Reuse across multiple World instances.</param>
    /// <param name="agentConfig">
    ///   Agent dimensions (Height, Radius, MaxClimb, MaxSlope). This is the single source
    ///   of truth — the same values are used for NavMesh clearance checks, path validation,
    ///   and physics capsule sizing. Never create separate configs per subsystem.
    /// </param>
    /// <param name="pfConfig">Optional pathfinding tuning (avoidance, replanning, thresholds).</param>
    /// <param name="physicsConfig">Optional physics tuning (gravity, timestep).</param>
    public World(NavMeshData navMeshData, AgentConfig agentConfig,
                 PathfindingConfiguration? pfConfig = null,
                 PhysicsConfiguration? physicsConfig = null)
    {
        _agentConfig = agentConfig;
        NavMesh = navMeshData;

        // Create an isolated physics simulation for this world instance.
        Physics = new PhysicsWorld(physicsConfig ?? new PhysicsConfiguration());

        // Build pathfinding stack on top of the shared (read-only) NavMeshData.
        var pathfinder = new Pathfinder(navMeshData);
        var config = pfConfig ?? new PathfindingConfiguration();
        Pathfinding = new PathfindingService(pathfinder, agentConfig, config);

        // MotorCharacterController is the production standard — uses BepuPhysics constraint
        // solver for smooth movement (32% faster, zero replanning vs velocity-based controller).
        var motorController = new MotorCharacterController(Physics);
        Movement = new MovementController(Physics, Pathfinding, agentConfig, config, motorController);

        // Forward movement events to our own event surface.
        Movement.OnMovementStarted  += (id, s, t) => OnMovementStarted?.Invoke(id, s, t);
        Movement.OnDestinationReached += (id, p)  => OnDestinationReached?.Invoke(id, p);
        Movement.OnPathReplanned     += id         => OnPathReplanned?.Invoke(id);
        Movement.OnMovementProgress  += (id, f)   => OnMovementProgress?.Invoke(id, f);
    }

    /// <summary>
    /// Creates a world that supports multiple agent sizes.
    ///
    /// Each AgentConfig registered in <paramref name="multiNavMesh"/> gets its own
    /// NavMesh and PathfindingService. Pass the same AgentConfig instance to
    /// <see cref="Spawn(int, Vector3, AgentConfig, EntityType)"/> at runtime to route
    /// each unit to its correct NavMesh.
    ///
    ///   var navMesh = new MultiAgentNavMesh("worlds/arena.obj")
    ///       .Add(goblinConfig)
    ///       .Add(trollConfig)
    ///       .Bake();
    ///   using var world = new World(navMesh);
    ///   world.Spawn(goblinId, pos, goblinConfig);
    ///   world.Spawn(trollId,  pos, trollConfig);
    /// </summary>
    public World(MultiAgentNavMesh multiNavMesh,
                 PathfindingConfiguration? pfConfig = null,
                 PhysicsConfiguration? physicsConfig = null)
    {
        _agentConfig = multiNavMesh.DefaultConfig;
        NavMesh = multiNavMesh.NavMeshes[_agentConfig];

        Physics = new PhysicsWorld(physicsConfig ?? new PhysicsConfiguration());

        var config = pfConfig ?? new PathfindingConfiguration();

        // Build one PathfindingService per registered config.
        PathfindingService? defaultSvc = null;
        foreach (var (agentConfig, navMeshData) in multiNavMesh.NavMeshes)
        {
            var svc = new PathfindingService(new Pathfinder(navMeshData), agentConfig, config);
            _pathfindingServices[agentConfig] = svc;
            if (agentConfig == _agentConfig)
                defaultSvc = svc;
        }

        Pathfinding = defaultSvc!;

        var motorController = new MotorCharacterController(Physics);
        Movement = new MovementController(Physics, Pathfinding, _agentConfig, config, motorController);

        Movement.OnMovementStarted   += (id, s, t) => OnMovementStarted?.Invoke(id, s, t);
        Movement.OnDestinationReached += (id, p)   => OnDestinationReached?.Invoke(id, p);
        Movement.OnPathReplanned      += id         => OnPathReplanned?.Invoke(id);
        Movement.OnMovementProgress   += (id, f)   => OnMovementProgress?.Invoke(id, f);
    }

    // ── Static factory helpers ────────────────────────────────────────────────

    /// <summary>
    /// Bakes a navigation mesh from an OBJ mesh file.
    ///
    /// This is the expensive step (100–500 ms depending on mesh complexity). Call it
    /// once at server startup per map version, then pass the result to new World(...)
    /// for every room/instance that uses that map.
    ///
    /// BAKING PROCESS:
    ///   1. Loads the .obj file via MeshLoader.
    ///   2. Registers geometry into a temporary PhysicsWorld (disposed immediately after).
    ///   3. Voxelises the geometry and runs DotRecast to produce walkable polygons.
    ///   4. Returns a NavMeshData object — lightweight, read-only, safe to share.
    ///
    /// TILED VS MONOLITHIC:
    ///   Pass a NavMeshConfiguration with EnableTileUpdates=true to get a tile-based
    ///   NavMesh that supports runtime updates (opening doors, collapsing bridges).
    ///   The default (null) produces a monolithic NavMesh — simpler, slightly faster queries.
    /// </summary>
    /// <param name="meshFilePath">Absolute path to the .obj file.</param>
    /// <param name="agentConfig">Agent dimensions — must match the config used when creating World.</param>
    /// <param name="navConfig">Optional tile configuration for runtime NavMesh updates.</param>
    public static NavMeshData BakeNavMesh(string meshFilePath, AgentConfig agentConfig,
                                          NavMeshConfiguration? navConfig = null)
    {
        // A temporary PhysicsWorld is used only to load the mesh and extract vertex data.
        // It is disposed immediately — only the baked NavMeshData is kept.
        using var tempPhysics = new PhysicsWorld();
        var worldBuilder = new WorldBuilder(tempPhysics, new MeshLoader());
        worldBuilder.LoadAndBuildWorld(meshFilePath, null);

        var builder = new NavMeshBuilder(tempPhysics, new NavMeshGenerator());
        return navConfig?.EnableTileUpdates == true
            ? builder.BuildTiledNavMeshDirect(agentConfig, navConfig)
            : builder.BuildNavMeshDirect(agentConfig);
    }

    /// <summary>
    /// Convenience factory: bakes NavMesh and creates a World in one call.
    ///
    /// Equivalent to:
    ///   var baked = World.BakeNavMesh(path, config);
    ///   var world = new World(baked, config);
    ///
    /// Use this for simple single-instance scenarios. For multi-room servers,
    /// prefer calling BakeNavMesh() once and passing the result to each new World().
    /// </summary>
    public static World CreateFromFile(string meshFilePath, AgentConfig agentConfig,
                                       PathfindingConfiguration? pfConfig = null,
                                       NavMeshConfiguration? navConfig = null)
    {
        var navMesh = BakeNavMesh(meshFilePath, agentConfig, navConfig);
        return new World(navMesh, agentConfig, pfConfig);
    }

    // ── Core Loop ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances the simulation by one tick. Call this every server frame.
    ///
    /// USE A FIXED TIMESTEP (e.g. 0.016f for 60 FPS, 0.008f for 125 FPS).
    /// A fixed timestep keeps the simulation deterministic — essential for
    /// server-authoritative multiplayer where all clients must agree on positions.
    ///
    /// UPDATE ORDER:
    ///   Movement is updated first (sets velocity goals), then physics integrates
    ///   those velocities into new positions. Reversing the order is incorrect.
    /// </summary>
    public void Update(float deltaTime)
    {
        Movement.UpdateMovement(deltaTime);  // 1. path-following sets velocity goals
        Physics.Update(deltaTime);           // 2. BepuPhysics integrates velocities → positions
    }

    // ── Agent Lifecycle ───────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a unit at the given world position.
    ///
    /// The position is automatically snapped to the nearest NavMesh surface and the
    /// capsule body is offset upward so the unit's feet land on that surface — the
    /// game server does not need to compute the Y offset manually.
    ///
    /// ENTITY ID:
    ///   IDs must be unique within this World instance but do NOT need to be globally
    ///   unique. Two separate World instances can each have an entity with id=1.
    /// </summary>
    /// <param name="entityId">Unique identifier for this unit within the world.</param>
    /// <param name="position">Approximate world position; snapped to NavMesh automatically.</param>
    /// <param name="entityType">Unit archetype (Player, NPC, etc.).</param>
    /// <returns>The created PhysicsEntity for advanced use (e.g. SetEntityPushable).</returns>
    public PhysicsEntity Spawn(int entityId, Vector3 position,
                               EntityType entityType = EntityType.Player)
        => SpawnInternal(entityId, position, _agentConfig, entityType);

    /// <summary>
    /// Spawns a unit with a specific agent size. Use this overload in multi-size worlds.
    ///
    /// <paramref name="agentConfig"/> must be the same instance that was passed to
    /// <see cref="MultiAgentNavMesh.Add"/> before baking — lookup uses reference equality.
    /// </summary>
    public PhysicsEntity Spawn(int entityId, Vector3 position,
                               AgentConfig agentConfig,
                               EntityType entityType = EntityType.Player)
        => SpawnInternal(entityId, position, agentConfig, entityType);

    private PhysicsEntity SpawnInternal(int entityId, Vector3 position,
                                        AgentConfig agentConfig,
                                        EntityType entityType)
    {
        // Resolve the PathfindingService for this config (falls back to default).
        var svc = _pathfindingServices.TryGetValue(agentConfig, out var s) ? s : Pathfinding;

        // Snap the requested position to the nearest NavMesh surface.
        // The search uses downward-priority: finds the highest surface below the point first.
        // This ensures consistent floor selection on multi-level maps (bridges, buildings).
        var snapped = svc.FindNearestValidPosition(position, new Vector3(5f, 10f, 5f))
                      ?? position;

        // BepuPhysics capsule shape: Height = cylinder length, Radius = hemisphere radius.
        // Total capsule height = Height + 2 * Radius.
        // Physics center Y = physics surface Y + (Height/2 + Radius) so feet touch the ground.
        float halfHeight = (agentConfig.Height / 2f) + agentConfig.Radius;

        // The navmesh Y and the physics mesh Y can diverge by ~0.2 m due to voxelisation.
        // Use a short downward raycast to find the actual physics surface beneath the snap point
        // so the capsule starts in contact with geometry rather than floating above it.
        float physicsGroundY = snapped.Y;
        if (Physics.Raycast(
                new Vector3(snapped.X, snapped.Y + 2f, snapped.Z),
                new Vector3(snapped.X, snapped.Y - 2f, snapped.Z),
                out var spawnHit, out _))
        {
            physicsGroundY = spawnHit.Y;
        }

        var center = new Vector3(snapped.X, physicsGroundY + halfHeight, snapped.Z);

        var (shape, inertia) = Physics.CreateCapsuleShapeWithInertia(
            agentConfig.Radius, agentConfig.Height, mass: 1f);

        var entity = Physics.RegisterEntityWithInertia(
            entityId, entityType, center, shape, inertia);

        _entities[entityId] = entity;
        _entityConfigs[entityId] = agentConfig;

        // Register the per-entity PathfindingService so MovementController routes
        // path queries to the NavMesh that was baked for this agent's dimensions.
        Movement.RegisterEntityService(entityId, svc);

        return entity;
    }

    /// <summary>
    /// Fully removes a unit from the world.
    ///
    /// DESPAWN ORDER (important — do not reverse):
    ///   1. StopMovement — clears path state and removes from character controller.
    ///   2. UnregisterEntity — removes the physics body.
    /// Calling UnregisterEntity while movement is active can leave dangling references.
    /// </summary>
    public void Despawn(int entityId)
    {
        // StopMovement internally calls characterController.RemoveEntity — handles both.
        Movement.StopMovement(entityId);

        if (_entities.TryGetValue(entityId, out var entity))
        {
            Physics.UnregisterEntity(entity);
            _entities.Remove(entityId);
            _entityConfigs.Remove(entityId);
        }
    }

    // ── Obstacle Lifecycle ────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a static obstacle (e.g. a tree resource node) at the given position.
    ///
    /// Two things happen:
    ///   1. A static box collider is added to the physics world so moving units are blocked.
    ///   2. The NavMesh tiles covering the obstacle footprint are rebuilt so the pathfinder
    ///      routes around the obstacle rather than through it.
    ///
    /// OVERLAP HANDLING:
    ///   If any units occupy the spawn footprint the behaviour depends on forceSpawn:
    ///   - false (default): spawn is rejected. SpawnResult.Spawned == false and
    ///     SpawnResult.DisplacedEntityIds lists the blocking unit IDs so the game server
    ///     can relocate them first, then retry.
    ///   - true: blocking units are pushed to the nearest point outside the obstacle's
    ///     footprint before the static body is created. Their movement is stopped;
    ///     the game server should re-issue Move() if they should resume pathing.
    ///     SpawnResult.DisplacedEntityIds lists every unit that was moved.
    ///
    /// NavMesh tile updates only occur when the world was baked with
    /// NavMeshConfiguration.EnableTileUpdates = true.
    /// </summary>
    /// <param name="entityId">Unique entity ID for this obstacle within the world.</param>
    /// <param name="position">World-space base position (bottom-centre of the obstacle).</param>
    /// <param name="size">Bounding box size (width, height, depth).</param>
    /// <param name="forceSpawn">
    ///   When true, push overlapping units out of the footprint and spawn unconditionally.
    ///   When false (default), reject the spawn if any unit would be overlapped.
    /// </param>
    public SpawnResult SpawnObstacle(int entityId, Vector3 position, Vector3 size,
                                     bool forceSpawn = false)
    {
        float checkRadius = MathF.Max(size.X, size.Z) * 0.5f + _agentConfig.Radius;
        var bodyCenter = new Vector3(position.X, position.Y + size.Y * 0.5f, position.Z);

        var overlapping = Physics.GetEntitiesInRadius(bodyCenter, checkRadius)
            .Where(e => !e.IsStatic && IsInsideObstacleFootprint(
                            Physics.GetEntityPosition(e), position, size))
            .ToList();

        if (overlapping.Count > 0 && !forceSpawn)
            return new SpawnResult(false, null,
                overlapping.Select(e => e.EntityId).ToList());

        var displaced = new List<int>();
        foreach (var unit in overlapping)
        {
            PushUnitOutOfObstacle(unit, position, size);
            displaced.Add(unit.EntityId);
        }

        var (shape, inertia) = Physics.CreateBoxShapeWithInertia(size, mass: 1f);
        var entity = Physics.RegisterEntityWithInertia(
            entityId, EntityType.Obstacle, bodyCenter, shape, inertia, isStatic: true);
        _entities[entityId] = entity;

        float navRadius = MathF.Max(size.X, size.Z) * 0.5f + 1f;
        _obstacles[entityId] = new ObstacleRecord(position, navRadius, entity);

        if (NavMesh.SourceVertices != null && NavMesh.NavConfig != null)
            Pathfinding.RebuildNavMeshRegion(position, navRadius, [], [], NavMesh.NavConfig);

        return new SpawnResult(true, entity, displaced);
    }

    // Returns true if the unit's XZ position falls inside the obstacle's footprint
    // (expanded by the agent radius to account for capsule width).
    private bool IsInsideObstacleFootprint(Vector3 unitPos, Vector3 obstacleBase, Vector3 size)
    {
        float halfX = size.X * 0.5f + _agentConfig.Radius;
        float halfZ = size.Z * 0.5f + _agentConfig.Radius;
        return MathF.Abs(unitPos.X - obstacleBase.X) < halfX
            && MathF.Abs(unitPos.Z - obstacleBase.Z) < halfZ;
    }

    // Pushes a unit to the nearest point just outside the obstacle's XZ footprint,
    // snaps to the NavMesh, and stops its current movement.
    private void PushUnitOutOfObstacle(PhysicsEntity unit, Vector3 obstacleBase, Vector3 size)
    {
        var unitPos = Physics.GetEntityPosition(unit);

        // Add agent radius + small clearance so the capsule edge clears the box.
        float halfX = size.X * 0.5f + _agentConfig.Radius + 0.1f;
        float halfZ = size.Z * 0.5f + _agentConfig.Radius + 0.1f;

        float dx = unitPos.X - obstacleBase.X;
        float dz = unitPos.Z - obstacleBase.Z;

        // Push along the axis of minimum penetration depth.
        float overlapX = halfX - MathF.Abs(dx);
        float overlapZ = halfZ - MathF.Abs(dz);

        static float SafeSign(float v) => v >= 0f ? 1f : -1f;

        var pushed = overlapX <= overlapZ
            ? new Vector3(unitPos.X + SafeSign(dx) * overlapX, unitPos.Y, unitPos.Z)
            : new Vector3(unitPos.X, unitPos.Y, unitPos.Z + SafeSign(dz) * overlapZ);

        var snapped = Pathfinding.FindNearestValidPosition(pushed, new Vector3(3f, 5f, 3f))
                      ?? pushed;
        float halfHeight = (_agentConfig.Height / 2f) + _agentConfig.Radius;
        var newCenter = new Vector3(snapped.X, snapped.Y + halfHeight, snapped.Z);

        Movement.StopMovement(unit.EntityId);
        Physics.SetEntityPosition(unit, newCenter);
    }

    /// <summary>
    /// Removes a previously spawned obstacle (e.g. a tree that was cut down).
    ///
    /// The physics collider is removed and the affected NavMesh tiles are rebuilt
    /// from the original source geometry, restoring walkability.
    /// </summary>
    public void DespawnObstacle(int entityId)
    {
        if (!_obstacles.TryGetValue(entityId, out var obs)) return;

        Physics.UnregisterEntity(obs.Entity);
        _entities.Remove(entityId);
        _obstacles.Remove(entityId);

        if (NavMesh.SourceVertices != null && NavMesh.NavConfig != null)
            Pathfinding.RebuildNavMeshRegion(
                obs.Position, obs.NavRadius,
                NavMesh.SourceVertices, NavMesh.SourceIndices!, NavMesh.NavConfig);
    }

    /// <summary>
    /// Instantly moves a unit to a new position — no path, no physics arc.
    ///
    /// Teleport:
    ///   - Stops any active movement.
    ///   - Snaps the target position to the nearest NavMesh surface.
    ///   - Directly sets the physics body position (immediate, one frame).
    ///
    /// Use for: respawn points, GM commands, checkpoint warps, cutscene placement.
    ///
    /// After teleporting, call Move() again if the unit should resume pathfinding:
    ///   world.Teleport(id, respawnPoint);
    ///   world.Move(id, questTarget);
    /// </summary>
    public void Teleport(int entityId, Vector3 position)
    {
        Movement.StopMovement(entityId);

        var snapped = Pathfinding.FindNearestValidPosition(position, new Vector3(5f, 10f, 5f))
                      ?? position;
        float halfHeight = (_agentConfig.Height / 2f) + _agentConfig.Radius;
        var center = new Vector3(snapped.X, snapped.Y + halfHeight, snapped.Z);

        if (_entities.TryGetValue(entityId, out var entity))
            Physics.SetEntityPosition(entity, center);
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Commands a unit to move to a target position via pathfinding.
    ///
    /// The system will:
    ///   1. Snap start and target to the NavMesh.
    ///   2. Find the shortest valid path (A* via DotRecast).
    ///   3. Validate the path against MaxClimb/MaxSlope constraints.
    ///   4. Auto-fix invalid segments by inserting intermediate waypoints.
    ///   5. Begin physics-driven movement along the waypoints.
    ///
    /// The returned MovementResponse includes:
    ///   - Success / failure reason
    ///   - Snapped start and target positions
    ///   - Estimated path length (meters) and travel time (seconds)
    ///   - The full waypoint list
    ///
    /// If the path becomes blocked at runtime, the system replans automatically
    /// (fires OnPathReplanned). The game server does NOT need to call Move() again.
    /// </summary>
    /// <param name="entityId">The unit to move.</param>
    /// <param name="target">Destination in world space; snapped to NavMesh automatically.</param>
    /// <param name="speed">Movement speed in meters per second.</param>
    public MovementResponse Move(int entityId, Vector3 target, float speed = 5f)
    {
        // Use per-entity config so movement grounding uses the correct capsule dimensions.
        var cfg = _entityConfigs.TryGetValue(entityId, out var c) ? c : _agentConfig;
        return Movement.RequestMovement(new MovementRequest(
            entityId, target, speed, cfg.Height, cfg.Radius));
    }

    /// <summary>
    /// Stops a unit's movement without removing it from the world.
    /// The unit stays at its current position and can be moved again with Move().
    /// </summary>
    public void StopMove(int entityId) => Movement.StopMovement(entityId);

    /// <summary>
    /// Applies a physics impulse to a unit (knockback, explosion, ability hit).
    ///
    /// Unlike a soft push, Knockback:
    ///   - Forces the unit into AIRBORNE state, pausing pathfinding.
    ///   - Lets physics govern the arc naturally (gravity, collisions).
    ///   - Auto-replans on landing once the unit returns to GROUNDED state.
    ///
    /// TIP: Include a small upward component for a natural arc:
    ///   var dir = Vector3.Normalize(new Vector3(dx, 0.3f, dz));
    ///   world.Knockback(targetId, dir, force: 8f);
    ///
    /// After knockback, poll GetState(id) == GROUNDED before issuing a new Move().
    /// </summary>
    public void Knockback(int entityId, Vector3 direction, float force)
        => Movement.Knockback(entityId, direction, force);

    /// <summary>
    /// Makes a unit jump if it is currently grounded.
    /// Returns true if the jump was applied (false if the unit is airborne).
    /// Pathfinding pauses during the jump and resumes automatically on landing.
    /// </summary>
    public bool Jump(int entityId, float jumpForce = 5f)
        => Movement.Jump(entityId, jumpForce);

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the authoritative world-space position of a unit.
    /// Send this to clients for position synchronisation.
    /// </summary>
    public Vector3 GetPosition(int entityId)
    {
        if (_entities.TryGetValue(entityId, out var entity))
            return Physics.GetEntityPosition(entity);
        return Vector3.Zero;
    }

    /// <summary>
    /// Returns the current velocity of a unit (meters per second).
    /// Useful for client-side animation blending or lag compensation.
    /// </summary>
    public Vector3 GetVelocity(int entityId)
    {
        if (_entities.TryGetValue(entityId, out var entity))
            return Physics.GetEntityVelocity(entity);
        return Vector3.Zero;
    }

    /// <summary>
    /// Returns the character state of a unit.
    ///
    ///   GROUNDED   — standing on terrain; pathfinding is active.
    ///   AIRBORNE   — falling or knocked back; pathfinding is paused.
    ///   RECOVERING — just landed; stabilising before pathfinding resumes.
    ///
    /// Use this to gate game abilities (e.g. cannot cast a spell while AIRBORNE)
    /// or trigger landing animations (transition from AIRBORNE → RECOVERING).
    /// </summary>
    public CharacterState GetState(int entityId)
    {
        if (_entities.TryGetValue(entityId, out var entity))
            return Movement.GetCharacterState(entity);
        return CharacterState.GROUNDED;
    }

    /// <summary>
    /// Returns true if the position is on the walkable NavMesh.
    /// Use this to validate spawn points or click targets before acting on them.
    /// </summary>
    public bool IsValidPosition(Vector3 position)
        => Pathfinding.IsValidPosition(position);

    /// <summary>
    /// Finds the nearest walkable NavMesh position to the given point.
    ///
    /// Uses downward-priority search: looks below the point first (gravity-aligned),
    /// then above. This ensures the correct floor is selected on multi-level maps
    /// (bridges, buildings) — the highest surface below the search point wins.
    ///
    /// Returns null if no walkable surface is found within the default search radius.
    /// </summary>
    public Vector3? SnapToNavMesh(Vector3 position)
        => Pathfinding.FindNearestValidPosition(position, new Vector3(5f, 10f, 5f));

    // ── Private types ─────────────────────────────────────────────────────────

    private readonly record struct ObstacleRecord(Vector3 Position, float NavRadius, PhysicsEntity Entity);

/// <summary>
/// Result of a <see cref="World.SpawnObstacle"/> call.
/// </summary>
/// <param name="Spawned">True if the obstacle was created.</param>
/// <param name="Entity">The created physics entity, or null if spawn was rejected.</param>
/// <param name="DisplacedEntityIds">
///   IDs of units that overlapped the footprint.
///   If Spawned is false these are the blockers that prevented the spawn.
///   If Spawned is true these are units that were pushed out (forceSpawn path).
/// </param>
public readonly record struct SpawnResult(
    bool Spawned,
    PhysicsEntity? Entity,
    IReadOnlyList<int> DisplacedEntityIds);

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>
    /// Releases the BepuPhysics simulation and all associated memory.
    /// Always dispose a World when the game room/instance ends.
    /// </summary>
    public void Dispose() => Physics.Dispose();
}
