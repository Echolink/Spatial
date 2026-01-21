# Spatial Systems Integration

**Complete integration between BepuPhysics v2 and DotRecast for game server development with real-time 3D visualization!**

## 🎉 Status: FULLY FUNCTIONAL ✅

All systems are operational and tested end-to-end!

## 📦 Project Structure

### Core Libraries
- **Spatial.Physics** - BepuPhysics v2.4.0 wrapper
  - Physics simulation with gravity
  - Collision detection and resolution
  - Static and dynamic entities
  - Capsule, Box, and Sphere shapes

- **Spatial.Pathfinding** - DotRecast 2026.1.1 wrapper
  - NavMesh generation from geometry
  - Pathfinding queries
  - Valid position checking
  - Agent configuration

- **Spatial.Integration** - Physics-Pathfinding bridge
  - NavMesh building from physics world
  - Movement controller for physics-based agents
  - Pathfinding service API

- **Spatial.Server** - Real-time visualization server ✨ NEW!
  - WebSocket server for broadcasting simulation state
  - JSON serialization of entities, NavMesh, and paths
  - Client connection management

### Applications
- **Spatial.TestHarness** - Integration tests
  - Physics collision tests
  - NavMesh generation tests (Direct DotRecast approach - 2x better quality!)
  - Full pathfinding integration tests
  - Real-time visualization broadcasting
  - ⭐ **Enhanced Simulation Test** - Production-ready validation suite with detailed metrics

