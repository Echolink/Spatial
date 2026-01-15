using System;
using System.Collections.Generic;
using UnityEngine;
using NativeWebSocket;
using Newtonsoft.Json;

namespace Spatial.Unity
{
    /// <summary>
    /// WebSocket client that connects to the Spatial.Server visualization server
    /// and receives real-time simulation state updates.
    /// </summary>
    public class SimulationClient : MonoBehaviour
    {
        [Header("Connection Settings")]
        [Tooltip("Server address (e.g., ws://localhost:8181)")]
        public string serverUrl = "ws://localhost:8181";
        
        [Header("Auto Connect")]
        public bool autoConnect = true;
        
        [Header("Status")]
        [SerializeField] private bool isConnected = false;
        [SerializeField] private float lastUpdateTime = 0f;
        
        // Events
        public event Action<SimulationState> OnStateReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;
        
        private WebSocket websocket;
        
        async void Start()
        {
            if (autoConnect)
            {
                await Connect();
            }
        }
        
        void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // Dispatch WebSocket messages on the main thread
            websocket?.DispatchMessageQueue();
#endif
        }
        
        /// <summary>
        /// Connect to the visualization server
        /// </summary>
        public async System.Threading.Tasks.Task Connect()
        {
            if (websocket != null && websocket.State != WebSocketState.Closed)
            {
                Debug.LogWarning("[SimulationClient] Already connected or connecting");
                return;
            }
            
            try
            {
                Debug.Log($"[SimulationClient] Connecting to {serverUrl}...");
                
                websocket = new WebSocket(serverUrl);
                
                websocket.OnOpen += () =>
                {
                    Debug.Log("[SimulationClient] Connected!");
                    isConnected = true;
                    OnConnected?.Invoke();
                };
                
                websocket.OnMessage += (bytes) =>
                {
                    try
                    {
                        string json = System.Text.Encoding.UTF8.GetString(bytes);
                        var state = JsonConvert.DeserializeObject<SimulationState>(json);
                        
                        lastUpdateTime = Time.time;
                        OnStateReceived?.Invoke(state);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SimulationClient] Failed to parse state: {ex.Message}");
                    }
                };
                
                websocket.OnError += (errorMsg) =>
                {
                    Debug.LogError($"[SimulationClient] WebSocket error: {errorMsg}");
                };
                
                websocket.OnClose += (closeCode) =>
                {
                    Debug.Log($"[SimulationClient] Disconnected (code: {closeCode})");
                    isConnected = false;
                    OnDisconnected?.Invoke();
                };
                
                await websocket.Connect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SimulationClient] Connection failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Disconnect from the server
        /// </summary>
        public async void Disconnect()
        {
            if (websocket != null)
            {
                await websocket.Close();
                websocket = null;
            }
        }
        
        async void OnDestroy()
        {
            if (websocket != null)
            {
                await websocket.Close();
            }
        }
        
        async void OnApplicationQuit()
        {
            if (websocket != null)
            {
                await websocket.Close();
            }
        }
        
        public bool IsConnected => isConnected;
    }
    
    #region Data Models
    
    /// <summary>
    /// Complete simulation state snapshot
    /// </summary>
    [Serializable]
    public class SimulationState
    {
        public float Timestamp;
        public List<EntityState> Entities = new List<EntityState>();
        public NavMeshGeometry NavMesh;
        public PathData CurrentPath;
    }
    
    /// <summary>
    /// State of a single entity in the simulation
    /// </summary>
    [Serializable]
    public class EntityState
    {
        public int Id;
        public string Type;
        public float[] Position; // [x, y, z]
        public float[] Rotation; // [x, y, z, w] quaternion
        public float[] Size;     // [width, height, length] or [radius, height, radius]
        public float[] Velocity; // [x, y, z]
        public bool IsStatic;
        public string ShapeType; // "Box", "Capsule", "Sphere"
    }
    
    /// <summary>
    /// NavMesh geometry data
    /// </summary>
    [Serializable]
    public class NavMeshGeometry
    {
        public List<float[]> Vertices = new List<float[]>(); // Each vertex is [x, y, z]
        public List<int> Indices = new List<int>();
        public int PolygonCount;
    }
    
    /// <summary>
    /// Current pathfinding path
    /// </summary>
    [Serializable]
    public class PathData
    {
        public List<float[]> Waypoints = new List<float[]>(); // Each waypoint is [x, y, z]
        public float PathLength;
        public int EntityId;
    }
    
    #endregion
}
