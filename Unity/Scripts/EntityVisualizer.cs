using System.Collections.Generic;
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
        
        [Header("Auto Create Materials")]
        public bool autoCreateMaterials = true;
        
        private Dictionary<int, GameObject> entityObjects = new Dictionary<int, GameObject>();
        private Dictionary<int, LineRenderer> velocityLines = new Dictionary<int, LineRenderer>();
        
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
            
            // Initial update
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
                
                default:
                    shapeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    shapeObj.name = "UnknownShape";
                    break;
            }
            
            // Remove collider (we're just visualizing, not simulating in Unity)
            Destroy(shapeObj.GetComponent<Collider>());
            
            return shapeObj;
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
                entityObj.transform.position = new Vector3(
                    state.Position[0],
                    state.Position[1],
                    state.Position[2]
                );
            }
            
            // Update rotation
            if (state.Rotation.Length >= 4)
            {
                entityObj.transform.rotation = new Quaternion(
                    state.Rotation[0],
                    state.Rotation[1],
                    state.Rotation[2],
                    state.Rotation[3]
                );
            }
            
            // Update velocity visualization
            if (velocityLines.TryGetValue(state.Id, out var velocityLine))
            {
                if (state.Velocity.Length >= 3)
                {
                    Vector3 position = entityObj.transform.position;
                    Vector3 velocity = new Vector3(state.Velocity[0], state.Velocity[1], state.Velocity[2]);
                    
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
            
            if (velocityLines.ContainsKey(id))
            {
                velocityLines.Remove(id);
            }
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
        }
    }
}
