# Slope Movement Fix - Constant Speed on All Surfaces

## Problem
After previous optimizations, movement was smooth on flat ground but agents still visibly struggled when climbing ramps. The goal is stable, constant movement speed on any surface type.

### Symptoms
- ✅ Smooth movement on horizontal/flat areas
- ❌ Visible struggle and speed variations on ramps
- ❌ Agent slows down when climbing slopes
- ❌ Jerky movement specifically on inclines

### Root Cause
The grounding system was treating **all surfaces the same** - applying the same correction frequency and tolerance on both flat ground and slopes.

**Why slopes are different:**
- On **flat ground**: Agent should stay at exact surface Y (tight tolerance is good)
- On **slopes**: Agent naturally deviates more as it climbs (tight tolerance fights physics)
- **Grounding force** on slopes interferes with natural upward momentum
- **Constant corrections** on slopes slow down the agent and create jerkiness

## Solution: Slope-Aware Grounding

The fix adds **different behavior for slopes vs flat ground**:

### 1. Increased Tolerance on Slopes
```csharp
// Slope-aware tolerance
float heightTolerance = isOnSlope ? 0.15f : 0.05f;
// Slopes: 15cm tolerance (3x more forgiving)
// Flat:   5cm tolerance (precise positioning)
```

**Why this works:**
- Slopes naturally cause more Y variance as agent climbs
- 15cm tolerance allows natural physics without constant corrections
- Still prevents major deviations (falling through/floating)

### 2. Reduced Grounding Frequency on Slopes
```csharp
if (isOnSlope)
{
    state.SlopeGroundingFrameCounter++;
    if (state.SlopeGroundingFrameCounter % 5 != 0)
    {
        // Skip grounding this frame - let physics handle slope naturally
        return;
    }
}
```

**Grounding frequency:**
- **Flat ground**: Every frame when error > 5cm (~60 times/sec)
- **Slopes**: Every 5 frames when error > 15cm (~12 times/sec)

**Benefits:**
- Agent maintains momentum while climbing
- Natural physics handles slope traversal
- Grounding only corrects major deviations
- Result: Constant speed on slopes

### 3. Early Detection of Slopes
The code already calculates `isOnSlope` based on:
```csharp
float heightDiff = Math.Abs(targetWaypoint.Y - (currentPosition.Y - agentHalfHeight));
float horizontalDist = CalculateXZDistance(currentPosition, targetWaypoint);
bool isOnSlope = heightDiff > 0.5f && horizontalDist > 0.1f;
```

Now this information is used to adjust grounding behavior automatically.

## Comparison

### Before (Same treatment for all surfaces)
| Surface Type | Tolerance | Grounding Freq | Result |
|--------------|-----------|----------------|--------|
| Flat ground  | 5cm       | Every frame    | ✅ Smooth |
| Slopes       | 5cm       | Every frame    | ❌ Struggle |

**Problem**: Tight tolerance + high frequency on slopes = constant interference

### After (Slope-aware behavior)
| Surface Type | Tolerance | Grounding Freq | Result |
|--------------|-----------|----------------|--------|
| Flat ground  | 5cm       | Every frame    | ✅ Smooth |
| Slopes       | 15cm      | Every 5 frames | ✅ Smooth |

**Solution**: Relaxed tolerance + low frequency on slopes = natural movement

## Performance Impact

### Query Frequency (at 60 FPS)
- **Flat ground**: ~12 queries/sec (only when error > 5cm)
- **Slopes**: ~12 queries/sec (every 5 frames)
- **Total**: Still ~18-24 queries/sec (vs 120/sec original)

### Grounding Force Applications
- **Flat ground**: Applied when needed (~12/sec)
- **Slopes**: Applied every 5 frames (~12/sec)
- **Result**: Minimal CPU impact while maintaining smoothness

## Code Changes

### Files Modified
- `Spatial.Integration/MovementController.cs`

### Changes:
1. **Slope-aware tolerance** (line ~667):
   - Use `isOnSlope` flag to choose tolerance
   - 15cm on slopes, 5cm on flat ground

2. **Grounding frequency control** (lines ~672-681):
   - Added `SlopeGroundingFrameCounter`
   - Skip grounding 4 out of 5 frames on slopes
   - Let physics handle slope naturally

3. **MovementState class** (lines ~1088):
   - Added `SlopeGroundingFrameCounter` property

## Testing Checklist

When testing slope movement:

- ✅ Flat ground: Smooth movement with precise positioning
- ✅ Gentle slopes: Constant speed, no struggling
- ✅ Steep ramps: Maintains momentum, smooth climb
- ✅ Speed consistency: Same speed on flat and slopes
- ✅ No jerkiness: Smooth transitions between surface types
- ✅ Still reaches goals: Accuracy maintained
- ✅ No falling through: Grounding still prevents major errors

## Tuning Parameters

### Slope Tolerance
```csharp
float heightTolerance = isOnSlope ? 0.15f : 0.05f;
```
- **Increase slope tolerance** (0.2f): More freedom, but may look floaty
- **Decrease slope tolerance** (0.1f): Tighter control, but may struggle
- **Recommended**: 0.15f (good balance)

### Slope Grounding Frequency
```csharp
if (state.SlopeGroundingFrameCounter % 5 != 0)
```
- **Higher number** (% 10): More momentum, but less correction
- **Lower number** (% 3): More responsive, but may struggle
- **Recommended**: % 5 (good balance)

## Visual Comparison

### Before Fix (Struggles on slopes)
```
Flat ground:  ========>========>========>  [Smooth, constant speed]
Climbing ramp: ===>=-==>==>-===>=-==>==>  [Jerky, speed variations]
```

### After Fix (Smooth on all surfaces)
```
Flat ground:  ========>========>========>  [Smooth, constant speed]
Climbing ramp: ========>========>========>  [Smooth, constant speed]
```

## Related Documents

- `MOVEMENT_SMOOTHNESS_OPTIMIZATION.md` - General smoothness improvements
- `RAMP_CLIMBING_FIX.md` - Original ramp climbing solution
- `CHARACTER_POSITIONING_FIX.md` - Ground positioning fix

## Key Principles

1. **One size doesn't fit all**: Different surfaces need different handling
2. **Trust physics on slopes**: Let natural physics handle inclines, only correct major errors
3. **Relaxed tolerance on dynamic surfaces**: More freedom = smoother movement
4. **Reduce correction frequency on slopes**: Don't fight the agent's momentum
5. **Maintain precision on flat ground**: Tight control where it matters

## Trade-offs

### Pros
- ✅ Constant movement speed on all surface types
- ✅ No visible struggle on slopes/ramps
- ✅ Natural-looking climbing behavior
- ✅ Maintains smooth flat-ground movement
- ✅ Minimal performance impact

### Cons
- ⚠️ Slightly less precise Y positioning on slopes (within 15cm)
- ⚠️ Agent may "float" slightly on very steep slopes
- ⚠️ Requires `isOnSlope` calculation (already done)

The trade-offs are minimal and the result is much better player experience.
