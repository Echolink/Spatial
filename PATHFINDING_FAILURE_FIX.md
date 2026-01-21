# Pathfinding Failure Fix: Agents Falling Through World

## Issue Summary

**Problem:** Agent-3 in the enhanced test was falling infinitely through the world, reaching Y=-765 after 15 seconds.

**Root Cause:** The agent wasn't being "pushed" by other entities - it was **falling due to gravity** because it had no pathfinding control.

## Why This Happened

1. **Agent-3 failed pathfinding** - Its goal position (35.1, -1.7, -5.7) was off the navmesh
2. **`RequestMovement()` returned `false`** - The agent was never added to `_movementStates`
3. **No movement control** - `UpdateMovement()` never processed this agent
4. **Gravity was enabled** - The agent had `disableGravity: false` set during creation
5. **Infinite falling** - With no control and gravity enabled, it fell forever

## The Confusion

The user thought the agent was being "pushed" by another entity, but this wasn't the case. The agent collision system is designed to **prevent pushing** by default:

- Agents block each other (like walls)
- No automatic push forces between agents
- `MaximumRecoveryVelocity = 0` in agent-agent collisions

For pushing to occur, you must explicitly:
- Mark the agent as pushable via `physicsWorld.SetEntityPushable(entity, true)`
- Call `movementController.Push()` or `Knockback()`

## The Fix

Added tracking for agents that fail pathfinding and keep them stationary:

```csharp
// Track agents that failed pathfinding
var failedAgents = new List<PhysicsEntity>();

// During pathfinding setup:
if (!pathResult.Success)
{
    metric.ReachedGoal = false;
    failedAgents.Add(entity);  // Track for later
}

// During simulation loop:
foreach (var failedAgent in failedAgents)
{
    physicsWorld.SetEntityVelocity(failedAgent, Vector3.Zero);  // Keep stationary
}
```

## Results

**Before:**
- Agent-3: Y=-763.8 (falling infinitely)
- Path Traveled: 776.68m (downward)

**After:**
- Agent-3: Y=-2.0 (stationary)
- Path Traveled: 1.18m (minimal drift before fix kicks in)

## Key Takeaways

1. **Pathfinding failure ≠ Movement control** - Agents without paths aren't controlled by `MovementController`
2. **Gravity affects uncontrolled agents** - If gravity is enabled and no control is applied, agents fall
3. **Agent collision doesn't cause pushing** - By design, agents block each other without pushing forces
4. **Failed pathfinding needs handling** - Always handle the case where pathfinding fails

## Related Systems

- **Collision System**: See `AGENT_COLLISION_GUIDE.md` for how agent-agent collisions work
- **Movement Controller**: Only controls agents in `_movementStates` (successful pathfinding)
- **Character Controller**: Manages grounded/airborne state but can't help agents without paths

## Prevention

To prevent this in production code:

```csharp
// Option 1: Keep agents stationary if pathfinding fails
if (!movementController.RequestMovement(moveRequest))
{
    physicsWorld.SetEntityVelocity(entity, Vector3.Zero);
    // Track this entity to keep zeroing velocity each frame
}

// Option 2: Remove agents that can't find paths
if (!movementController.RequestMovement(moveRequest))
{
    physicsWorld.UnregisterEntity(entity);
}

// Option 3: Make them kinematic (zero inverse mass)
if (!movementController.RequestMovement(moveRequest))
{
    // Set inverse mass to 0 to make kinematic (not affected by gravity)
    // This requires direct Bepu API access
}
```

## Testing Verification

Run the enhanced test to verify:
```bash
dotnet run --project Spatial.TestHarness -- enhanced
```

Expected behavior:
- Agents with valid paths move normally
- Agents with failed pathfinding stay at their spawn positions
- No infinite falling or large negative Y values
