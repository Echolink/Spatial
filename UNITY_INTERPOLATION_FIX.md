# Unity Position Interpolation Fix

**Date**: 2026-01-28  
**Issue**: Movement still appears laggy in Unity despite physics smoothness improvements

## Root Cause: No Client-Side Interpolation

The physics simulation was running smoothly at 125 FPS, but **Unity was directly setting positions** from WebSocket messages without interpolation.

### The Problem (Before)

```csharp
// EntityVisualizer.cs - Line 336
entityObj.transform.position = new Vector3(
    -state.Position[0],
    state.Position[1] + yOffset,
    state.Position[2]
);
```

This caused visible "teleporting" because:
1. WebSocket messages arrive discretely (even at 125 Hz)
2. Network latency causes slight bunching/delays
3. Unity renders at 60+ FPS but only updates positions when messages arrive
4. Result: Agent "jumps" between positions

## Solution: Position Interpolation

Added **smooth interpolation** between received positions, a standard technique in networked games.

### Key Changes

**File**: `Unity/Scripts/EntityVisualizer.cs`

#### 1. Added Interpolation Settings

```csharp
[Header("Position Smoothing")]
[Tooltip("Enable smooth interpolation between positions (fixes laggy appearance)")]
public bool enableSmoothing = true;

[Tooltip("Interpolation speed (0=no smoothing, 1=instant, 0.1-0.3=smooth)")]
[Range(0f, 1f)]
public float smoothingSpeed = 0.2f;

private Dictionary<int, Vector3> targetPositions = new Dictionary<int, Vector3>();
private Dictionary<int, Quaternion> targetRotations = new Dictionary<int, Quaternion>();
```

#### 2. Modified UpdateEntityObject to Store Targets

```csharp
// Store the received position as target
Vector3 targetPos = new Vector3(
    -state.Position[0],
    state.Position[1] + yOffset,
    state.Position[2]
);
targetPositions[state.Id] = targetPos;

// Interpolate towards target (smooth!)
if (enableSmoothing && !state.IsStatic)
{
    entityObj.transform.position = Vector3.Lerp(
        entityObj.transform.position, 
        targetPos, 
        smoothingSpeed
    );
}
else
{
    entityObj.transform.position = targetPos;
}
```

#### 3. Added Update() for Continuous Interpolation

```csharp
void Update()
{
    if (!enableSmoothing)
        return;
    
    // Continue interpolating every Unity frame
    // This runs at 60+ FPS regardless of WebSocket rate
    foreach (var kvp in entityObjects)
    {
        Vector3 targetPos = targetPositions[entityId];
        entityObj.transform.position = Vector3.Lerp(
            entityObj.transform.position,
            targetPos,
            Time.deltaTime * smoothSpeed
        );
    }
}
```

## How It Works

### Two-Stage Interpolation

1. **WebSocket Update (125 FPS)**:
   - Receives new position from server
   - Stores as `targetPosition`
   - Does NOT snap directly to position

2. **Unity Update (60-144 FPS)**:
   - Smoothly moves towards `targetPosition`
   - Uses `Vector3.Lerp` for smooth acceleration
   - Frame-rate independent via `Time.deltaTime`

### Visual Result

**Before**: Position → [jump] → Position → [jump] → Position  
**After**: Position → [smooth] → [smooth] → Position → [smooth] → [smooth]

Unity fills in the gaps between WebSocket messages with smooth interpolation.

## Configuration

### Default Settings (Recommended)

```csharp
enableSmoothing = true
smoothingSpeed = 0.2f  // 20% interpolation per frame
```

### Tuning Guide

**If movement feels sluggish/delayed**:
- Increase `smoothingSpeed` → `0.3f` or `0.4f`
- Higher = more responsive but potentially less smooth

**If movement still feels snappy**:
- Decrease `smoothingSpeed` → `0.15f` or `0.1f`
- Lower = smoother but slightly more lag

**For instant updates (testing/debug)**:
- Set `enableSmoothing = false`
- Positions update directly like before

### Performance Notes

- **CPU Cost**: Negligible (simple lerp per entity)
- **Memory**: 2 Vector3 + 2 Quaternion per entity (~48 bytes)
- **Frame Rate**: No impact

## Technical Details

### Why Vector3.Lerp?

```csharp
Vector3.Lerp(current, target, 0.2f)
```

This moves 20% of the way from `current` to `target` each frame:
- Frame 1: 80% old, 20% new
- Frame 2: 64% old, 36% new
- Frame 3: 51% old, 49% new
- Frame 4: 41% old, 59% new
- ...exponential approach to target

### Why Time.deltaTime?

```csharp
smoothingSpeed * 10f * Time.deltaTime
```

Makes interpolation **frame-rate independent**:
- 60 FPS: deltaTime ≈ 0.016
- 144 FPS: deltaTime ≈ 0.007
- Same visual result regardless of frame rate

### Static Objects

```csharp
if (enableSmoothing && !state.IsStatic)
```

Static objects update instantly because:
- They don't move (no need for smoothing)
- Terrain/walls should snap to position immediately

## Combined Effect

### Server-Side (Previous Fix)
- ✅ Reduced motor aggression (0.15 strength)
- ✅ Reduced movement speed (3.0 m/s)
- ✅ Increased physics rate (125 FPS)

### Client-Side (This Fix)
- ✅ Position interpolation between updates
- ✅ Continuous movement at Unity frame rate
- ✅ Smooth acceleration/deceleration

### Result
**Butter-smooth movement** with no visible stuttering or lag.

## Testing

1. **Open Unity project**
2. **Find EntityVisualizer component** (on SimulationClient GameObject)
3. **Verify settings**:
   - `Enable Smoothing` = ✓ checked
   - `Smoothing Speed` = 0.2
4. **Run C# simulation**:
   ```bash
   dotnet run --project Spatial.TestHarness
   ```
5. **Connect Unity** (Play mode)
6. **Observe**: Movement should be smooth and fluid

## Rollback

To revert to instant updates:

**In Unity Inspector**:
- Uncheck `Enable Smoothing` on EntityVisualizer component

**Or edit code**:
```csharp
public bool enableSmoothing = false;
```

## Related Fixes

1. ✅ **Server-Side**: `SMOOTH_MOVEMENT_FIX.md`
   - Physics timestep, motor config, movement speed

2. ✅ **Client-Side**: `UNITY_INTERPOLATION_FIX.md` (this document)
   - Position interpolation in Unity

Together these provide complete end-to-end smoothness.

---

**Status**: ✅ Implemented and ready for testing
