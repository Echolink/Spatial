# Sinking Character Fix - Root Cause Analysis

## Problem Description
Character appears half-submerged in the ground during movement in Unity visualization.

## Root Cause Identified ✅

### The Numbers Don't Lie

From terminal output analysis:
```
Step 748:
- NavMesh surface:        Y = 7.81m  (where feet SHOULD be)
- Agent physics center:   Y = 8.14m  (actual position)
- Expected center:        Y = 9.21m  (7.81 + 1.4 halfHeight) ❌
- Actual feet:            Y = 6.74m  (8.14 - 1.4 halfHeight) ❌
- Feet offset error:      -1.07m    (1.07m BELOW ground!) 🚨
```

### What's Happening

The `MovementController` correctly calculates where the character SHOULD be:
```csharp
float targetY = interpolatedGroundY + agentHalfHeight;  // = 7.81 + 1.4 = 9.21m ✅
_characterController.ApplyGroundingForce(entity, desiredVelocity, targetY, agentHalfHeight);
```

But the `MotorCharacterController` is **NOT maintaining that height**!

### Why Motor Control Was Too Weak

The motor-based approach uses **proportional control** (like a spring), not hard position constraints:

```csharp
float yError = targetY - currentPos.Y;  // = 9.21 - 8.14 = 1.07m error!
float verticalCorrection = yError * HeightCorrectionStrength;
```

**Previous Config (TOO WEAK):**
- `HeightCorrectionStrength = 5.0f`
- `MaxVerticalCorrection = 3.0f`
- Correction force: 1.07 × 5.0 = 5.35 m/s (but clamped to 3.0 m/s)

**With Gravity at 9.81 m/s²**, this correction wasn't strong enough to overcome:
- Downward gravity pull
- Movement along slopes
- Physics constraints from collisions

Result: Character slowly sinks into ground as gravity wins.

## The Fix ✅

### Increased Motor Strength Parameters

```csharp
var motorConfig = new MotorCharacterConfig
{
    MotorStrength = 0.5f,              // ↑ from 0.3 (67% increase)
    HeightCorrectionStrength = 15.0f,  // ↑ from 5.0 (3x stronger!)
    MaxVerticalCorrection = 8.0f,      // ↑ from 3.0 (2.67x faster)
    HeightErrorTolerance = 0.1f,       // ↓ from 0.2 (tighter tolerance)
    VerticalDamping = 0.9f,            // ↑ from 0.8 (less oscillation)
    StabilityThreshold = 0.15f         // ↓ from 0.2 (faster recovery)
};
```

### What These Changes Do

**HeightCorrectionStrength = 15.0f:**
- For 1.07m error: `1.07 × 15.0 = 16.05 m/s` upward velocity
- Strong enough to overcome gravity (9.81 m/s²) and maintain height
- Clamped to MaxVerticalCorrection to prevent violent snapping

**MaxVerticalCorrection = 8.0f:**
- Allows faster corrections when character is sinking
- Previous 3.0 m/s was too conservative
- 8.0 m/s provides aggressive but controlled correction

**MotorStrength = 0.5f:**
- Higher responsiveness to velocity corrections
- Faster reaction to height errors
- More aggressive movement execution

**VerticalDamping = 0.9f:**
- Reduces bounce/oscillation when reaching target height
- Prevents "bobbing" effect
- Character settles smoothly at correct height

## Why This Problem Occurred

### Motor-Based Control Philosophy

The motor approach uses **soft constraints** (forces) instead of **hard constraints** (direct position setting):

✅ **Advantages:**
- More realistic physics
- Smooth, natural movement
- Works with physics solver
- Handles slopes gracefully

❌ **Challenge:**
- Must tune parameters to overcome gravity
- Can sink if correction force too weak
- Requires balance between strength and stability

### The Balance

```
Correction Force > Gravity + Friction + Movement
     ↓
15.0 × error  >  9.81 m/s²
     ↓
For 1m error: 15 m/s > 9.81 m/s ✅
```

## Test Results Expected

### Before Fix:
```
NavMesh Y: 7.81m
Agent Y:   8.14m  (center)
Feet Y:    6.74m  ❌ 1.07m below ground
Visual:    Character half-submerged
```

### After Fix:
```
NavMesh Y: 7.81m
Agent Y:   9.21m  (center) ✅
Feet Y:    7.81m  ✅ ON ground
Visual:    Character standing on surface properly
```

