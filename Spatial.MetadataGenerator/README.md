# Spatial Metadata Generator

CLI tool for generating and validating metadata files for the Spatial mesh loading system.

## Installation

```bash
# Build the tool
dotnet build Spatial.MetadataGenerator

# Optional: Install globally
dotnet pack
dotnet tool install --global --add-source ./nupkg Spatial.MetadataGenerator
```

## Commands

### Generate Metadata Template

Scans an .obj file and generates a metadata template with all detected mesh objects:

```bash
# Generate full metadata template
dotnet run --project Spatial.MetadataGenerator -- generate worlds/arena.obj

# Generate with custom output path
dotnet run --project Spatial.MetadataGenerator -- generate worlds/arena.obj -o custom_metadata.json

# Generate minimal metadata (version only)
dotnet run --project Spatial.MetadataGenerator -- generate worlds/arena.obj --minimal
```

**Output:** Creates `<input>.json` with detected mesh names and default properties.

### Validate Metadata File

Validates an existing metadata file for errors:

```bash
dotnet run --project Spatial.MetadataGenerator -- validate worlds/arena.obj.json
```

**Checks:**
- JSON syntax
- Required fields
- Valid value ranges (friction, restitution)
- Transform array sizes

## Usage Example

```bash
# 1. Export your world from Blender as .obj
# File → Export → Wavefront (.obj)

# 2. Generate metadata template
cd Spatial.TestHarness
dotnet run --project ../Spatial.MetadataGenerator -- generate worlds/my_world.obj

# 3. Edit the generated worlds/my_world.obj.json
# - Customize physics properties
# - Use wildcards for patterns (e.g., "wall_*")

# 4. Validate your changes
dotnet run --project ../Spatial.MetadataGenerator -- validate worlds/my_world.obj.json

# 5. Load in your application
var worldData = worldBuilder.LoadAndBuildWorld("worlds/my_world.obj");
```

## Metadata Format

See the [main plan documentation](../README.md) for full metadata format reference.

### Minimal Metadata

```json
{
  "version": "1.0"
}
```

Uses all defaults - simplest option!

### Full Metadata

```json
{
  "version": "1.0",
  "defaultEntityType": "StaticObject",
  "defaultIsStatic": true,
  "meshes": [
    {
      "name": "ground",
      "entityType": "StaticObject",
      "isStatic": true,
      "material": {
        "friction": 0.8,
        "restitution": 0.0
      }
    },
    {
      "name": "wall_*",
      "entityType": "StaticObject",
      "isStatic": true,
      "material": {
        "friction": 0.5,
        "restitution": 0.1
      }
    }
  ],
  "transform": {
    "scale": [1.0, 1.0, 1.0],
    "rotation": [0, 0, 0],
    "position": [0, 0, 0]
  }
}
```

## Features

- **Auto-detection:** Scans .obj files and extracts all object/group names
- **Template generation:** Creates properly formatted JSON with all detected meshes
- **Validation:** Checks for common errors and value ranges
- **Helpful messages:** Clear error and warning messages
- **Flexible output:** Choose minimal or full templates

## Tips

1. **Use wildcards:** Instead of listing every wall, use `"wall_*"` pattern
2. **Start minimal:** Generate with `--minimal`, add details only where needed
3. **Validate often:** Run `validate` after editing to catch errors early
4. **Match Blender names:** Name objects clearly in Blender before export

## Troubleshooting

### "No objects found"
- Your .obj file doesn't have named objects/groups
- In Blender: Enable "Objects as OBJ Objects" when exporting
- Or use minimal metadata (defaults will apply to all geometry)

### "Friction/restitution out of range"
- These values should be between 0.0 and 1.0
- The tool accepts values outside this range but warns you

### "Transform arrays must have 3 elements"
- Scale, rotation, and position are 3D vectors: `[x, y, z]`
- Example: `"scale": [2.0, 2.0, 2.0]` (doubles size on all axes)

## See Also

- `Spatial.MeshLoading` - The mesh loading library
- `Spatial.TestHarness/worlds/README.md` - Blender workflow guide
- Main project README - Full documentation
