# Unity Visualization - Quick Start

## 5-Minute Setup

### 1. Install Packages (Unity)
```
Package Manager > + > Add package from git URL
https://github.com/endel/NativeWebSocket.git#upm

Package Manager > Unity Registry > "Newtonsoft Json" > Install
```

### 2. Import Scripts
Copy to Unity `Assets/Spatial/`:
- `SimulationClient.cs`
- `EntityVisualizer.cs`
- `NavMeshVisualizer.cs`

### 3. Setup Scene
1. Create Empty GameObject → Name: "SimulationVisualizer"
2. Add all 3 scripts to it
3. Check "Auto Connect" on SimulationClient
4. Check "Auto Create Materials" on both visualizers

### 4. Run
```bash
# Terminal 1: Start C# server
cd "c:\Users\nikog\Documents\Project\Physics"
dotnet run --project Spatial.TestHarness

# Wait for: "[Info] Waiting 3 seconds for Unity client to connect..."

# Unity: Press Play
```

## That's It!

You should see:
- ✅ Gray ground plane
- ✅ Blue/Orange entities
- ✅ Green NavMesh surface
- ✅ Cyan path lines
- ✅ Yellow velocity vectors

## Troubleshooting

**Not connecting?**
1. C# server must start FIRST
2. Check URL: `ws://localhost:8181`
3. Check firewall

**Can't see anything?**
1. Move camera to `(10, 10, 10)` looking at origin
2. Check Unity Console for errors

**Package errors?**
1. Restart Unity after installing packages
2. Check Package Manager shows both packages installed

For detailed setup, see `UNITY_SETUP_GUIDE.md`
