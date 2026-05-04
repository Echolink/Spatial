# Unity 3D Visualization for Spatial Physics & Pathfinding

Real-time 3D visualization of BepuPhysics simulation and DotRecast pathfinding using Unity.

## 📁 Contents

- **`Scripts/`** - Unity C# scripts for visualization
  - `SimulationClient.cs` - WebSocket client for real-time updates
  - `EntityVisualizer.cs` - Renders physics entities
  - `NavMeshVisualizer.cs` - Renders NavMesh and paths

- **`UNITY_SETUP_GUIDE.md`** - Complete setup instructions
- **`QUICK_START.md`** - 5-minute setup guide

## ✨ Features

### Entity Visualization
- Automatic shape detection (Box, Capsule, Sphere)
- Color-coded by type (Static/Dynamic/Agent)
- Real-time position and rotation updates
- Velocity vector visualization

### NavMesh Visualization
- Semi-transparent walkable surface rendering
- Polygon-accurate mesh from DotRecast
- Automatic updates when NavMesh changes

### Path Visualization
- Cyan line showing current path
- Waypoint markers along the path
- Path length display
- Entity-to-path association

### Performance
- Efficient object pooling
- 125 Hz simulation updates via WebSocket
- Handles hundreds of entities smoothly

## 🚀 Quick Start

See [`QUICK_START.md`](QUICK_START.md) for a 5-minute setup guide.

## 📖 Full Documentation

See [`UNITY_SETUP_GUIDE.md`](UNITY_SETUP_GUIDE.md) for:
- Detailed setup instructions
- Troubleshooting guide
- Advanced configuration
- Extension examples
- Performance optimization

## 🎯 Use Cases

### 1. Debugging Physics
- Verify collision detection
- Check gravity and forces
- Monitor velocity and stability
- Identify NaN or unstable values

### 2. Testing Pathfinding
- Visualize NavMesh generation
- Verify path validity
- Check waypoint spacing
- Debug obstacle avoidance

### 3. Tuning Parameters
- Adjust physics parameters and see results immediately
- Test different NavMesh configurations
- Compare agent movement speeds
- Optimize collision response

### 4. Development Workflow
- Faster iteration cycles
- Better understanding of behavior
- Easier communication with team
- Visual regression testing

## 🔧 Requirements

- **Unity 2021.3 LTS or newer**
- **NativeWebSocket** package (WebGL compatible)
- **Newtonsoft.Json** package
- **C# Server** (Spatial.TestHarness) running

## 📊 What You'll See

### Test 1: Physics Collision
```
Ground plane (gray box)
    ↓
Capsule falls (blue, with velocity vector)
    ↓
Bounces and settles
    ↓
Velocity vector disappears (at rest)
```

### Test 2: Full Integration
```
Static obstacles (gray boxes)
    ↓
NavMesh generated (green surface around obstacles)
    ↓
Agent spawns (orange capsule)
    ↓
Path calculated (cyan line with waypoints)
    ↓
Agent follows path (moving toward destination)
```

## 🎨 Customization

All visualization aspects are customizable:

- **Colors**: Change material colors for each entity type
- **Sizes**: Adjust velocity vector scale, waypoint size
- **Rendering**: Use custom shaders and effects
- **UI**: Add information panels, statistics
- **Camera**: Implement orbit, follow, or free-cam

## 🔌 Architecture

```
C# Simulation (Spatial.TestHarness)
    ↓ (Physics World State)
Spatial.Server (WebSocket Server)
    ↓ (JSON over WebSocket)
Unity SimulationClient (WebSocket Client)
    ↓ (Parsed State)
EntityVisualizer + NavMeshVisualizer
    ↓ (Render)
Unity Scene (3D View)
```

### Data Flow
1. BepuPhysics simulates physics
2. DotRecast generates NavMesh
3. Pathfinder calculates paths
4. SimulationStateBuilder captures snapshot
5. VisualizationServer broadcasts to Unity
6. SimulationClient receives and parses
7. Visualizers update Unity GameObjects
8. Camera renders 3D scene

## 🌟 Benefits Over Console Output

| Console | Unity Visualization |
|---------|-------------------|
| `Position: (1.23, 2.34, 3.45)` | See entity at that location |
| `Velocity: (0.5, -2.0, 0.3)` | See yellow arrow showing direction/magnitude |
| `NavMesh: 3 polygons` | See green walkable surface |
| `Path: 2 waypoints` | See cyan line with markers |
| Hard to understand | Intuitive and immediate |

## 🛠️ Troubleshooting

**Common Issues:**

1. **"Connection failed"**
   - Start C# server first
   - Check port 8181 is not blocked

2. **"Package not found"**
   - Install NativeWebSocket and Newtonsoft.Json
   - Restart Unity after installation

3. **"Nothing appears"**
   - Position camera to see simulation area
   - Check all scripts are on same GameObject

See [UNITY_SETUP_GUIDE.md](UNITY_SETUP_GUIDE.md) for detailed troubleshooting.

## 📦 Package Info

Compatible with:
- ✅ Unity 2021.3 LTS
- ✅ Unity 2022 LTS
- ✅ Unity 2023+
- ✅ Windows, macOS, Linux Editor
- ✅ Standalone builds
- ⚠️ WebGL (requires WebSocket polyfill)

## 🤝 Integration

### Use in Your Project
1. Copy `Scripts/` folder to your Assets
2. Install required packages
3. Add components to a GameObject
4. Connect to your simulation server

### Extend Functionality
All scripts are well-documented and extensible:
- Inherit from visualizer classes
- Add custom entity types
- Implement new rendering modes
- Add UI overlays

## 📝 License

Part of the Spatial Physics & Pathfinding project.

## 🎓 Learning Resources

- **Unity Basics**: https://learn.unity.com/
- **WebSocket in Unity**: https://github.com/endel/NativeWebSocket
- **BepuPhysics Docs**: https://github.com/bepu/bepuphysics2
- **DotRecast Docs**: https://github.com/ikpil/DotRecast

## 💡 Tips

1. **Start Simple**: Use auto-create materials first
2. **Camera First**: Position camera before running
3. **Watch Console**: Unity Console shows connection status
4. **Iterate Fast**: Keep Unity open while tweaking C# code
5. **Record Sessions**: Add recording to replay interesting moments

---

**Ready to visualize?** Start with [QUICK_START.md](QUICK_START.md)!
