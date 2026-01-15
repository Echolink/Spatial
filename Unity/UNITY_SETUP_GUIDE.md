# Unity 3D Visualization Setup Guide

This guide will help you set up Unity to visualize your physics simulation and pathfinding in real-time 3D.

## Prerequisites

- **Unity 2021.3 LTS or newer** (recommended: Unity 2022 LTS)
- **Unity Package Manager** access

## Quick Start

### 1. Install Required Packages

Open Unity Package Manager (`Window > Package Manager`) and install:

1. **Native WebSocket** (for WebSocket communication)
   - In Package Manager, click `+` → `Add package from git URL`
   - Enter: `https://github.com/endel/NativeWebSocket.git#upm`
   - Click `Add`

2. **Newtonsoft.Json** (for JSON serialization)
   - In Package Manager, switch to "Unity Registry"
   - Search for `Newtonsoft Json`
   - Click `Install`

### 2. Import Scripts

1. Create a new folder in your Unity project: `Assets/Spatial`
2. Copy all scripts from `Unity/Scripts/` to `Assets/Spatial/`:
   - `SimulationClient.cs`
   - `EntityVisualizer.cs`
   - `NavMeshVisualizer.cs`

### 3. Create Visualization Scene

1. **Create a new scene** or use an existing one
2. **Create an empty GameObject**: `GameObject > Create Empty`
3. **Name it**: `SimulationVisualizer`
4. **Add all three scripts** to this GameObject:
   - `SimulationClient`
   - `EntityVisualizer`
   - `NavMeshVisualizer`

### 4. Configure the Components

#### SimulationClient Settings:
- **Server URL**: `ws://localhost:8181` (default)
- **Auto Connect**: ✓ (checked)

#### EntityVisualizer Settings:
- **Auto Create Materials**: ✓ (checked) - will auto-generate materials
- Or create custom materials and assign them:
  - Static Material: Gray color for static objects
  - Dynamic Material: Blue color for dynamic objects
  - Agent Material: Orange color for agents
- **Show Velocity Vectors**: ✓ (checked)
- **Velocity Vector Scale**: `0.3` (adjust to preference)

#### NavMeshVisualizer Settings:
- **Show NavMesh**: ✓ (checked)
- **Show Path**: ✓ (checked)
- **Auto Create Materials**: ✓ (checked)
- **NavMesh Color**: Semi-transparent green `(0.2, 0.8, 0.2, 0.3)`
- **Path Color**: Cyan
- **Show Waypoints**: ✓ (checked)

### 5. Position the Camera

1. Select your Main Camera
2. Set position to see the simulation area (e.g., `Position: (10, 10, 10)`)
3. Rotate to look at origin (e.g., `Rotation: (30, -45, 0)`)
4. Or add a camera orbit script for better viewing

### 6. Run the Visualization

**Start Order (IMPORTANT):**

1. **First**: Start the C# simulation
   ```bash
   dotnet run --project "Spatial.TestHarness\Spatial.TestHarness.csproj"
   ```
   
   Wait for this message:
   ```
   [Viz Server] WebSocket server started on ws://0.0.0.0:8181
   [Info] Waiting for Unity client to connect...
   ```

2. **Second**: Press Play in Unity
   - Unity will connect automatically
   - You should see: `[SimulationClient] Connected!` in Unity Console
   - The C# console will show: `[Viz Server] Client connected`

3. **Watch**: The simulation will run and you'll see:
   - Physics entities appearing and moving
   - NavMesh rendered as a green semi-transparent surface
   - Pathfinding paths shown as cyan lines with waypoint markers
   - Velocity vectors on moving objects

## Troubleshooting

### "Could not resolve reference NativeWebSocket"

**Solution**: Install NativeWebSocket package
```
Unity Package Manager > + > Add package from git URL
https://github.com/endel/NativeWebSocket.git#upm
```

### "Type or namespace 'Newtonsoft' could not be found"

**Solution**: Install Newtonsoft.Json package
```
Unity Package Manager > Unity Registry > Search "Newtonsoft Json" > Install
```

### "Connection failed" or "WebSocket error"

**Checklist**:
1. ✓ Is the C# server running first?
2. ✓ Is the server URL correct? (`ws://localhost:8181`)
3. ✓ Is there a firewall blocking port 8181?
4. ✓ Check Windows Firewall settings

### Nothing appears in Unity

**Checklist**:
1. ✓ Is Unity Console showing "Connected!"?
2. ✓ Are all three scripts on the same GameObject?
3. ✓ Is the camera positioned to see the simulation area?
4. ✓ Check Unity Console for errors

### Entities appear but are the wrong size

**Issue**: Different shape scales between BepuPhysics and Unity

**Solution**: Adjust in `EntityVisualizer.cs` → `CreateShapeObject()` method
- Current scaling should work correctly for most cases
- Modify `localScale` calculations if needed

## Understanding the Visualization

