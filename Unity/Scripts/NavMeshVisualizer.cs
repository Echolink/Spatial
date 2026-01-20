using System.Collections.Generic;
using UnityEngine;

namespace Spatial.Unity
{
    /// <summary>
    /// Visualizes the DotRecast NavMesh and current pathfinding paths.
    /// </summary>
    public class NavMeshVisualizer : MonoBehaviour
    {
        [Header("NavMesh Visualization")]
        public bool showNavMesh = true;
        public Material navMeshMaterial;
        public Color navMeshColor = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        
        [Header("Path Visualization")]
        public bool showPath = true;
        public Color pathColor = Color.cyan;
        public float pathLineWidth = 0.1f;
        public bool showWaypoints = true;
        public float waypointRadius = 0.15f;
        
        [Header("Auto Create Materials")]
        public bool autoCreateMaterials = true;
        
        private GameObject navMeshObject;
        private GameObject pathObject;
        private LineRenderer pathLine;
        private List<GameObject> waypointObjects = new List<GameObject>();
        
        void Start()
        {
            // Auto-create materials if needed
            if (autoCreateMaterials && navMeshMaterial == null)
            {
                navMeshMaterial = CreateTransparentMaterial("NavMeshMaterial", navMeshColor);
            }
            
            // Subscribe to simulation updates
            var client = GetComponent<SimulationClient>();
            if (client != null)
            {
                client.OnStateReceived += UpdateVisualization;
            }
            else
            {
                Debug.LogError("[NavMeshVisualizer] No SimulationClient found on this GameObject!");
            }
        }
        
        /// <summary>
        /// Update the visualization based on the latest simulation state
        /// </summary>
        public void UpdateVisualization(SimulationState state)
        {
            if (showNavMesh && state.NavMesh != null)
            {
                UpdateNavMesh(state.NavMesh);
            }
            
            if (showPath && state.CurrentPath != null)
            {
                UpdatePath(state.CurrentPath);
            }
            else
            {
                ClearPath();
            }
        }
        
        /// <summary>
        /// Update or create the NavMesh visualization
        /// </summary>
        private void UpdateNavMesh(NavMeshGeometry navMesh)
        {
            // Skip if no data
            if (navMesh.Vertices.Count == 0 || navMesh.Indices.Count == 0)
            {
                ClearNavMesh();
                return;
            }
            
            // Create or reuse mesh object
            if (navMeshObject == null)
            {
                navMeshObject = new GameObject("NavMesh");
                navMeshObject.transform.SetParent(transform);
                
                var meshFilter = navMeshObject.AddComponent<MeshFilter>();
                var meshRenderer = navMeshObject.AddComponent<MeshRenderer>();
                meshRenderer.material = navMeshMaterial;
            }
            
            // Build Unity mesh from NavMesh data
            var mesh = new Mesh();
            mesh.name = "NavMesh";
            
            // Convert vertices with coordinate system transformation
            // Unity uses left-handed coordinate system, server uses right-handed
            // Negate X-axis to match Unity's OBJ importer behavior
            Vector3[] vertices = new Vector3[navMesh.Vertices.Count];
            for (int i = 0; i < navMesh.Vertices.Count; i++)
            {
                var v = navMesh.Vertices[i];
                if (v.Length >= 3)
                {
                    vertices[i] = new Vector3(-v[0], v[1], v[2]);
                }
            }
            
            // Convert indices and reverse winding order
            // When we flip X-axis, we need to reverse triangle winding to maintain correct normals
            int[] triangles = new int[navMesh.Indices.Count];
            for (int i = 0; i < navMesh.Indices.Count; i += 3)
            {
                triangles[i] = navMesh.Indices[i];
                triangles[i + 1] = navMesh.Indices[i + 2]; // Swap indices 1 and 2
                triangles[i + 2] = navMesh.Indices[i + 1];
            }
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            navMeshObject.GetComponent<MeshFilter>().mesh = mesh;
            
            Debug.Log($"[NavMeshVisualizer] Updated NavMesh: {vertices.Length} vertices, {triangles.Length / 3} triangles, {navMesh.PolygonCount} polygons");
        }
        