- **Unity/** - 3D Visualization client ✨ NEW!
  - Real-time entity rendering
  - NavMesh surface visualization
  - Path and waypoint display
  - See [Unity/README.md](Unity/README.md) for setup

## ✨ Features

### Physics (BepuPhysics v2.4.0)
✅ Gravity and forces  
✅ Collision detection and resolution  
✅ Static and dynamic bodies  
✅ SpringSettings for stable contacts  
✅ Multiple shape types  
✅ Velocity-based movement  

### Pathfinding (DotRecast 2026.1.1)
✅ NavMesh generation from physics geometry  
✅ Pathfinding with DtNavMeshQuery  
✅ Waypoint paths  
✅ Valid position testing  
✅ Polygon-accurate navigation  

### Movement Integration
✅ Physics-based agent movement  
✅ Waypoint following  
✅ Path request and execution  
✅ Collision-aware navigation  
✅ **Agent blocking behavior** - Agents block each other instead of pushing  
✅ **Explicit push mechanics** - Push, knockback, and explosion forces  
✅ **Local avoidance system** - Steering behaviors for dynamic obstacles  

### 3D Visualization ✨ NEW!
✅ Real-time WebSocket streaming  
✅ Unity client for 3D viewing  
✅ Entity rendering (Box, Capsule, Sphere)  
✅ NavMesh surface visualization  
✅ Path and waypoint display  
✅ Velocity vector visualization  

## 🚀 Quick Start

### 1. Run the Tests

```bash
cd "c:\Users\nikog\Documents\Project\Physics"
dotnet run --project Spatial.TestHarness
```

This will:
- Start the physics simulation
- Generate NavMesh from obstacles
- Calculate paths
- Test agent movement
- **Start visualization server** on port 8181

### 2. View in 3D (Optional but Recommended!)

Follow the [Unity Setup Guide](Unity/QUICK_START.md) to see your simulation in real-time 3D!

**Quick Setup:**
1. Install Unity 2021.3+
2. Add NativeWebSocket and Newtonsoft.Json packages
3. Import Unity scripts
4. Press Play while simulation is running

See beautiful real-time visualization instead of just numbers!

## 📊 Test Results

### Test 1: Physics Collision ✅
- Entity falls with gravity (9.81 m/s²)
- Collides with ground plane
- Bounces and settles at y=1.50
- No NaN values - physics stable!

### Test 2: Full Integration ✅
- NavMesh generated: 20 vertices, 10 triangles, 3 walkable polygons
- Path found: 2 waypoints, length 7.07 units
- Agent moves from (0,1,0) toward (5,1,5)
- Distance reduced from 7.09 to 3.82 units in 1 second
- Movement speed: 3 units/second

## 📚 Documentation

- **[DOTRECAST_INTEGRATION_STATUS.md](DOTRECAST_INTEGRATION_STATUS.md)** - Complete integration details
- **[MOVEMENT_FLOW_GUIDE.md](MOVEMENT_FLOW_GUIDE.md)** - Movement system documentation
- **[AGENT_COLLISION_GUIDE.md](AGENT_COLLISION_GUIDE.md)** - Agent collision and push mechanics guide
- **[Unity/README.md](Unity/README.md)** - 3D visualization setup
- **[Unity/QUICK_START.md](Unity/QUICK_START.md)** - 5-minute setup guide

## 🔧 Requirements

- .NET 8.0 SDK
- Windows, Linux, or macOS
- (Optional) Unity 2021.3+ for visualization

## 🏗️ Architecture

```
BepuPhysics v2.4.0
    ↓ (static geometry)
NavMesh Generation (DotRecast)
    ↓ (walkable surface)
Pathfinding
    ↓ (waypoints)
Movement Controller
    ↓ (physics forces)
Physics Simulation
    ↓ (simulation state)
Visualization Server (WebSocket)
    ↓ (JSON updates)
Unity Client (3D View)
```

## 🎯 Use Cases

### Game Server Development
- Server-authoritative physics
- AI pathfinding for NPCs
- Obstacle avoidance
- Dynamic world navigation

### Debugging & Testing
- Visualize physics interactions
- Verify pathfinding correctness
- Tune movement parameters
- Monitor performance

### Rapid Prototyping
- Fast iteration with Unity visualization
- Test scenarios in 3D
- Validate game mechanics
- Demo to stakeholders

## 🔑 Key Learnings

### BepuPhysics v2.4.0
- Requires explicit `SpringSettings(30f, 1f)` for contacts
- Static bodies must use `simulation.Statics`
- Bodies must be awakened when setting velocity
- Low friction (0.1) needed for smooth character movement

### DotRecast 2026.1.1
- `RcSimpleInputGeomProvider` for geometry input
- `RcBuilder.Build()` generates mesh data
- `DtNavMeshBuilder.CreateNavMeshData()` creates queryable mesh
- Proper cell size crucial for quality

### Integration
- Store shape indices in entities for navmesh extraction
- Skip waypoints at same horizontal position
- Use XZ distance for waypoint reach threshold
- Broadcast state at 60 FPS for smooth visualization

## 🌟 What's New in This Version

### ⭐ Enhanced Simulation Test Suite
- **Production-Ready Validation**: Comprehensive test with detailed metrics
- **Multi-Agent Testing**: Test 1-10 agents simultaneously with diverse scenarios
- **Direct NavMesh Generation**: 2x better quality than physics-based approach (~823 vs ~416 triangles)
- **Performance Metrics**: Track generation time, success rates, path efficiency, agent speeds
- **Flexible Configuration**: Configurable agent count, custom meshes, navmesh export

**Quick Start**:
```bash
cd Spatial.TestHarness
dotnet run -- enhanced              # Run with 5 agents (default)
dotnet run -- enhanced 10           # Stress test with 10 agents
dotnet run -- enhanced 1 --export-navmesh  # Debug + export
```

### Real-Time 3D Visualization
- **WebSocket Server**: Broadcasts simulation state to Unity clients
- **Unity Scripts**: Complete visualization system
- **Entity Rendering**: See boxes, capsules, spheres in 3D
- **NavMesh Display**: Green semi-transparent walkable surface
- **Path Visualization**: Cyan lines with waypoint markers
- **Velocity Vectors**: Yellow arrows showing movement direction

### Benefits
- **Faster Development**: See issues immediately
- **Better Understanding**: Intuitive 3D view vs console numbers
- **Easy Debugging**: Visual inspection of behavior
- **Great Demos**: Show stakeholders real-time 3D simulation

## 📝 License

MIT License - See project for details

## 🔗 Dependencies

- [BepuPhysics v2](https://github.com/bepu/bepuphysics2) - High-performance physics engine
- [DotRecast](https://github.com/ikpil/DotRecast) - C# port of Recast navigation
- [Fleck](https://github.com/statianzo/Fleck) - WebSocket server
- [Newtonsoft.Json](https://www.newtonsoft.com/json) - JSON serialization

## 🤝 Contributing

This is a complete, working integration. Feel free to:
- Add more shape types
- Implement dynamic obstacles
- Add navmesh streaming
- Enhance visualization
- Optimize performance

---

**Ready to see your simulation in 3D?** Check out [Unity/QUICK_START.md](Unity/QUICK_START.md)!