### Colors (Default):
- **Gray**: Static objects (ground, obstacles)
- **Blue**: Dynamic physics objects
- **Orange**: Agents with pathfinding
- **Green (transparent)**: NavMesh walkable surface
- **Cyan**: Current pathfinding path
- **Yellow→Red**: Velocity vectors

### What You'll See:

#### Test 1: Physics Collision
- A dynamic capsule falling
- Hitting the ground plane
- Bouncing and settling
- Velocity vector shrinking as it comes to rest

#### Test 2: Full Integration
- NavMesh generated from obstacles
- An agent with a path
- Agent following waypoints
- Agent moving toward destination

## Advanced Configuration

### Custom Materials

Create better-looking materials in Unity:

1. **Static Material**:
   - Create: `Assets > Create > Material`
   - Name: `StaticMaterial`
   - Shader: Standard
   - Color: Gray (128, 128, 128)
   - Metallic: 0.3
   - Smoothness: 0.5

2. **Dynamic Material**:
   - Shader: Standard
   - Color: Blue (51, 153, 255)
   - Metallic: 0.5
   - Smoothness: 0.7
   - Emission: Enable with slight blue glow

3. **Agent Material**:
   - Shader: Standard
   - Color: Orange (255, 128, 51)
   - Metallic: 0.2
   - Smoothness: 0.6

Assign these in the EntityVisualizer component.

### Camera Control

Add a simple orbit camera script or use Unity's Cinemachine:

```csharp
// Simple orbit: Hold right-click and drag
public class OrbitCamera : MonoBehaviour
{
    public float rotationSpeed = 5f;
    public float zoomSpeed = 5f;
    public Transform target;
    
    void Update()
    {
        if (Input.GetMouseButton(1)) // Right mouse button
        {
            float h = rotationSpeed * Input.GetAxis("Mouse X");
            float v = rotationSpeed * Input.GetAxis("Mouse Y");
            transform.RotateAround(target.position, Vector3.up, h);
            transform.RotateAround(target.position, transform.right, -v);
        }
        
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        transform.Translate(0, 0, scroll * zoomSpeed);
    }
}
```

### Performance Optimization

For large simulations:

1. **Object Pooling**: Reuse GameObjects instead of creating/destroying
2. **LOD Groups**: Use simpler meshes for distant objects
3. **Frustum Culling**: Only update visible objects
4. **Reduce Line Segments**: Lower path line quality for many paths

## Integration with Your Own Unity Project

### Option 1: Use as Library
Copy the `Spatial` folder to your project's Assets.

### Option 2: Package
Create a Unity Package:
1. Select `Assets/Spatial` folder
2. Right-click → `Export Package`
3. Import into other projects

### Option 3: Custom Integration
Modify the scripts to fit your needs:
- Change materials/shaders
- Add custom entity types
- Integrate with your game logic
- Add UI controls

## Extending the Visualization

### Add UI Information Panel

```csharp
// Add to SimulationClient.cs
public SimulationState LatestState { get; private set; }

// In OnMessage callback:
LatestState = state;
```

Then create a UI TextMeshPro element:

```csharp
public class SimulationInfo : MonoBehaviour
{
    public SimulationClient client;
    public TMPro.TextMeshProUGUI infoText;
    
    void Update()
    {
        if (client.LatestState != null)
        {
            var state = client.LatestState;
            infoText.text = $"Entities: {state.Entities.Count}\n" +
                           $"NavMesh Polygons: {state.NavMesh?.PolygonCount ?? 0}\n" +
                           $"Path Length: {state.CurrentPath?.PathLength ?? 0:F2}";
        }
    }
}
```

### Record and Playback

Add recording capability:

```csharp
List<SimulationState> recordedStates = new List<SimulationState>();
bool isRecording = false;

// In OnStateReceived:
if (isRecording)
{
    recordedStates.Add(state);
}

// Playback at any speed
IEnumerator Playback(float speed = 1f)
{
    foreach (var state in recordedStates)
    {
        OnStateReceived?.Invoke(state);
        yield return new WaitForSeconds(0.016f / speed); // 60 FPS
    }
}
```

## Additional Resources

- **BepuPhysics**: https://github.com/bepu/bepuphysics2
- **DotRecast**: https://github.com/ikpil/DotRecast
- **NativeWebSocket**: https://github.com/endel/NativeWebSocket
- **Unity Manual**: https://docs.unity3d.com/Manual/index.html

## Support

If you encounter issues:
1. Check Unity Console for error messages
2. Check C# server console for connection logs
3. Verify all packages are installed correctly
4. Ensure the server is running before starting Unity

## Summary

You now have a complete real-time 3D visualization of your physics simulation and pathfinding!

**Benefits**:
- See physics interactions in real-time
- Verify NavMesh generation visually
- Debug pathfinding paths
- Understand agent movement
- Tune parameters with immediate visual feedback

Happy visualizing! 🎮
