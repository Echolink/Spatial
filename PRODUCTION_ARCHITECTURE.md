# Production Architecture Guide

**Last Updated**: 2026-01-26  
**Status**: ✅ Production Ready  
**Decision**: Motor-Based Character Controller

---

## Executive Decision

**✅ USE: MotorCharacterController** (Production Standard)  
**⚠️ BACKUP ONLY: CharacterController** (Legacy velocity-based)

After comprehensive testing in Phase 4, the motor-based approach has been adopted as the production architecture for all character movement in the Spatial project.

---

## Quick Start: Production Setup

### Recommended Configuration

```csharp
using Spatial.Integration;
using Spatial.Pathfinding;
using Spatial.Physics;

// 1. Create physics world
var physicsConfig = new PhysicsConfiguration
{
    Gravity = new Vector3(0, -9.81f, 0),
    Timestep = 0.016f
};
var physicsWorld = new PhysicsWorld(physicsConfig);

// 2. Create agent configuration (single source of truth)
var agentConfig = new AgentConfig
{
    Height = 2.0f,
    Radius = 0.4f,
    MaxSlope = 45.0f,
    MaxClimb = 0.5f  // Critical for path validation
};

// 3. Build NavMesh
var navMeshGenerator = new NavMeshGenerator();
var navMeshBuilder = new NavMeshBuilder(physicsWorld, navMeshGenerator);
var navMeshData = navMeshBuilder.BuildNavMeshDirect(agentConfig);

// 4. Create pathfinder and pathfinding service
var pathfinder = new Pathfinder(navMeshData);
var pathfindingConfig = new PathfindingConfiguration
{
    PathAutoFix = true,       // ✅ Keep enabled
    PathValidation = true     // ✅ Keep enabled
};
var pathfindingService = new PathfindingService(
    pathfinder, 
    agentConfig, 
    pathfindingConfig
);

// 5. ✅ PRODUCTION: Create motor-based controller
var motorController = new MotorCharacterController(physicsWorld);

// 6. Create movement controller with motor
var movementController = new MovementController(
    physicsWorld,
    pathfindingService,
    agentConfig,
    pathfindingConfig,
    motorController  // ✅ Motor-based (RECOMMENDED)
);

// 7. Spawn agent
var (agentShape, agentInertia) = physicsWorld.CreateCapsuleShapeWithInertia(
    agentConfig.Radius, 
    agentConfig.Height, 
    mass: 1.0f
);
var agent = physicsWorld.RegisterEntityWithInertia(
    entityId: 1,
    entityType: EntityType.Player,
    position: spawnPosition,
    shape: agentShape,
    inertia: agentInertia,
    isStatic: false
);

// 8. Request movement
var moveRequest = new MovementRequest(
    entityId: 1,
    targetPosition: goalPosition,
    maxSpeed: 5.0f
);
var response = movementController.RequestMovement(moveRequest);

// 9. Update loop
while (simulation_running)
{
    movementController.UpdateMovement(deltaTime);
    physicsWorld.Update(deltaTime);
}
```

---

## Why Motor-Based Controller?

### Performance Comparison (Phase 4 Results)

| Metric | Velocity-Based | Motor-Based | Improvement |
|--------|----------------|-------------|-------------|
| Distance Traveled | 84.61m | **41.02m** | **51.5% less** |
| Replanning Events | 11 | **0** | **100% reduction** |
| Completion Time | 21.1s | **14.4s** | **32% faster** |
| Y Position Range | ±14.3m | **±8.8m** | **39% more stable** |
| Agent-3 Success | ❌ Failed | **✅ Success** | **Solved!** |

### Key Advantages

1. **Perfect Path Following**: Zero replanning needed
2. **Steep Terrain Handling**: Solves 10m multi-level climb (71.5% grade)
3. **Efficiency**: Travels half the distance to reach goals
4. **Stability**: No physics explosions or launching on slopes
5. **BepuPhysics Alignment**: Uses recommended motor approach

---

## When to Use Each Controller

### ✅ MotorCharacterController (Default)

