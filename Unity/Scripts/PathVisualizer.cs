using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spatial.Unity
{
    /// <summary>
    /// Visualizes agent paths, waypoints, and target positions for debugging pathfinding.
    ///
    /// Each agent gets its own color (shared with EntityVisualizer).
    /// Shows:
    ///   - Path lines and waypoint markers in the agent's color
    ///   - Original requested target (colored marker + vertical pole)
    ///   - System-snapped target when it differs from the original (white marker)
    /// </summary>
    public class PathVisualizer : MonoBehaviour
    {
        [Header("Waypoint Visualization")]
        [Tooltip("Show waypoint markers along the path")]
        public bool showWaypoints = true;

        [Tooltip("Show path lines connecting waypoints")]
        public bool showPathLines = true;

        [Tooltip("Size of waypoint spheres")]
        [Range(0.1f, 2.0f)]
        public float waypointSize = 0.3f;

        [Tooltip("Width of path lines")]
        [Range(0.01f, 0.5f)]
        public float pathLineWidth = 0.1f;

        [Header("Target Visualization")]
        [Tooltip("Show original and snapped target markers")]
        public bool showTargets = true;

        [Tooltip("Height of the vertical pole drawn at each target")]
        [Range(0.5f, 5.0f)]
        public float targetPoleHeight = 2.5f;

        [Tooltip("Size of the target sphere marker")]
        [Range(0.1f, 1.0f)]
        public float targetMarkerSize = 0.4f;

        [Tooltip("Minimum distance (m) between original and snapped target to draw both markers")]
        [Range(0.05f, 1.0f)]
        public float snappedTargetDiffThreshold = 0.15f;

        [Header("Path Labels")]
        [Tooltip("Show entity ID labels")]
        public bool showEntityLabels = true;

        private Dictionary<int, PathVisualization> pathVisualizations = new Dictionary<int, PathVisualization>();

        private class PathVisualization
        {
            public GameObject pathRoot;
            public List<GameObject> waypointMarkers = new List<GameObject>();
            public LineRenderer pathLine;
            public TextMesh label;
            public GameObject originalTargetMarker;
            public GameObject snappedTargetMarker;
        }

        void Start()
        {
            var client = GetComponent<SimulationClient>();
            if (client != null)
            {
                client.OnStateReceived += UpdatePathVisualization;
            }
            else
            {
                Debug.LogError("[PathVisualizer] No SimulationClient found on this GameObject!");
            }
        }

        private void UpdatePathVisualization(SimulationState state)
        {
            if (!showWaypoints && !showPathLines && !showTargets)
            {
                ClearAllPaths();
                return;
            }

            HashSet<int> currentPathEntityIds = new HashSet<int>();

            if (state.AgentPaths != null)
            {
                foreach (var pathData in state.AgentPaths)
                {
                    currentPathEntityIds.Add(pathData.EntityId);

                    if (!pathVisualizations.ContainsKey(pathData.EntityId))
                        CreatePathVisualization(pathData);
                    else
                        UpdatePathVisualization(pathData);
                }
            }

            // Remove stale paths
            var toRemove = pathVisualizations.Keys.Where(id => !currentPathEntityIds.Contains(id)).ToList();
            foreach (var id in toRemove)
                DestroyPathVisualization(id);
        }

        private void CreatePathVisualization(PathData pathData)
        {
            Color agentColor = EntityVisualizer.GetAgentColor(pathData.EntityId);

            var viz = new PathVisualization();
            viz.pathRoot = new GameObject($"Path_Entity_{pathData.EntityId}");
            viz.pathRoot.transform.SetParent(transform);

            if (showWaypoints && pathData.Waypoints.Count > 0)
            {
                for (int i = 0; i < pathData.Waypoints.Count; i++)
                    viz.waypointMarkers.Add(CreateWaypointMarker(pathData.Waypoints[i], i, viz.pathRoot.transform, agentColor));
            }

            if (showPathLines && pathData.Waypoints.Count > 1)
                viz.pathLine = CreatePathLine(pathData.Waypoints, viz.pathRoot, agentColor);

            if (showEntityLabels)
                viz.label = CreatePathLabel(pathData.EntityId, pathData.Waypoints, viz.pathRoot.transform, agentColor);

            if (showTargets)
            {
                bool wasSnapped = pathData.SnappedTarget != null && pathData.OriginalTarget != null
                    && DistanceBetween(pathData.OriginalTarget, pathData.SnappedTarget) > snappedTargetDiffThreshold;

                if (pathData.OriginalTarget != null)
                    viz.originalTargetMarker = CreateTargetMarker(pathData.OriginalTarget, viz.pathRoot.transform, agentColor, "OriginalTarget", invalid: wasSnapped);

                if (wasSnapped)
                    viz.snappedTargetMarker = CreateSnappedTargetMarker(pathData.SnappedTarget, viz.pathRoot.transform, agentColor);
            }

            pathVisualizations[pathData.EntityId] = viz;
        }

        private void UpdatePathVisualization(PathData pathData)
        {
            var viz = pathVisualizations[pathData.EntityId];
            Color agentColor = EntityVisualizer.GetAgentColor(pathData.EntityId);

            // Refresh waypoint markers
            foreach (var marker in viz.waypointMarkers)
                if (marker != null) Destroy(marker);
            viz.waypointMarkers.Clear();

            if (showWaypoints && pathData.Waypoints.Count > 0)
            {
                for (int i = 0; i < pathData.Waypoints.Count; i++)
                    viz.waypointMarkers.Add(CreateWaypointMarker(pathData.Waypoints[i], i, viz.pathRoot.transform, agentColor));
            }

            // Refresh path line
            if (showPathLines && pathData.Waypoints.Count > 1)
            {
                if (viz.pathLine == null)
                    viz.pathLine = CreatePathLine(pathData.Waypoints, viz.pathRoot, agentColor);
                else
                    UpdatePathLine(viz.pathLine, pathData.Waypoints);
            }
            else if (viz.pathLine != null)
            {
                Destroy(viz.pathLine.gameObject);
                viz.pathLine = null;
            }

            // Refresh label
            if (showEntityLabels && viz.label != null && pathData.Waypoints.Count > 0)
            {
                var first = pathData.Waypoints[0];
                viz.label.transform.position = new Vector3(-first[0], first[1] + 2.0f, first[2]);
            }

            // Refresh target markers
            if (viz.originalTargetMarker != null) { Destroy(viz.originalTargetMarker); viz.originalTargetMarker = null; }
            if (viz.snappedTargetMarker != null)  { Destroy(viz.snappedTargetMarker);  viz.snappedTargetMarker  = null; }

            if (showTargets)
            {
                bool wasSnapped = pathData.SnappedTarget != null && pathData.OriginalTarget != null
                    && DistanceBetween(pathData.OriginalTarget, pathData.SnappedTarget) > snappedTargetDiffThreshold;

                if (pathData.OriginalTarget != null)
                    viz.originalTargetMarker = CreateTargetMarker(pathData.OriginalTarget, viz.pathRoot.transform, agentColor, "OriginalTarget", invalid: wasSnapped);

                if (wasSnapped)
                    viz.snappedTargetMarker = CreateSnappedTargetMarker(pathData.SnappedTarget, viz.pathRoot.transform, agentColor);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a target marker: a vertical pole topped with a sphere.
        /// Original target uses the agent's color; snapped target uses white.
        /// </summary>
        /// <summary>
        /// Creates a target marker beacon (vertical pole + sphere tip + ring).
        /// Valid targets: white pole + vivid agent-color sphere.
        /// Invalid targets (off-mesh, was snapped): red pole + red sphere + X cross bars.
        /// </summary>
        private GameObject CreateTargetMarker(float[] pos, Transform parent, Color agentColor, string name, bool invalid = false)
        {
            Vector3 worldPos = new Vector3(-pos[0], pos[1], pos[2]);

            var root = new GameObject(name);
            root.transform.SetParent(parent);
            root.transform.position = worldPos;

            Color poleColor   = invalid ? new Color(1f, 0.25f, 0.1f) : Color.white;
            Color sphereColor = invalid ? new Color(1f, 0.25f, 0.1f) : VividColor(agentColor);

            // Vertical pole
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform);
            pole.transform.localPosition = new Vector3(0f, targetPoleHeight * 0.5f, 0f);
            pole.transform.localScale = new Vector3(0.08f, targetPoleHeight * 0.5f, 0.08f);
            Destroy(pole.GetComponent<Collider>());
            SetRendererColor(pole, poleColor);

            // Sphere tip
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Marker";
            sphere.transform.SetParent(root.transform);
            sphere.transform.localPosition = new Vector3(0f, targetPoleHeight, 0f);
            sphere.transform.localScale = Vector3.one * (targetMarkerSize * 1.4f);
            Destroy(sphere.GetComponent<Collider>());
            SetRendererColor(sphere, sphereColor);

            // Ring disc
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(root.transform);
            ring.transform.localPosition = new Vector3(0f, targetPoleHeight - targetMarkerSize * 0.4f, 0f);
            ring.transform.localScale = new Vector3(targetMarkerSize * 1.8f, 0.04f, targetMarkerSize * 1.8f);
            Destroy(ring.GetComponent<Collider>());
            SetRendererColor(ring, sphereColor);

            // X cross bars on invalid targets — two thin rotated cubes
            if (invalid)
            {
                float crossSize = targetMarkerSize * 1.6f;
                for (int i = 0; i < 2; i++)
                {
                    var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bar.name = $"CrossBar{i}";
                    bar.transform.SetParent(root.transform);
                    bar.transform.localPosition = new Vector3(0f, targetPoleHeight, 0f);
                    bar.transform.localScale = new Vector3(crossSize, 0.06f, 0.06f);
                    bar.transform.localRotation = Quaternion.Euler(0f, 45f + i * 90f, 0f);
                    Destroy(bar.GetComponent<Collider>());
                    SetRendererColor(bar, Color.white);
                }
            }

            return root;
        }

        /// <summary>
        /// Creates the snapped-target marker: white pole + vivid agent-color diamond
        /// (rotated cube) to distinguish it from the original target sphere.
        /// </summary>
        private GameObject CreateSnappedTargetMarker(float[] pos, Transform parent, Color agentColor)
        {
            Vector3 worldPos = new Vector3(-pos[0], pos[1], pos[2]);

            var root = new GameObject("SnappedTarget");
            root.transform.SetParent(parent);
            root.transform.position = worldPos;

            // White pole — same as a valid original, so it reads as "this is where I'm actually going"
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform);
            pole.transform.localPosition = new Vector3(0f, targetPoleHeight * 0.5f, 0f);
            pole.transform.localScale = new Vector3(0.08f, targetPoleHeight * 0.5f, 0.08f);
            Destroy(pole.GetComponent<Collider>());
            SetRendererColor(pole, Color.white);

            // Diamond (rotated cube) in vivid agent color — visually distinct from the sphere on the original
            var diamond = GameObject.CreatePrimitive(PrimitiveType.Cube);
            diamond.name = "Diamond";
            diamond.transform.SetParent(root.transform);
            diamond.transform.localPosition = new Vector3(0f, targetPoleHeight, 0f);
            diamond.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            float ds = targetMarkerSize * 1.2f;
            diamond.transform.localScale = new Vector3(ds, ds, ds);
            Destroy(diamond.GetComponent<Collider>());
            SetRendererColor(diamond, VividColor(agentColor));

            return root;
        }

        private static Color VividColor(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, 1.0f, 1.0f);
        }

        private GameObject CreateWaypointMarker(float[] waypoint, int index, Transform parent, Color color)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Waypoint_{index}";
            marker.transform.SetParent(parent);
            marker.transform.position = new Vector3(-waypoint[0], waypoint[1], waypoint[2]);
            marker.transform.localScale = Vector3.one * waypointSize;
            Destroy(marker.GetComponent<Collider>());
            SetRendererColor(marker, color);
            return marker;
        }

        private LineRenderer CreatePathLine(List<float[]> waypoints, GameObject parent, Color color)
        {
            var lineObj = new GameObject("PathLine");
            lineObj.transform.SetParent(parent.transform);

            var lr = lineObj.AddComponent<LineRenderer>();
            lr.startWidth = pathLineWidth;
            lr.endWidth = pathLineWidth;
            lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lr.startColor = color;
            lr.endColor = color;

            UpdatePathLine(lr, waypoints);
            return lr;
        }

        private void UpdatePathLine(LineRenderer lr, List<float[]> waypoints)
        {
            lr.positionCount = waypoints.Count;
            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                lr.SetPosition(i, new Vector3(-wp[0], wp[1], wp[2]));
            }
        }

        private TextMesh CreatePathLabel(int entityId, List<float[]> waypoints, Transform parent, Color color)
        {
            if (waypoints.Count == 0) return null;

            var labelObj = new GameObject($"Label_Entity_{entityId}");
            labelObj.transform.SetParent(parent);
            var first = waypoints[0];
            labelObj.transform.position = new Vector3(-first[0], first[1] + 2.0f, first[2]);

            var tm = labelObj.AddComponent<TextMesh>();
            tm.text = $"Agent {entityId}";
            tm.fontSize = 20;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.1f;
            return tm;
        }

        private void SetRendererColor(GameObject obj, Color color)
        {
            var rend = obj.GetComponent<Renderer>();
            if (rend == null) return;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            rend.material = mat;
        }

        private static float DistanceBetween(float[] a, float[] b)
        {
            float dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void DestroyPathVisualization(int entityId)
        {
            if (pathVisualizations.TryGetValue(entityId, out var viz))
            {
                if (viz.pathRoot != null) Destroy(viz.pathRoot);
                pathVisualizations.Remove(entityId);
            }
        }

        private void ClearAllPaths()
        {
            foreach (var viz in pathVisualizations.Values)
                if (viz.pathRoot != null) Destroy(viz.pathRoot);
            pathVisualizations.Clear();
        }

        void OnDestroy()
        {
            ClearAllPaths();
        }
    }
}
