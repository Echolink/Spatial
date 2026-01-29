using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spatial.Unity
{
    /// <summary>
    /// Visualizes physics entities from the simulation.
    /// Creates and updates GameObjects to match the simulation state.
    /// </summary>
    public class EntityVisualizer : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [Tooltip("Material for static objects")]
        public Material staticMaterial;
        
        [Tooltip("Material for dynamic objects")]
        public Material dynamicMaterial;
        
        [Tooltip("Material for agents")]
        public Material agentMaterial;
        
        [Header("Velocity Visualization")]
        public bool showVelocityVectors = true;
        public float velocityVectorScale = 0.3f;
        
        [Header("Position Smoothing")]
        [Tooltip("Enable smooth interpolation between positions (fixes laggy appearance)")]
        public bool enableSmoothing = true;
        
        [Tooltip("Interpolation speed (higher = more responsive, 15-20 recommended)")]
        [Range(1f, 30f)]
        public float smoothingSpeed = 18f;
        
        [Header("Auto Create Materials")]
        public bool autoCreateMaterials = true;
        
    private Dictionary<int, GameObject> entityObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, LineRenderer> velocityLines = new Dictionary<int, LineRenderer>();
    private Dictionary<int, Vector3> targetPositions = new Dictionary<int, Vector3>();
    private Dictionary<int, Quaternion> targetRotations = new Dictionary<int, Quaternion>();
    private Dictionary<int, Vector3> previousTargetPositions = new Dictionary<int, Vector3>();
        
        void Start()
        {
            // Auto-create default materials if needed
            if (autoCreateMaterials)
            {
                if (staticMaterial == null)
                {
                    staticMaterial = CreateDefaultMaterial("StaticMaterial", new Color(0.5f, 0.5f, 0.5f));
                }
                if (dynamicMaterial == null)
                {
                    dynamicMaterial = CreateDefaultMaterial("DynamicMaterial", new Color(0.2f, 0.6f, 1.0f));
                }
                if (agentMaterial == null)
                {
                    agentMaterial = CreateDefaultMaterial("AgentMaterial", new Color(1.0f, 0.5f, 0.2f));
                }
            }
            
            // Subscribe to simulation updates
            var client = GetComponent<SimulationClient>();
            if (client != null)
            {
                client.OnStateReceived += UpdateVisualization;
            }
            else
            {
                Debug.LogError("[EntityVisualizer] No SimulationClient found on this GameObject!");
            }
        }
        
        /// <summary>
        /// Continue interpolating towards target positions between WebSocket updates.
        /// This provides smooth movement at Unity's frame rate regardless of WebSocket rate.
        /// </summary>
        void Update()
        {
            if (!enableSmoothing)
                return;
            
            // Interpolate ALL entities towards their target positions
            // This runs every Unity frame (60-144 FPS) for ultra-smooth movement
            foreach (var kvp in entityObjects)
            {
                int entityId = kvp.Key;
                GameObject entityObj = kvp.Value;
                
                // Interpolate position if we have a target
                if (targetPositions.TryGetValue(entityId, out Vector3 targetPos))
                {
                    // Use frame-rate independent interpolation
                    // Higher smoothingSpeed = more responsive, lower = smoother but more lag
                    float t = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
                    
                    Vector3 currentPos = entityObj.transform.position;
                    
                    // Detect if entity is on a slope/ramp by checking height change rate
                    bool isOnSlope = false;
                    if (previousTargetPositions.TryGetValue(entityId, out Vector3 prevTarget))
                    {
                        float horizontalDist = Mathf.Sqrt(
                            (targetPos.x - prevTarget.x) * (targetPos.x - prevTarget.x) +
                            (targetPos.z - prevTarget.z) * (targetPos.z - prevTarget.z)
                        );
                        float heightChange = Mathf.Abs(targetPos.y - prevTarget.y);
                        
                        // If height changes significantly relative to horizontal distance, we're on a slope
                        if (horizontalDist > 0.01f && heightChange > 0.05f)
                        {
                            float slope = heightChange / horizontalDist;
                            isOnSlope = slope > 0.1f; // More than 10% grade = slope
                        }
                    }
                    
                    if (isOnSlope)
                    {
                        // On slope: Apply smoothing to ALL axes to follow the waypoint path precisely
                        // This prevents cutting corners around ramps
                        entityObj.transform.position = Vector3.Lerp(currentPos, targetPos, t);
                    }
                    else
                    {
                        // Flat terrain: Apply Y instantly to prevent sinking, smooth X/Z for fluid movement
                        entityObj.transform.position = new Vector3(
                            Mathf.Lerp(currentPos.x, targetPos.x, t),  // Smooth X
                            targetPos.y,                                 // Instant Y (prevents sinking)
                            Mathf.Lerp(currentPos.z, targetPos.z, t)   // Smooth Z
                        );
                    }
                }
                
                // Interpolate rotation if we have a target
                if (targetRotations.TryGetValue(entityId, out Quaternion targetRot))
                {
                    float t = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
                    entityObj.transform.rotation = Quaternion.Slerp(
                        entityObj.transform.rotation,
                        targetRot,
                        t
                    );
                }
            }
        }
        
        /// <summary>
        /// Update the visualization based on the latest simulation state
        /// </summary>
        public void UpdateVisualization(SimulationState state)
        {
            // Track which entities are in the current state
            HashSet<int> currentEntityIds = new HashSet<int>();
            
            // Update or create entities
            foreach (var entityState in state.Entities)
            {
                currentEntityIds.Add(entityState.Id);
                
                if (!entityObjects.ContainsKey(entityState.Id))
                {
                    CreateEntityObject(entityState);
                }
                else
                {
                    UpdateEntityObject(entityState);
                }
            }
            
            // Remove entities that no longer exist
            List<int> toRemove = new List<int>();
            foreach (var id in entityObjects.Keys)
            {
                if (!currentEntityIds.Contains(id))
                {
                    toRemove.Add(id);
                }
            }
            
            foreach (var id in toRemove)
            {
                DestroyEntity(id);
            }
        }
        
        /// <summary>
        /// Create a new GameObject for an entity
        /// </summary>
        private void CreateEntityObject(EntityState state)
        {
            GameObject entityObj = new GameObject($"Entity_{state.Id}_{state.Type}");
            entityObj.transform.SetParent(transform);
            
            // Log entities at or near (0,0,0) to help identify the debug cube issue
            if (state.Position.Length >= 3)
            {
                float distFromOrigin = Mathf.Sqrt(
                    state.Position[0] * state.Position[0] + 
                    state.Position[1] * state.Position[1] + 
                    state.Position[2] * state.Position[2]
                );
                
                if (distFromOrigin < 1.0f)
                {
                    Debug.Log($"[EntityVisualizer] Entity near origin detected:");
                    Debug.Log($"  ID: {state.Id}, Type: {state.Type}, Shape: {state.ShapeType}");
                    Debug.Log($"  Position: ({state.Position[0]:F2}, {state.Position[1]:F2}, {state.Position[2]:F2})");
                    Debug.Log($"  IsStatic: {state.IsStatic}");
                    if (state.Size.Length > 0)
                    {
                        Debug.Log($"  Size: ({string.Join(", ", state.Size.Select(s => s.ToString("F2")))})");
                    }
                }
            }
            
            // Create the appropriate shape
            GameObject visualObj = CreateShapeObject(state);
            visualObj.transform.SetParent(entityObj.transform);
            
            // Set material based on type
            var renderer = visualObj.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (state.IsStatic && staticMaterial != null)
                    renderer.material = staticMaterial;
                else if (state.Type == "Agent" && agentMaterial != null)
                    renderer.material = agentMaterial;
                else if (dynamicMaterial != null)
                    renderer.material = dynamicMaterial;
            }
            
            // Create velocity visualizer
            if (showVelocityVectors && !state.IsStatic)
            {
                var velocityLine = CreateVelocityLine(entityObj);
                velocityLines[state.Id] = velocityLine;
            }
            
            entityObjects[state.Id] = entityObj;
            
            // Set initial position DIRECTLY (no interpolation for spawn)
            // This prevents the agent from appearing at (0,0,0) and slowly moving to spawn point
            if (state.Position.Length >= 3)
            {
                float yOffset = 0;
                if (state.ShapeType == "Capsule" && state.Size.Length >= 2)
                {
                    float capsuleHeight = state.Size[1];
                    yOffset = -capsuleHeight * 0.5f;
                }
                
                Vector3 spawnPos = new Vector3(
                    -state.Position[0],
                    state.Position[1] + yOffset,
                    state.Position[2]
                );
                
                entityObj.transform.position = spawnPos;
                targetPositions[state.Id] = spawnPos; // Store as target too
            }
            
            if (state.Rotation.Length >= 4)
            {
                Quaternion spawnRot = new Quaternion(
                    state.Rotation[0],
                    state.Rotation[1],
                    state.Rotation[2],
                    state.Rotation[3]
                );
                
                entityObj.transform.rotation = spawnRot;
                targetRotations[state.Id] = spawnRot;
            }
            
            // Now update normally for subsequent frames
            UpdateEntityObject(state);
        }
        
        /// <summary>
        /// Create the appropriate shape GameObject
        /// </summary>
        private GameObject CreateShapeObject(EntityState state)
        {
            GameObject shapeObj;
            
            switch (state.ShapeType)
            {
                case "Box":
                    shapeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    shapeObj.name = "BoxShape";
                    if (state.Size.Length >= 3)
                    {
                        shapeObj.transform.localScale = new Vector3(state.Size[0], state.Size[1], state.Size[2]);
                    }
                    break;
                
                case "Capsule":
                    shapeObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    shapeObj.name = "CapsuleShape";
                    if (state.Size.Length >= 3)
                    {
                        // Unity capsule: diameter = size[0], height = size[1]
                        shapeObj.transform.localScale = new Vector3(state.Size[0], state.Size[1] * 0.5f, state.Size[0]);
                    }
                    break;
                
                case "Sphere":
                    shapeObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    shapeObj.name = "SphereShape";
                    if (state.Size.Length >= 1)
                    {
                        float diameter = state.Size[0];
                        shapeObj.transform.localScale = new Vector3(diameter, diameter, diameter);
                    }
                    break;
                
                case "Mesh":
                    shapeObj = CreateMeshObject(state);
                    shapeObj.name = "MeshShape";
                    break;
                
                default:
                    shapeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    shapeObj.name = "UnknownShape";
                    break;
            }
            
            // Remove collider (we're just visualizing, not simulating in Unity)
            var collider = shapeObj.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            
            return shapeObj;
        }
        
        /// <summary>
        /// Create a GameObject with custom mesh from entity state
        /// </summary>
        private GameObject CreateMeshObject(EntityState state)
        {
            GameObject meshObj = new GameObject("CustomMesh");
            
            if (state.Mesh != null && state.Mesh.Vertices.Count > 0 && state.Mesh.Indices.Count > 0)
            {
                // Create Unity mesh
                Mesh mesh = new Mesh();
                mesh.name = $"Entity_{state.Id}_Mesh";
                
                // Convert vertex data with coordinate system transformation
                // Unity uses left-handed coordinate system, server uses right-handed
                // Negate X-axis to match Unity's OBJ importer behavior
                Vector3[] vertices = new Vector3[state.Mesh.Vertices.Count];
                for (int i = 0; i < state.Mesh.Vertices.Count; i++)
                {
                    var v = state.Mesh.Vertices[i];
                    if (v.Length >= 3)
                    {
                        vertices[i] = new Vector3(-v[0], v[1], v[2]);
                    }
                }
                
                // Set mesh data with reversed winding order
                // When we flip X-axis, we need to reverse triangle winding to maintain correct normals
                int[] triangles = new int[state.Mesh.Indices.Count];
                for (int i = 0; i < state.Mesh.Indices.Count; i += 3)
                {
                    triangles[i] = state.Mesh.Indices[i];
                    triangles[i + 1] = state.Mesh.Indices[i + 2]; // Swap indices 1 and 2
                    triangles[i + 2] = state.Mesh.Indices[i + 1];
                }
                
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                
                // Recalculate normals and bounds
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                
                // Add mesh filter and renderer
                var meshFilter = meshObj.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;
                
                var meshRenderer = meshObj.AddComponent<MeshRenderer>();
                meshRenderer.material = new Material(Shader.Find("Standard"));
            }
            else
            {
                Debug.LogWarning($"[EntityVisualizer] Entity {state.Id} is marked as Mesh but has no mesh data!");
                Debug.LogWarning($"[EntityVisualizer] This entity is at position ({state.Position[0]:F2}, {state.Position[1]:F2}, {state.Position[2]:F2})");
                Debug.LogWarning($"[EntityVisualizer] Entity Type: {state.Type}, IsStatic: {state.IsStatic}");
                
                // Fallback to a small cube
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallback.transform.SetParent(meshObj.transform);
                fallback.transform.localScale = Vector3.one * 0.5f;
            }
            
            return meshObj;
        }
        
        /// <summary>
        /// Create a line renderer for velocity visualization
        /// </summary>
        private LineRenderer CreateVelocityLine(GameObject parent)
        {
            GameObject lineObj = new GameObject("VelocityVector");
            lineObj.transform.SetParent(parent.transform);
            
            var lineRenderer = lineObj.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
            lineRenderer.positionCount = 2;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.yellow;
            lineRenderer.endColor = Color.red;
            
            return lineRenderer;
        }
        
        /// <summary>
        /// Update an existing entity's position and rotation
        /// </summary>
        private void UpdateEntityObject(EntityState state)
        {
            if (!entityObjects.TryGetValue(state.Id, out var entityObj))
                return;
            
            // Update position
            if (state.Position.Length >= 3)
            {
                // BepuPhysics Position = capsule center (for physics calculations)
                // For capsule visualization with feet-pivot preference:
                // - Physics reports center position (e.g., Y=1.0 for 2m capsule on ground at Y=0)
                // - Unity capsule pivot is at center by default
                // - User wants "feet on ground" visual (Y=0 means feet at Y=0)
                // - Solution: Offset visual position down by half the capsule height
                
                float yOffset = 0;
                if (state.ShapeType == "Capsule" && state.Size.Length >= 2)
                {
                    // Size[1] is capsule height in physics world
                    // Unity capsule scale is multiplied by 0.5f (see CreateShapeObject)
                    // So actual Unity capsule height = state.Size[1] * 0.5f * 2 (Unity default height)
                    // But Size[1] already contains the full physics height
                    float capsuleHeight = state.Size[1];
                    yOffset = -capsuleHeight * 0.5f;  // Move down by half height so feet touch ground
                }
                
                // Apply coordinate system transformation
                // Unity uses left-handed coordinate system, server uses right-handed
                // Negate X-axis to match Unity's OBJ importer behavior
                Vector3 targetPos = new Vector3(
                    -state.Position[0],
                    state.Position[1] + yOffset,  // Apply feet-pivot offset for capsules
                    state.Position[2]
                );
                
                // Store previous target for slope detection
                if (targetPositions.TryGetValue(state.Id, out Vector3 oldTarget))
                {
                    previousTargetPositions[state.Id] = oldTarget;
                }
                
                // Store target position for interpolation
                targetPositions[state.Id] = targetPos;
                
                // If smoothing disabled, apply position instantly
                if (!enableSmoothing || state.IsStatic)
                {
                    entityObj.transform.position = targetPos;
                }
            }
            
            // Update rotation
            if (state.Rotation.Length >= 4)
            {
                Quaternion targetRot = new Quaternion(
                    state.Rotation[0],
                    state.Rotation[1],
                    state.Rotation[2],
                    state.Rotation[3]
                );
                
                targetRotations[state.Id] = targetRot;
                
                // If smoothing disabled, apply rotation instantly
                if (!enableSmoothing || state.IsStatic)
                {
                    entityObj.transform.rotation = targetRot;
                }
            }
            
            // Update velocity visualization
            if (velocityLines.TryGetValue(state.Id, out var velocityLine))
            {
                if (state.Velocity.Length >= 3)
                {
                    Vector3 position = entityObj.transform.position;
                    // Apply coordinate system transformation to velocity
                    // Unity uses left-handed coordinate system, server uses right-handed
                    // Negate X-axis to match Unity's OBJ importer behavior
                    Vector3 velocity = new Vector3(-state.Velocity[0], state.Velocity[1], state.Velocity[2]);
                    
                    velocityLine.SetPosition(0, position);
                    velocityLine.SetPosition(1, position + velocity * velocityVectorScale);
                    
                    // Hide line if velocity is near zero
                    velocityLine.enabled = velocity.magnitude > 0.01f;
                }
            }
        }
        
        /// <summary>
        /// Destroy an entity GameObject
        /// </summary>
        private void DestroyEntity(int id)
        {
            if (entityObjects.TryGetValue(id, out var obj))
            {
                Destroy(obj);
                entityObjects.Remove(id);
            }
            
            velocityLines.Remove(id);
            targetPositions.Remove(id);
            targetRotations.Remove(id);
            previousTargetPositions.Remove(id);
        }
        
        /// <summary>
        /// Create a default material with the given color
        /// </summary>
        private Material CreateDefaultMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Standard"));
            material.name = name;
            material.color = color;
            return material;
        }
        
        void OnDestroy()
        {
            // Clean up all entity objects
            foreach (var obj in entityObjects.Values)
            {
                if (obj != null)
                    Destroy(obj);
            }
            entityObjects.Clear();
            velocityLines.Clear();
            targetPositions.Clear();
            targetRotations.Clear();
            previousTargetPositions.Clear();
        }
    }
}