        /// <summary>
        /// Clear the NavMesh visualization
        /// </summary>
        private void ClearNavMesh()
        {
            if (navMeshObject != null)
            {
                Destroy(navMeshObject);
                navMeshObject = null;
            }
        }
        
        /// <summary>
        /// Update or create the path visualization
        /// </summary>
        private void UpdatePath(PathData path)
        {
            // Skip if no waypoints
            if (path.Waypoints.Count == 0)
            {
                ClearPath();
                return;
            }
            
            // Create path object if needed
            if (pathObject == null)
            {
                pathObject = new GameObject("CurrentPath");
                pathObject.transform.SetParent(transform);
                
                // Create line renderer
                pathLine = pathObject.AddComponent<LineRenderer>();
                pathLine.material = new Material(Shader.Find("Sprites/Default"));
                pathLine.startColor = pathColor;
                pathLine.endColor = pathColor;
                pathLine.startWidth = pathLineWidth;
                pathLine.endWidth = pathLineWidth;
                pathLine.positionCount = 0;
            }
            
            // Update line positions with coordinate system transformation
            // Unity uses left-handed coordinate system, server uses right-handed
            // Negate X-axis to match Unity's OBJ importer behavior
            pathLine.positionCount = path.Waypoints.Count;
            for (int i = 0; i < path.Waypoints.Count; i++)
            {
                var wp = path.Waypoints[i];
                if (wp.Length >= 3)
                {
                    pathLine.SetPosition(i, new Vector3(-wp[0], wp[1], wp[2]));
                }
            }
            
            // Update waypoint markers
            if (showWaypoints)
            {
                UpdateWaypoints(path.Waypoints);
            }
            else
            {
                ClearWaypoints();
            }
        }
        
        /// <summary>
        /// Update waypoint marker spheres
        /// </summary>
        private void UpdateWaypoints(List<float[]> waypoints)
        {
            // Remove excess waypoint objects
            while (waypointObjects.Count > waypoints.Count)
            {
                int lastIndex = waypointObjects.Count - 1;
                Destroy(waypointObjects[lastIndex]);
                waypointObjects.RemoveAt(lastIndex);
            }
            
            // Create or update waypoint objects
            for (int i = 0; i < waypoints.Count; i++)
            {
                GameObject waypointObj;
                
                if (i < waypointObjects.Count)
                {
                    waypointObj = waypointObjects[i];
                }
                else
                {
                    waypointObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    waypointObj.name = $"Waypoint_{i}";
                    waypointObj.transform.SetParent(pathObject.transform);
                    waypointObj.transform.localScale = Vector3.one * waypointRadius * 2;
                    
                    // Remove collider
                    Destroy(waypointObj.GetComponent<Collider>());
                    
                    // Set color
                    var renderer = waypointObj.GetComponent<Renderer>();
                    renderer.material = new Material(Shader.Find("Standard"));
                    renderer.material.color = pathColor;
                    
                    waypointObjects.Add(waypointObj);
                }
                
                // Update position with coordinate system transformation
                // Unity uses left-handed coordinate system, server uses right-handed
                // Negate X-axis to match Unity's OBJ importer behavior
                var wp = waypoints[i];
                if (wp.Length >= 3)
                {
                    waypointObj.transform.position = new Vector3(-wp[0], wp[1], wp[2]);
                }
            }
        }
        
        /// <summary>
        /// Clear waypoint markers
        /// </summary>
        private void ClearWaypoints()
        {
            foreach (var obj in waypointObjects)
            {
                if (obj != null)
                    Destroy(obj);
            }
            waypointObjects.Clear();
        }
        
        /// <summary>
        /// Clear the path visualization
        /// </summary>
        private void ClearPath()
        {
            if (pathObject != null)
            {
                Destroy(pathObject);
                pathObject = null;
                pathLine = null;
            }
            ClearWaypoints();
        }
        
        /// <summary>
        /// Create a transparent material with the given color
        /// </summary>
        private Material CreateTransparentMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Standard"));
            material.name = name;
            material.color = color;
            
            // Enable transparency
            material.SetFloat("_Mode", 3); // Transparent mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            
            return material;
        }
        
        void OnDestroy()
        {
            ClearNavMesh();
            ClearPath();
        }
    }
}