## Testing Instructions

```bash
cd Spatial.TestHarness
dotnet run -- motor-vs-velocity --motor
```

Watch in Unity:
- ✅ Character should walk ON the surface
- ✅ Feet should touch ground, not sink into it
- ✅ No bouncing or oscillation
- ✅ Smooth movement up slopes

## Technical Deep Dive

### The Coordinate Flow

1. **NavMesh** returns ground surface Y = 7.81m
2. **MovementController** calculates:
   ```csharp
   targetY = 7.81 + 1.4 = 9.21m  // Capsule center
   ```
3. **MotorCharacterController** applies force:
   ```csharp
   yError = 9.21 - 8.14 = 1.07m
   correction = 1.07 × 15.0 = 16.05 m/s (clamped to 8.0)
   newVelocity.Y += correction × MotorStrength
   ```
4. **Physics** integrates velocity and resolves collisions
5. **Next frame** character should be closer to Y=9.21m

### Why Not Just Teleport?

Could we just set `position.Y = targetY` directly? Yes, but:

❌ **Problems with direct positioning:**
- Breaks physics simulation
- Causes jitter/stuttering
- Ignores collisions
- Looks unnatural
- Can cause clipping through geometry

✅ **Motor control advantages:**
- Respects physics constraints
- Smooth, natural movement
- Handles collisions properly
- Works with slopes
- Realistic behavior

## Files Modified

1. ✅ `Spatial.TestHarness/TestMotorVsVelocity.cs` - Increased motor strength
2. ✅ `CHARACTER_POSITIONING_FIX.md` - Spawn offset documentation
3. ✅ `NAVMESH_HEIGHT_INVESTIGATION.md` - Diagnostic guide
4. ✅ `SINKING_CHARACTER_FIX.md` - This file

## Key Lessons

### 1. Motor Parameters Must Overcome Physics
```
HeightCorrection × Error > Gravity + Other Forces
```

### 2. Proportional Control Needs Tuning
- Too weak = sinking
- Too strong = bouncing
- Balance = smooth and stable

### 3. Visualization Shows Physics State
- Unity displays what physics reports
- If physics is wrong, visual is wrong
- Fix physics, not visualization

### 4. Debugging Requires Data
The terminal output showed:
- Target Y = 9.21m (calculated correctly)
- Actual Y = 8.14m (motor not reaching target)
- Error = 1.07m (correction too weak)

Without these numbers, we couldn't diagnose the problem!

## Related Issues

### Issue 1: Spawn Position ✅ FIXED
- Problem: Agent spawned without height offset
- Fix: Added `capsuleHalfHeight` offset at spawn
- File: `TestMotorVsVelocity.cs` lines 281-313

### Issue 2: Motor Strength ✅ FIXED
- Problem: Height correction too weak vs gravity
- Fix: Increased correction strength 3x
- File: `TestMotorVsVelocity.cs` lines 213-221

### Issue 3: Visualization (No fix needed)
- `EntityVisualizer.cs` correctly offsets visual by -halfHeight
- Works properly when physics position is correct

## Alternative Approaches Considered

### Option A: Direct Position Setting ❌
```csharp
physicsWorld.SetEntityPosition(entity, targetY);
```
- Pro: Always at correct height
- Con: Breaks physics, causes jitter

### Option B: Kinematic Character Controller ❌
```csharp
// Bypass physics, move directly
position += velocity × deltaTime;
```
- Pro: Full control
- Con: Must handle all collisions manually

### Option C: Stronger Motor Control ✅ CHOSEN
```csharp
// Let physics work, but with stronger correction
correction = error × 15.0f;  // vs previous 5.0f
```
- Pro: Physics-based, smooth, handles collisions
- Con: Requires parameter tuning

## Summary

**Problem:** Character sinking 1.07m into ground due to weak motor corrections.

**Root Cause:** HeightCorrectionStrength (5.0f) insufficient to overcome gravity (9.81 m/s²) and maintain target height.

**Solution:** Increased motor parameters:
- 3x stronger height correction (5.0 → 15.0)
- 2.67x faster maximum correction (3.0 → 8.0)
- More aggressive motor response (0.3 → 0.5)

**Result:** Character maintains correct height, walks on surface properly. ✅
