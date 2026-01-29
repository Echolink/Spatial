using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spatial.Unity
{
    /// <summary>
    /// Visualizes agent paths and waypoints for debugging pathfinding.
    /// </summary>
    public class PathVisualizer : MonoBehaviour
    {
        [Header("Waypoint Visualization")]
        [Tooltip("Show waypoint markers")]
        public bool showWaypoints = true;
        
        [Tooltip("Show path lines connecting waypoints")]
        public bool showPathLines = true;
        
        [Tooltip("Size of waypoint spheres")]
        [Range(0.1f, 2.0f)]
        public float waypointSize = 0.3f;
        
        [Tooltip("Color for waypoint markers")]
        public Color waypointColor = new Color(1.0f, 1.0f, 0.0f, 0.8f); // Yellow
        
        [Tooltip("Color for path lines")]
        public Color pathLineColor = new Color(0.0f, 1.0f, 0.0f, 0.6f); // Green
        
        [Tooltip("Width of path lines")]
        [Range(0.01f, 0.5f)]
        public float pathLineWidth = 0.1f;
        
        [Tooltip("Material for waypoint spheres")]
        public Material waypointMaterial;
        
        [Header("Path Labels")]
        [Tooltip("Show entity ID labels on paths")]
        public bool showEntityLabels = true;
        
        private Dictionary<int, PathVisualization> pathVisualizations = new Dictionary<int, PathVisualization>();
        
        private class PathVisualization
        {
            public GameObject pathRoot;
            public List<GameObject> waypointMarkers = new List<GameObject>();
            public LineRenderer pathLine;
            public TextMesh label;
        }
        
        void Start()
        {
            Debug.Log("[PathVisualizer] Starting up...");
            Debug.Log($"[PathVisualizer] Show Waypoints: {showWaypoints}, Show Path Lines: {showPathLines}");
            
            // Auto-create waypoint material if needed
            if (waypointMaterial == null)
            {
                waypointMaterial = CreateWaypointMaterial();
                Debug.Log("[PathVisualizer] Created default waypoint material");
            }
            
            // Subscribe to simulation updates
            var client = GetComponent<SimulationClient>();
            if (client != null)
            {
                client.OnStateReceived += UpdatePathVisualization;
                Debug.Log("[PathVisualizer] Successfully subscribed to SimulationClient updates");
            }
            else
            {
                Debug.LogError("[PathVisualizer] No SimulationClient found on this GameObject!");
            }
        }
        
        /// <summary>
        /// Update path visualization based on simulation state
        /// </summary>
        private void UpdatePathVisualization(SimulationState state)
        {
            // DEBUG: Always log state updates
            int pathCount = state.AgentPaths != null ? state.AgentPaths.Count : 0;
            int entityCount = state.Entities != null ? state.Entities.Count : 0;
            Debug.Log($"[PathVisualizer] State update received - Entities: {entityCount}, Agent paths: {pathCount}");
            
            if (!showWaypoints && !showPathLines)
            {
                ClearAllPaths();
                return;
            }
            
            // Track which paths are in current state
            HashSet<int> currentPathEntityIds = new HashSet<int>();
            
            // Update or create path visualizations
            if (state.AgentPaths != null)
            {
                foreach (var pathData in state.AgentPaths)
                {
                    currentPathEntityIds.Add(pathData.EntityId);
                    
                    // DEBUG: Log waypoint count for this agent
                    Debug.Log($"[PathVisualizer] Agent {pathData.EntityId} has {pathData.Waypoints.Count} waypoints");
                    
                    if (!pathVisualizations.ContainsKey(pathData.EntityId))
                    {
                        CreatePathVisualization(pathData);
                    }
                    else
                    {
                        UpdatePathVisualization(pathData);
                    }
                }
            }
            
            // Remove paths that no longer exist
            List<int> toRemove = new List<int>();
            foreach (var entityId in pathVisualizations.Keys)
            {
                if (!currentPathEntityIds.Contains(entityId))
                {
                    toRemove.Add(entityId);
                }
            }
            
            foreach (var entityId in toRemove)
            {
                DestroyPathVisualization(entityId);
            }
        }
        
        /// <summary>
        /// Create visualization for a new path
        /// </summary>
        private void CreatePathVisualization(PathData pathData)
        {
            Debug.Log($"[PathVisualizer] Creating visualization for Agent {pathData.EntityId} with {pathData.Waypoints.Count} waypoints");
            
            var viz = new PathVisualization();
            
            // Create root GameObject
            viz.pathRoot = new GameObject($"Path_Entity_{pathData.EntityId}");
            viz.pathRoot.transform.SetParent(transform);
            
            // Create waypoint markers
            if (showWaypoints && pathData.Waypoints.Count > 0)
            {
                Debug.Log($"[PathVisualizer] Creating {pathData.Waypoints.Count} waypoint markers for Agent {pathData.EntityId}");
                for (int i = 0; i < pathData.Waypoints.Count; i++)
                {
                    var waypoint = pathData.Waypoints[i];
                    var marker = CreateWaypointMarker(waypoint, i, viz.pathRoot.transform);
                    viz.waypointMarkers.Add(marker);
                }
            }
            
            // Create path line
            if (showPathLines && pathData.Waypoints.Count > 1)
            {
                Debug.Log($"[PathVisualizer] Creating path line for Agent {pathData.EntityId}");
                viz.pathLine = CreatePathLine(pathData.Waypoints, viz.pathRoot);
            }
            
            // Create label
            if (showEntityLabels)
            {
                viz.label = CreatePathLabel(pathData.EntityId, pathData.Waypoints, viz.pathRoot.transform);
            }
            
            pathVisualizations[pathData.EntityId] = viz;
            Debug.Log($"[PathVisualizer] Finished creating visualization for Agent {pathData.EntityId}");
        }
        
        /// <summary>
        /// Update existing path visualization
        /// </summary>
        private void UpdatePathVisualization(PathData pathData)
        {
            var viz = pathVisualizations[pathData.EntityId];
            
            // Clear old markers
            foreach (var marker in viz.waypointMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }
            viz.waypointMarkers.Clear();
            
            // Recreate waypoint markers
            if (showWaypoints && pathData.Waypoints.Count > 0)
            {
                for (int i = 0; i < pathData.Waypoints.Count; i++)
                {
                    var waypoint = pathData.Waypoints[i];
                    var marker = CreateWaypointMarker(waypoint, i, viz.pathRoot.transform);
                    viz.waypointMarkers.Add(marker);
                }
            }
            
            // Update path line
            if (showPathLines && pathData.Waypoints.Count > 1)
            {
                if (viz.pathLine == null)
                {
                    viz.pathLine = CreatePathLine(pathData.Waypoints, viz.pathRoot);
                }
                else
                {
                    UpdatePathLine(viz.pathLine, pathData.Waypoints);
                }
            }
            else if (viz.pathLine != null)
            {
                Destroy(viz.pathLine.gameObject);
                viz.pathLine = null;
            }
            
            // Update label
            if (showEntityLabels && viz.label != null && pathData.Waypoints.Count > 0)
            {
                var firstWaypoint = pathData.Waypoints[0];
                // Coordinate transformation: negate X for Unity's left-handed system
                viz.label.transform.position = new Vector3(
                    -firstWaypoint[0],
                    firstWaypoint[1] + 2.0f,
                    firstWaypoint[2]
                );
            }
        }
        
        /// <summary>
        /// Create a waypoint marker sphere
        /// </summary>
        private GameObject CreateWaypointMarker(float[] waypoint, int index, Transform parent)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Waypoint_{index}";
            marker.transform.SetParent(parent);
            
            // Coordinate transformation: negate X for Unity's left-handed system
            marker.transform.position = new Vector3(
                -waypoint[0],  // Negate X
                waypoint[1],
                waypoint[2]
            );
            marker.transform.localScale = Vector3.one * waypointSize;
            
            // Remove collider (visualization only)
            var collider = marker.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            
            // Set material
            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null && waypointMaterial != null)
            {
                renderer.material = waypointMaterial;
            }
            
            return marker;
        }
        
        /// <summary>
        /// Create a line renderer for the path
        /// </summary>
        private LineRenderer CreatePathLine(List<float[]> waypoints, GameObject parent)
        {
            GameObject lineObj = new GameObject("PathLine");
            lineObj.transform.SetParent(parent.transform);
            
            var lineRenderer = lineObj.AddComponent<LineRenderer>();
            lineRenderer.startWidth = pathLineWidth;
            lineRenderer.endWidth = pathLineWidth;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = pathLineColor;
            lineRenderer.endColor = pathLineColor;
            
            UpdatePathLine(lineRenderer, waypoints);
            
            return lineRenderer;
        }
        
        /// <summary>
        /// Update line renderer positions
        /// </summary>
        private void UpdatePathLine(LineRenderer lineRenderer, List<float[]> waypoints)
        {
            lineRenderer.positionCount = waypoints.Count;
            
            for (int i = 0; i < waypoints.Count; i++)
            {
                var waypoint = waypoints[i];
                // Coordinate transformation: negate X for Unity's left-handed system
                lineRenderer.SetPosition(i, new Vector3(
                    -waypoint[0],  // Negate X
                    waypoint[1],
                    waypoint[2]
                ));
            }
        }
        
        /// <summary>
        /// Create a text label for the path
        /// </summary>
        private TextMesh CreatePathLabel(int entityId, List<float[]> waypoints, Transform parent)
        {
            if (waypoints.Count == 0)
                return null;
            
            GameObject labelObj = new GameObject($"Label_Entity_{entityId}");
            labelObj.transform.SetParent(parent);
            
            var firstWaypoint = waypoints[0];
            // Coordinate transformation: negate X for Unity's left-handed system
            labelObj.transform.position = new Vector3(
                -firstWaypoint[0],
                firstWaypoint[1] + 2.0f,  // Float above first waypoint
                firstWaypoint[2]
            );
            
            var textMesh = labelObj.AddComponent<TextMesh>();
            textMesh.text = $"Agent {entityId}";
            textMesh.fontSize = 20;
            textMesh.color = waypointColor;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.1f;
            
            return textMesh;
        }
        
        /// <summary>
        /// Destroy path visualization for an entity
        /// </summary>
        private void DestroyPathVisualization(int entityId)
        {
            if (pathVisualizations.TryGetValue(entityId, out var viz))
            {
                if (viz.pathRoot != null)
                    Destroy(viz.pathRoot);
                
                pathVisualizations.Remove(entityId);
            }
        }
        
        /// <summary>
        /// Clear all path visualizations
        /// </summary>
        private void ClearAllPaths()
        {
            foreach (var viz in pathVisualizations.Values)
            {
                if (viz.pathRoot != null)
                    Destroy(viz.pathRoot);
            }
            pathVisualizations.Clear();
        }
        
        /// <summary>
        /// Create a default material for waypoints
        /// </summary>
        private Material CreateWaypointMaterial()
        {
            var material = new Material(Shader.Find("Standard"));
            material.name = "WaypointMaterial";
            material.color = waypointColor;
            material.SetFloat("_Metallic", 0.0f);
            material.SetFloat("_Glossiness", 0.5f);
            return material;
        }
        
        void OnDestroy()
        {
            ClearAllPaths();
        }
    }
}
