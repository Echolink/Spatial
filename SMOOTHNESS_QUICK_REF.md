# Movement Smoothness - Quick Reference

## Problem
Agent movement looks **snappy/laggy** with visible teleporting between frames.

## Solution Applied

### Three Changes Made:

1. **⬇️ Slower Motor** (50% reduction in aggression)
   ```csharp
   MotorStrength = 0.15f  // was 0.3f
   ```

2. **⬇️ Slower Speed** (40% reduction)
   ```csharp
   maxSpeed = 3.0f  // was 5.0f
   ```

3. **⬆️ Faster Physics** (2x update rate)
   ```csharp
   Timestep = 0.008f  // was 0.016f (125fps vs 60fps)
   ```

## Quick Tuning Guide

### If movement is still too snappy:
- **Reduce** `MotorStrength` → try `0.10f`
- **Reduce** `maxSpeed` → try `2.5f`
- **Reduce** `Timestep` → try `0.006f` (167fps)

### If movement is too sluggish:
- **Increase** `MotorStrength` → try `0.20f`
- **Increase** `maxSpeed` → try `3.5f`
- Keep `Timestep` at `0.008f`

### If performance is too slow:
- **Increase** `Timestep` → try `0.012f` (83fps)
- Keep motor and speed settings
- Reduces step count by 33%

## All Changes in One Place

**File**: `Spatial.TestHarness/TestEnhancedShowcase.cs`

```csharp
// 1. Physics config (line ~85)
Timestep = 0.008f

// 2. Motor config (line ~207)
var motorConfig = new MotorCharacterConfig
{
    MotorStrength = 0.15f,
    HeightCorrectionStrength = 3.0f,  // Reduced from 8.0 to prevent snapping
    MaxVerticalCorrection = 2.0f,      // Limits upward snap speed
    HeightErrorTolerance = 0.4f,       // Applies damping over larger range
    VerticalDamping = 0.7f,
    IdleVerticalDamping = 0.3f
};

// 3. Movement request (line ~537)
maxSpeed: 3.0f

// 4. Step counts
// Settling: 375 steps (was 190)
// Active: 1875 steps (was 937)
```

## Test Command

```bash
dotnet run --project Spatial.TestHarness
```

Watch for smooth movement in Unity visualizer.

---

**Result**: Smooth, natural-looking agent movement with no visible stuttering.
