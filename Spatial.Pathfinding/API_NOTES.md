# DotRecast API Integration Notes

## Current Status
The DotRecast integration structure is complete, but some API calls need verification against DotRecast 2026.1.1.

## Remaining API Issues

### 1. Input Geometry Class
- **Issue**: `RcInputGeom` class not found
- **Possible Solutions**:
  - Check if class is in `DotRecast.Recast.Geom` namespace
  - Verify if it's `InputGeomBuilder` or another class name
  - May need to use `RcBuilderContext` and `RcInputGeomBuilder` pattern

### 2. RcAreaModification Enum
- **Issue**: Enum values not matching (`RC_AREA_MODIFICATION_WALKABLE`, `RC_AREA_MODIFICATION_NONE` not found)
- **Action Needed**: Check DotRecast source for actual enum values
- **Possible Values**: May be `RcAreaModification.Walkable`, `RcAreaModification.None`, or integer constants

### 3. DtNavMesh Constructor
- **Issue**: Constructor signature doesn't match
- **Current Attempt**: `new DtNavMesh(navMeshData)`
- **Possible Solutions**:
  - May need `DtNavMeshParams` as second parameter
  - May need different initialization pattern
  - Check if `DtNavMeshBuilder.Build()` returns `DtNavMesh` directly

### 4. DtStatus Comparison
- **Issue**: Cannot use `!=` operator with `DtStatus`
- **Current Attempt**: `(status & DtStatus.DT_SUCCESS) != DtStatus.DT_SUCCESS`
- **Possible Solutions**:
  - May need to cast to int: `((int)(status & DtStatus.DT_SUCCESS)) != 0`
  - May need to use `HasFlag()` if DtStatus implements IFormattable
  - Check if DtStatus is a struct vs enum

## Next Steps
1. Review DotRecast 2026.1.1 source code: https://github.com/ikpil/DotRecast
2. Check example files: `RecastSoloMeshTest.cs`, `FindPathTest.cs`
3. Verify actual class names and namespaces
4. Test with actual geometry data once API is confirmed

## Resources
- DotRecast GitHub: https://github.com/ikpil/DotRecast
- DotRecast NuGet: https://www.nuget.org/packages/DotRecast.Detour/
- Recast Navigation Docs: https://recastnav.com/