**Use for**:
- All production deployments
- Multi-level navigation
- Steep slopes and stairs
- Complex pathfinding scenarios
- Long-distance navigation
- Default choice for all new agents

**Advantages**:
- Best performance and stability
- Zero replanning overhead
- Handles all terrain types
- Production-tested and validated

### ⚠️ CharacterController (Backup Only)

**Use only for**:
- Backwards compatibility with existing code
- Debugging motor controller issues (fallback)
- Special cases requiring direct velocity control
- Legacy system support

**Disadvantages**:
- 2x more distance traveled
- Frequent replanning needed
- Less stable on steep terrain
- Not recommended for new code

---

## Critical Configuration

### 1. AgentConfig (Single Source of Truth)

```csharp
var agentConfig = new AgentConfig
{
    Height = 2.0f,      // Agent height
    Radius = 0.4f,      // Agent radius
    MaxSlope = 45.0f,   // Maximum walkable slope (degrees)
    MaxClimb = 0.5f     // ⚠️ CRITICAL: Max vertical climb per segment
};
```

**⚠️ Important**: The same `AgentConfig` must be used for:
- NavMesh generation
- PathfindingService creation
- MovementController creation

This ensures alignment between what the NavMesh considers walkable and what the movement system can execute.

### 2. PathfindingConfiguration

```csharp
var pathfindingConfig = new PathfindingConfiguration
{
    PathAutoFix = true,                      // ✅ Always enable
    PathValidation = true,                   // ✅ Always enable
    PathfindingSearchExtentsHorizontal = 5.0f,
    PathfindingSearchExtentsVertical = 10.0f,
    LocalAvoidanceRadius = 3.0f,
    ReplanDistanceThreshold = 2.0f,
    DestinationReachedThreshold = 1.5f
};
```

**Critical Settings**:
- `PathAutoFix = true` - Automatically inserts waypoints to fix invalid segments
- `PathValidation = true` - Validates segments against MaxClimb/MaxSlope

### 3. MotorCharacterController Configuration

```csharp
// Default values (work well for most cases)
var motorController = new MotorCharacterController(
    physicsWorld,
    motorStrength: 80f,              // Higher = stronger movement
    heightCorrectionStrength: 50f,   // Higher = faster height adjustment
    damping: 0.5f                    // Higher = less oscillation
);
```

**Tuning Guide**:
- Increase `motorStrength` if agents move too slowly
- Increase `heightCorrectionStrength` if agents drift from path height
- Increase `damping` if agents oscillate or bounce
- Default values are production-tested

---

## Movement Target Fallback (Option D)

**Added**: 2026-04-06

When a player sends a movement command to an unreachable target (wrong navmesh island,
off-mesh click, clicked inside a wall), the system now attempts a tiered recovery
instead of hard-cancelling.

### The Four Tiers

| Tier | Triggers when… | Unit behavior | `WasTargetAdjusted` |
|------|----------------|---------------|---------------------|
| **1 — Direct** | Full path to snapped target found | Goes exactly to player's target | `false` |
| **2 — Nearest reachable near target** | Tier 1 failed; samples ring of candidates around target | Goes to closest reachable point within `FallbackTargetSearchRadius` | `true` |
| **3 — Furthest reachable toward target** | Tiers 1–2 failed; DotRecast returned a partial corridor | Advances to the island edge in the target's direction | `true` |
| **4 — Hard cancel** | No reachable position anywhere | Unit stays put, `FailureReason = NoReachablePosition` | — |

### How to Read the Response

```csharp
var response = movementController.RequestMovement(moveRequest);

if (response.Success)
{
    if (response.WasTargetAdjusted)
    {
        // Show yellow/warning destination marker instead of green
        // response.AdjustmentReason is one of:
        //   "NearestReachableNearTarget"      — Tier 2
        //   "FurthestReachableTowardTarget"   — Tier 3
        ShowAdjustedMarker(response.ActualTargetPosition, response.AdjustmentReason);
    }
    else
    {
        ShowNormalMarker(response.ActualTargetPosition); // Tier 1 — exact target
    }
}
else
{
    // Tier 4 hard cancel
    // response.FailureReason is MovementFailureReason.NoReachablePosition
    ShowErrorFeedback(response.FailureReason);
}
```

