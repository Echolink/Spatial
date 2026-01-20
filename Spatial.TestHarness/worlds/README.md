# World Assets for Spatial Project

This directory contains 3D mesh files that define world geometry for physics simulation and pathfinding.

## Quick Start

1. Place your `.obj` file in this directory
2. (Optional) Create a `.obj.json` metadata file
3. Load it in code: `worldBuilder.LoadAndBuildWorld("worlds/your_world.obj")`

## Blender Workflow

### Creating a World in Blender

1. **Model Your World**
   - Create ground planes, walls, obstacles
   - Keep geometry simple for physics (low poly)
   - Use proper scale (1 Blender unit = 1 meter in physics)

2. **Name Your Objects**
   - Select object → Object Properties → Name
   - Use descriptive names: `ground`, `wall_north`, `wall_south`, etc.
   - Names support wildcards in metadata: `wall_*` matches all walls

3. **Export as OBJ**
   - File → Export → Wavefront (.obj)
   - Settings:
     - ✓ Selection Only (if you want specific objects)
     - ✓ Apply Modifiers
     - ✓ Include Normals (optional)
     - ✓ Include UVs (optional, not used for physics)
     - ✓ Triangulate Faces (IMPORTANT!)
     - ✓ Objects as OBJ Objects (keeps names)
   - Save to `worlds/` directory

### Example Blender Setup

```
Arena Scene:
  - ground (Plane, scaled 10x10)
  - wall_north (Cube, scaled to 16x3x1)
  - wall_south (Cube, scaled to 16x3x1)
  - wall_east (Cube, scaled to 1x3x16)
  - wall_west (Cube, scaled to 1x3x16)
```

## Metadata Files

### No Metadata (Simplest)

Just place `your_world.obj` - system uses defaults!
- All objects → Static
- Friction: 0.5
- No bounciness

### Creating Metadata

**Option 1: Use the generator tool**
```bash
dotnet run --project Spatial.MetadataGenerator -- generate worlds/your_world.obj
```

**Option 2: Manual creation**

Create `your_world.obj.json` next to your `.obj` file:

```json
{
  "version": "1.0",
  "meshes": [
    {
      "name": "ground",
      "material": {
        "friction": 0.8
      }
    },
    {
      "name": "wall_*",
      "material": {
        "friction": 0.5,
        "restitution": 0.1
      }
    }
  ]
}
```

### Metadata Fields Reference

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `version` | string | "1.0" | Metadata schema version |
| `defaultEntityType` | string | "StaticObject" | Default for all objects |
| `defaultIsStatic` | bool | true | Default static flag |
| `meshes[].name` | string | (required) | Object name or pattern |
| `meshes[].entityType` | string | defaultEntityType | Entity type |
| `meshes[].isStatic` | bool | defaultIsStatic | Is object immovable |
| `meshes[].material.friction` | float | 0.5 | Surface friction (0.0-1.0) |
| `meshes[].material.restitution` | float | 0.0 | Bounciness (0.0-1.0) |
| `transform.scale` | [x,y,z] | [1,1,1] | Global scale |
| `transform.rotation` | [x,y,z] | [0,0,0] | Global rotation (degrees) |
| `transform.position` | [x,y,z] | [0,0,0] | Global position offset |

## Examples in This Directory

### simple_arena.obj

Basic test arena with:
- Ground plane (20x20 meters)
- 4 walls forming a square enclosure
- Minimal complexity for testing

**Usage:**
```csharp
var worldBuilder = new WorldBuilder(physicsWorld, new MeshLoader());
worldBuilder.LoadAndBuildWorld("worlds/simple_arena.obj");
```

## Tips & Best Practices

### Performance
- Keep triangle count reasonable (< 10,000 per mesh for best performance)
- Use simplified collision meshes (physics doesn't need visual detail)
- Combine static objects when possible

### Scale
- 1 Blender unit = 1 meter in physics
- Character capsule is typically ~2 meters tall
- Doorways should be 2+ meters high

### Naming Conventions
- `ground_*` - Walkable horizontal surfaces
- `wall_*` - Vertical walls/obstacles
- `obstacle_*` - General obstacles
- `ceiling_*` - Overhead geometry

### Physics Materials
- **High friction (0.7-0.9)**: Ground, stairs, rough surfaces
- **Medium friction (0.4-0.6)**: Walls, general surfaces
- **Low friction (0.1-0.3)**: Ice, metal, slippery surfaces
- **Restitution (bounciness)**: Usually 0.0-0.2 for solid objects

## Troubleshooting

### "No meshes found in file"
- Check you exported with "Objects as OBJ Objects"
- Verify file isn't empty
- Make sure faces are triangulated

### "Invalid mesh geometry"
- Triangulate faces in Blender before export
- Check for non-manifold geometry
- Remove duplicate vertices

### NavMesh not generating
- Ensure ground plane exists and is named appropriately
- Check that surfaces are actually horizontal (top-facing normals)
- Verify scale is reasonable

### Objects at wrong scale
- Use `transform.scale` in metadata to fix
- Or adjust in Blender and re-export

## File Format Support

Currently supported:
- ✅ `.obj` (Wavefront OBJ) - Recommended, universally supported

Future support planned:
- ⏳ `.fbx` (Autodesk FBX)
- ⏳ `.gltf` / `.glb` (GL Transmission Format)
- ⏳ `.dae` (Collada)

## Loading in Code

```csharp
// Simple load
var worldData = worldBuilder.LoadAndBuildWorld("worlds/arena.obj");

// With explicit metadata path
var worldData = worldBuilder.LoadAndBuildWorld(
    "worlds/arena.obj", 
    "worlds/custom_meta.json"
);

// Hybrid: Load world + add procedural elements
worldBuilder.LoadAndBuildWorld("worlds/arena.obj");
worldBuilder.AddProceduralElements(physics => {
    // Add spawn points, dynamic objects, etc.
    var boxShape = physics.CreateBoxShape(new Vector3(1, 1, 1));
    physics.RegisterEntity(3000, EntityType.Obstacle, 
        new Vector3(5, 2, 5), boxShape, isStatic: false);
});

// Generate navmesh as usual
var navMeshData = navMeshBuilder.BuildNavMeshFromPhysicsWorld(agentConfig);
```

## Questions?

See the main project README or check the implementation in:
- `Spatial.MeshLoading` - Mesh loading system
- `Spatial.Integration/WorldBuilder.cs` - World building
- `Spatial.TestHarness/Program.cs` - Example usage
