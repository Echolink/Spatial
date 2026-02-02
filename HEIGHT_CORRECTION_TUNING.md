# Height Correction Tuning Guide

**Date**: 2026-02-02  
**Issue**: Units appearing below target height at destination

## Problem History

### Iteration 1: Initial Smooth Movement
- **Config**: `HeightCorrectionStrength = 5.0`
- **Result**: Unit stopped at Y=6.997586 (feet) when target was Y=7.8
- **Error**: 0.803m too low
- **Cause**: Not enough correction force to overcome damping

### Iteration 2: Aggressive Correction (FAILED)
- **Config**: `HeightCorrectionStrength = 8.0`
- **Result**: Visible vertical snapping during movement
- **Cause**: Too aggressive correction without enough damping

### Iteration 3: Balanced Approach (CURRENT)
- **Config**: 
  ```csharp
  HeightCorrectionStrength = 6.5f,   // Moderate increase
  MaxVerticalCorrection = 3.5f,      // Allow faster correction
  HeightErrorTolerance = 0.25f,      // Wider damping zone
  VerticalDamping = 0.75f,           // Slightly less damping
  ```
- **Expected**: Height error < 0.4m with minimal snapping

## Motor Parameter Interactions

### HeightCorrectionStrength
- **What it does**: Proportional gain for Y position error
- **Higher value**: Stronger upward force when below target
- **Side effect**: Can cause oscillation if too high

### MaxVerticalCorrection
- **What it does**: Caps the maximum vertical velocity from height correction
- **Higher value**: Allows faster height adjustments
- **Side effect**: Can cause "popping" if too high

### HeightErrorTolerance
- **What it does**: Distance from target where damping kicks in
- **Higher value**: Smoother approach to target (larger "slow down" zone)
- **Side effect**: May not reach exact target if too large

### VerticalDamping
- **What it does**: Reduces vertical velocity when near target
- **Lower value**: Less braking, faster response
- **Side effect**: Can overshoot if too low

## Tuning Strategy

**To increase height without snapping:**
1. ✅ Increase `HeightCorrectionStrength` by 1.0-1.5 at a time
2. ✅ Increase `HeightErrorTolerance` proportionally (wider damping zone)
3. ✅ Slightly reduce `VerticalDamping` (faster response)
4. ✅ Test and measure height error at destination

**To reduce snapping:**
1. ✅ Increase `HeightErrorTolerance` (larger smooth zone)
2. ✅ Increase `VerticalDamping` (more braking near target)
3. ✅ Reduce `MaxVerticalCorrection` (cap max speed)

## Expected Results

With current settings:
- Unit should reach within **0.2-0.4m** of target height
- Minimal to no visible snapping during movement
- Smooth approach to final position

If height error is still > 0.5m, increase `HeightCorrectionStrength` to 7.0-7.5 and retest.

## Position Coordinate System

**Important**: Do NOT change the position convention between server and Unity!

- **Server**: Sends physics center position (BepuPhysics standard)
- **Unity**: Subtracts half capsule height to display feet position
- **This works correctly** - the issue is motor strength, not position convention

Attempting to send "feet position" from server creates confusion and misalignment.