### Tuning `PathfindingConfiguration`

```csharp
var pathfindingConfig = new PathfindingConfiguration
{
    // Tier 2 ring search around the original target
    FallbackTargetSearchRadius  = 5.0f,  // meters — set 0 to skip Tier 2
    FallbackTargetSearchSamples = 8,     // directions × 2 rings = 16 candidates
};
```

- **Increase** `FallbackTargetSearchRadius` for larger maps where targets are often
  clicked far from the navmesh boundary (e.g., open-world).
- **Decrease** it (or set 0) if you want strict "exact target or fail" behaviour.
- `FallbackTargetSearchSamples` rarely needs changing; 8 covers 45° resolution.

### Why Tier 2 Naturally Handles Disconnected Islands

When the target is on a different island, every ring candidate sampled around the
target is also on that island — all unreachable from the agent. Tier 2 finds nothing
and falls through to Tier 3 without any special-casing. Tier 3 then uses the partial
DotRecast corridor (the "furthest reachable toward target" waypoints) that DotRecast
returns for disconnected targets, advancing the unit to its island's boundary.

### `PathResult.IsPartial`

`Pathfinder.FindPath` now exposes whether DotRecast returned a partial corridor
(`DT_PARTIAL_RESULT`). You can inspect this directly if needed:

```csharp
var path = pathfindingService.FindPath(start, end);
if (path.Success && path.IsPartial)
{
    // path.Waypoints ends at the furthest reachable point — not at `end`
}
```

---

## Migration Guide

### From Velocity-Based to Motor-Based

**Before** (Legacy):
```csharp
var movementController = new MovementController(
    physicsWorld, 
    pathfinder, 
    agentConfig
);
```

**After** (Production):
```csharp
var pathfindingService = new PathfindingService(pathfinder, agentConfig, pathfindingConfig);
var motorController = new MotorCharacterController(physicsWorld);
var movementController = new MovementController(
    physicsWorld,
    pathfindingService,
    agentConfig,
    pathfindingConfig,
    motorController  // ✅ Explicit motor controller
);
```

**Changes Required**:
1. Create `PathfindingService` explicitly
2. Create `MotorCharacterController` instance
3. Pass motor controller to `MovementController` constructor
4. No changes to `MovementRequest` or API usage

---

## Testing & Validation

### Test Commands

```bash
# Compare both approaches (educational)
dotnet run --project Spatial.TestHarness -- motor-vs-velocity

# Test motor controller only (production validation)
dotnet run --project Spatial.TestHarness -- motor-vs-velocity --motor

# Full multi-agent test (5 agents)
dotnet run --project Spatial.TestHarness -- enhanced 5

# Stress test (10 agents)
dotnet run --project Spatial.TestHarness -- enhanced 10
```

### Success Metrics

Monitor these in production:
- **Success Rate**: Should be ≥60% for complex multi-agent scenarios
- **Replanning Frequency**: Should be near 0 with motor controller
- **Path Efficiency**: Traveled distance should be close to direct distance
- **Physics Stability**: No explosions, launching, or falling through world

---

## Architecture Components

### Production Stack

```
Game Server
    ↓
MovementController (coordinates everything)
    ↓
├─→ PathfindingService (path finding + validation + autofix)
│   ├─→ Pathfinder (DotRecast integration)
│   │   └─→ NavMeshData (walkable surfaces)
│   └─→ PathSegmentValidator (MaxClimb/MaxSlope validation)
│       └─→ PathAutoFix (inserts intermediate waypoints)
│
└─→ MotorCharacterController ✅ (smooth physics movement)
    └─→ PhysicsWorld (BepuPhysics integration)
        └─→ Physics entities, collision, gravity
```

### Key Systems Integration

1. **NavMesh Generation** → Uses `AgentConfig.MaxClimb` and `MaxSlope`
2. **Path Validation** → Validates segments against same `AgentConfig`
3. **PathAutoFix** → Inserts waypoints to split invalid climbs
4. **Motor Controller** → Executes path with smooth acceleration
5. **Physics World** → Handles gravity, collisions, ground contact

---

## Common Issues & Solutions

### Issue: Agent doesn't move

**Check**:
1. Is NavMesh generated? (`navMeshData != null`)
2. Is path found? (`pathResult.Success == true`)
3. Is `MovementController.RequestMovement()` called?
4. Is `MovementController.UpdateMovement()` called each frame?
5. Is `PhysicsWorld.Update()` called each frame?

### Issue: Agent falls through world

**Solution**: Already solved with motor controller! This was the Agent-3 issue.
- Ensure using `MotorCharacterController` (not `CharacterController`)
- Verify `PathAutoFix = true` in configuration

### Issue: Agent doesn't reach goal

**Check**:
1. Is goal on NavMesh? Use larger search extents
2. Is path valid? Check `PathSegmentValidator` output
3. Is agent stuck? Check for collision blocking
4. Is replanning threshold too high? Lower `ReplanDistanceThreshold`
5. Was the target adjusted? Check `response.WasTargetAdjusted` and `response.AdjustmentReason`
   — Tier 3 ("FurthestReachableTowardTarget") means goal is on a disconnected island

### Issue: Agent moves but not along path

**Check**:
1. Is path being passed to visualization? (Unity client)
2. Is motor controller properly configured?
3. Are waypoints valid? Check path validation logs

---

## Performance Considerations

### Motor Controller Overhead

**Minimal**: Motor controller adds negligible overhead compared to velocity-based:
- Same physics update cost
- No additional replanning cost (actually saves CPU)
- Slightly more complex ground contact analysis (microseconds)

**Net Result**: 32% faster overall due to zero replanning

### Scaling

Tested with:
- ✅ 5 agents: 60% success rate
- ✅ 10 agents: Validated in stress tests
- 📊 50+ agents: Not yet tested (future Phase 5)

Expected scaling: Linear with agent count (no quadratic issues)

---

## Related Documentation

### Phase 4 Audit Results
- `PHASE4_DECISION_AND_VALIDATION.md` - Final decision and validation
- `PHASE2_MOTOR_IMPLEMENTATION.md` - Motor controller implementation
- `MOTOR_VS_VELOCITY_USAGE.md` - Comparison test usage

### System Design
- `PATH_VALIDATION_IMPLEMENTATION.md` - PathAutoFix system
- `CONFIGURATION_ALIGNMENT_SUMMARY.md` - AgentConfig unification
- `NAVMESH_PATH_ANALYSIS.md` - Root cause analysis

### Integration Guides
- `IMPLEMENTATION_SUMMARY.md` - Overall system architecture
- `GAME_SERVER_INTEGRATION_GUIDE.md` - Game server integration
- `MOVEMENT_FLOW_GUIDE.md` - Movement system flow

---

## Support & Questions

### Decision Authority

This architecture decision was made based on:
- Comprehensive Phase 4 testing (2026-01-26)
- Direct comparison: Motor vs Velocity controllers
- Multi-agent validation (5-10 agents)
- Success criteria: Agent-3 climb scenario

**Decision is final for production deployment.**

### Future Review

Consider reviewing if:
- New BepuPhysics version changes motor behavior
- New terrain types show unexpected issues
- Performance bottlenecks identified at 50+ agents scale

---

## Quick Reference

### Production Checklist

- ✅ Use `MotorCharacterController` (not `CharacterController`)
- ✅ Enable `PathAutoFix = true`
- ✅ Enable `PathValidation = true`
- ✅ Use same `AgentConfig` everywhere
- ✅ Set `MaxClimb = 0.5f` (critical for validation)
- ✅ Call `UpdateMovement()` and `PhysicsWorld.Update()` each frame
- ✅ Test with Agent-3 scenario to validate setup

### Red Flags

- ❌ Using `CharacterController` for new code
- ❌ `PathAutoFix = false` (will cause falling on steep terrain)
- ❌ Different `AgentConfig` values between systems
- ❌ `MaxClimb` > 1.0f (may cause invalid paths)
- ❌ Not calling `UpdateMovement()` every frame

---

**🎉 System is production-ready with motor-based controller!**
