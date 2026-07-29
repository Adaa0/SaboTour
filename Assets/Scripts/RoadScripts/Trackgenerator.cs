using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TrackGenerator : MonoBehaviour
{
    [Header("Track Shape Parameters")]
    public int numPoints = 20;
    public Vector2 xBounds = new Vector2(-150, 150);
    public Vector2 yBounds = new Vector2(-150, 150);
    public bool generateOnStart = false;

    [Header("F1-Style Generation Settings")]
    [Range(1, 10)] public int trackComplexity = 6;
    public int minCorners = 5;
    [Range(0.1f, 0.4f)] public float cornerSmoothness = 0.3f;
    public int cornerSegments = 20;
    [Range(0, 180)] public float minTurnAngle = 30f;
    [Range(0, 180)] public float maxTurnAngle = 150f;

    [Header("Mesh Settings")]
    [Range(5f, 50f)] public float roadWidth = 25f;
    public float uvTiling = 10f;

    [Header("Checkpoint Settings")]
    public GameObject checkpointPrefab;
    [Range(3, 30)] public int checkpointsPerLap = 10;
    public bool showCheckpointsInEditor = true;

    [HideInInspector] public UnityEvent onTrackGenerated;

    [Header("Debug Info")]
    [SerializeField] private int _seed;
    public int seed { get { return _seed; } private set { _seed = value; } }

    private List<Vector3> _trackPoints;
    private List<Vector2> _refinedPoints;
    private List<GameObject> _checkpoints = new List<GameObject>();

    void Start()
    {
        if (generateOnStart && Application.isPlaying)
            GenerateTrack();
    }

    /// <summary>
    /// Rastgele (DateTime tabanlı) bir seed ile pist üretir.
    /// EDİTÖR BUTONU ve tek oyunculu test için kullanılır.
    /// MULTIPLAYER'DA BUNU KULLANMA — TrackSeedSync.cs bunun yerine
    /// GenerateTrackWithSeed(int) çağıracak, böylece host ve tüm client'lar
    /// AYNI seed ile AYNI pisti üretir.
    /// </summary>
    public void GenerateTrack()
    {
        int randomSeed = (int)(System.DateTime.Now.Ticks % int.MaxValue);
        GenerateTrackWithSeed(randomSeed);
    }

    /// <summary>
    /// Belirli bir seed ile deterministik pist üretir. Aynı seed her zaman
    /// aynı pisti üretir (Unity'nin Random sınıfı seed'e göre deterministik
    /// çalışır) — bu yüzden host'un seed'ini client'lara göndermek yeterli,
    /// tüm pist verisini network üzerinden göndermeye gerek yok.
    /// </summary>
    public void GenerateTrackWithSeed(int seedValue)
    {
        ClearTrack();
        _seed = seedValue;
        Random.InitState(_seed);

        _trackPoints = CreateRacetrack();

        if (_trackPoints != null && _trackPoints.Count > 2)
        {
            GenerateRoadMesh(_trackPoints);
            GenerateCheckpoints(_trackPoints);
            onTrackGenerated.Invoke();
            Debug.Log($"Track generated with seed: {_seed}. Checkpoints: {checkpointsPerLap}");
        }
        else
        {
            Debug.LogWarning("Failed to generate valid track points");
        }
    }

    public void ClearTrack()
    {
        var mf = GetComponent<MeshFilter>();
        var mc = GetComponent<MeshCollider>();

        if (mf != null && mf.sharedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(mf.sharedMesh);
            else
                DestroyImmediate(mf.sharedMesh);
            mf.sharedMesh = null;
        }
        if (mc != null) mc.sharedMesh = null;

        foreach (var cp in _checkpoints)
        {
            if (Application.isPlaying) Destroy(cp);
            else DestroyImmediate(cp);
        }
        _checkpoints.Clear();

        _trackPoints = null;
        _refinedPoints = null;

        Debug.Log("Track cleared.");
    }

    public List<Vector3> CreateRacetrack()
    {
        List<Vector2> basePoints = null;
        int attempts = 0;
        const int maxAttempts = 100000;

        do
        {
            if (attempts > 0)
            {
                _seed = Random.Range(0, int.MaxValue);
                Random.InitState(_seed);
                Debug.Log($"Attempting new track with seed: {_seed} (Attempt {attempts + 1})");
            }

            var randomPoints = new List<Vector2>();
            for (int i = 0; i < numPoints; i++)
            {
                randomPoints.Add(new Vector2(
                    Random.Range(xBounds.x, xBounds.y),
                    Random.Range(yBounds.x, yBounds.y)
                ));
            }

            basePoints = GetConvexHull(randomPoints);
            if (basePoints.Count < 3)
            {
                attempts++;
                continue;
            }

            basePoints = RefineTrackShape(basePoints, trackComplexity + Mathf.Max(0, minCorners - basePoints.Count));
            if (basePoints.Count < 3)
            {
                attempts++;
                continue;
            }

            attempts++;
            if (attempts >= maxAttempts)
            {
                Debug.LogError("Could not generate a valid track after " + maxAttempts + " attempts. Check parameters.");
                return new List<Vector3>();
            }

        } while (!ValidateTrackAnglesAndDistances(basePoints));

        _refinedPoints = CurveCorners(basePoints);
        return _refinedPoints.Select(p => new Vector3(p.x, 0, p.y)).ToList();
    }

    #region Track Shape Generation
    private bool DoSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);

        if (Mathf.Abs(d) < 0.0001f) return false;

        float t = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / d;
        float u = ((p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x)) / d;

        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }

    private float DistancePointToSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLength = line.magnitude;

        if (lineLength < 0.0001f)
            return Vector2.Distance(point, lineStart);

        float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (lineLength * lineLength));
        Vector2 projection = lineStart + t * line;

        return Vector2.Distance(point, projection);
    }

    private bool ValidateTrackAnglesAndDistances(List<Vector2> points)
    {
        if (points.Count < 3) return false;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 prev = points[(i - 1 + points.Count) % points.Count];
            Vector2 curr = points[i];
            Vector2 next = points[(i + 1) % points.Count];

            if (Vector2.Distance(prev, curr) < roadWidth * 0.8f ||
                Vector2.Distance(curr, next) < roadWidth * 0.8f)
                return false;

            float angle = Vector2.Angle(curr - prev, next - curr);
            if (angle < minTurnAngle || angle > maxTurnAngle)
                return false;

            for (int j = i + 2; j < points.Count + i - 1; j++)
            {
                int index = j % points.Count;
                if (index == i || index == (i - 1 + points.Count) % points.Count)
                    continue;

                if (Vector2.Distance(curr, points[index]) < roadWidth * 1.5f)
                    return false;
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % points.Count];

            for (int j = i + 2; j < points.Count; j++)
            {
                if (i == 0 && j == points.Count - 1) continue;

                Vector2 p3 = points[j];
                Vector2 p4 = points[(j + 1) % points.Count];

                if (DoSegmentsIntersect(p1, p2, p3, p4))
                {
                    return false;
                }
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 segStart = points[i];
            Vector2 segEnd = points[(i + 1) % points.Count];

            for (int j = 0; j < points.Count; j++)
            {
                if (j == i || j == (i + 1) % points.Count ||
                    j == (i - 1 + points.Count) % points.Count)
                    continue;

                float dist = DistancePointToSegment(points[j], segStart, segEnd);
                if (dist < roadWidth * 1.2f)
                    return false;
            }
        }

        return true;
    }

    private List<Vector2> RefineTrackShape(List<Vector2> points, int iterations)
    {
        if (points.Count < 3) return points;

        var refined = new List<Vector2>(points);

        for (int i = 0; i < iterations; i++)
        {
            int longestEdgeIndex = -1;
            float maxEdgeLengthSq = 0f;

            for (int j = 0; j < refined.Count; j++)
            {
                float distSq = (refined[j] - refined[(j + 1) % refined.Count]).sqrMagnitude;
                if (distSq > maxEdgeLengthSq)
                {
                    maxEdgeLengthSq = distSq;
                    longestEdgeIndex = j;
                }
            }

            if (longestEdgeIndex == -1) continue;

            Vector2 p1 = refined[longestEdgeIndex];
            Vector2 p2 = refined[(longestEdgeIndex + 1) % refined.Count];

            if (Vector2.Distance(p1, p2) < roadWidth * 2f) continue;

            Vector2 mid = (p1 + p2) / 2f;
            Vector2 dir = (p2 - p1).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x);

            bool validPointFound = false;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                float disp = Random.Range(roadWidth * 1.5f, Mathf.Sqrt(maxEdgeLengthSq) * 0.4f);
                if (Random.value < 0.5f) disp = -disp;

                Vector2 newPoint = mid + normal * disp;

                if (IsHairpin(p1, newPoint, p2)) continue;
                if (IsPointTooCloseToTrack(refined, newPoint, roadWidth * 2.0f)) continue;

                var testRefined = new List<Vector2>(refined);
                testRefined.Insert(longestEdgeIndex + 1, newPoint);

                if (ValidateTrackAnglesAndDistances(testRefined))
                {
                    refined = testRefined;
                    validPointFound = true;
                    break;
                }
            }

            if (!validPointFound) break;
        }

        return refined;
    }

    private bool IsPointTooCloseToTrack(List<Vector2> points, Vector2 newPoint, float minDistance)
    {
        foreach (var point in points)
            if (Vector2.Distance(point, newPoint) < minDistance)
                return true;
        return false;
    }

    private bool IsHairpin(Vector2 prev, Vector2 curr, Vector2 next)
    {
        Vector2 v1 = (curr - prev).normalized;
        Vector2 v2 = (next - curr).normalized;
        float angle = Vector2.Angle(v1, v2);
        return angle < minTurnAngle || angle > maxTurnAngle;
    }

    private List<Vector2> CurveCorners(List<Vector2> points)
    {
        if (points == null || points.Count < 3) return new List<Vector2>(points);

        var finalPath = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 prev = points[(i - 1 + points.Count) % points.Count];
            Vector2 curr = points[i];
            Vector2 next = points[(i + 1) % points.Count];

            Vector2 start = Vector2.Lerp(curr, prev, cornerSmoothness);
            Vector2 end = Vector2.Lerp(curr, next, cornerSmoothness);

            for (int j = 0; j <= cornerSegments; j++)
            {
                float t = (float)j / cornerSegments;
                finalPath.Add(QuadraticBezier(start, curr, end, t));
            }
        }
        return finalPath;
    }

    private Vector2 QuadraticBezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float u = 1 - t;
        return u * u * p0 + 2 * u * t * p1 + t * t * p2;
    }
    #endregion

    #region Checkpoint & Mesh Generation
    public List<GameObject> GetCheckpoints() => _checkpoints;

    private void GenerateCheckpoints(List<Vector3> trackPoints)
    {
        if (checkpointPrefab == null || checkpointsPerLap <= 0) return;

        foreach (var cp in _checkpoints)
        {
            if (Application.isPlaying) Destroy(cp);
            else DestroyImmediate(cp);
        }
        _checkpoints.Clear();

        int interval = Mathf.Max(1, trackPoints.Count / checkpointsPerLap);

        for (int i = 0; i < checkpointsPerLap; i++)
        {
            int pointIndex = i * interval;
            if (pointIndex >= trackPoints.Count) pointIndex = trackPoints.Count - 1;

            Vector3 pos = trackPoints[pointIndex];
            Vector3 nextPoint = trackPoints[(pointIndex + 1) % trackPoints.Count];
            Vector3 forward = (nextPoint - pos).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            GameObject cpObject = Instantiate(checkpointPrefab, pos + Vector3.up * 5f, rotation, transform);
            cpObject.name = $"Checkpoint_{i}";

            Checkpoint cp = cpObject.GetComponent<Checkpoint>();
            if (cp != null)
            {
                cp.checkpointIndex = i;
                cp.isFinishLine = (i == 0);
            }

            _checkpoints.Add(cpObject);
        }
    }

    private void GenerateRoadMesh(List<Vector3> points)
    {
        if (points == null || points.Count < 3) return;

        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        var mc = GetComponent<MeshCollider>();

        if (mf.sharedMesh != null)
        {
            if (Application.isPlaying) Destroy(mf.sharedMesh);
            else DestroyImmediate(mf.sharedMesh);
        }

        var mesh = new Mesh { name = "Procedural Road Mesh" };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();

        float cumulativeDistance = 0f;
        var cumulativeDistances = new List<float>();

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) cumulativeDistance += Vector3.Distance(points[i - 1], points[i]);
            cumulativeDistances.Add(cumulativeDistance);

            Vector3 curr = points[i];
            Vector3 next = points[(i + 1) % points.Count];
            Vector3 dir = (next - curr).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;

            vertices.Add(curr - right * (roadWidth / 2f));
            vertices.Add(curr + right * (roadWidth / 2f));

            float v = cumulativeDistances[i] / uvTiling;
            uvs.Add(new Vector2(0, v));
            uvs.Add(new Vector2(1, v));
        }

        for (int i = 0; i < points.Count; i++)
        {
            int baseIndex = i * 2;
            int nextIndex = ((i + 1) % points.Count) * 2;

            triangles.Add(baseIndex);
            triangles.Add(nextIndex);
            triangles.Add(baseIndex + 1);

            triangles.Add(nextIndex);
            triangles.Add(nextIndex + 1);
            triangles.Add(baseIndex + 1);
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.2f, 0.2f, 0.2f),
                enableInstancing = true
            };
    }

    private List<Vector2> GetConvexHull(List<Vector2> points)
    {
        if (points.Count < 3) return points;

        var uniquePoints = points.Distinct().ToList();
        if (uniquePoints.Count < 3) return uniquePoints;

        Vector2 start = uniquePoints.OrderBy(p => p.y).ThenBy(p => p.x).First();
        var sorted = uniquePoints.Where(p => p != start)
                               .OrderBy(p => Mathf.Atan2(p.y - start.y, p.x - start.x))
                               .ThenBy(p => Vector2.Distance(start, p)).ToList();

        var stack = new Stack<Vector2>();
        stack.Push(start);
        stack.Push(sorted[0]);

        for (int i = 1; i < sorted.Count; i++)
        {
            Vector2 top = stack.Pop();
            while (stack.Count > 0 && CrossProduct(stack.Peek(), top, sorted[i]) <= 0)
                top = stack.Pop();
            stack.Push(top);
            stack.Push(sorted[i]);
        }

        return stack.Reverse().ToList();
    }

    private float CrossProduct(Vector2 p1, Vector2 p2, Vector2 p3) =>
        (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);

    private void OnDrawGizmos()
    {
        if (_refinedPoints != null && _refinedPoints.Count > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < _refinedPoints.Count; i++)
            {
                Vector3 p1 = new Vector3(_refinedPoints[i].x, 0, _refinedPoints[i].y);
                Vector3 p2 = new Vector3(_refinedPoints[(i + 1) % _refinedPoints.Count].x, 0, _refinedPoints[(i + 1) % _refinedPoints.Count].y);
                Gizmos.DrawSphere(p1, 1f);
                Gizmos.DrawLine(p1, p2);
            }
        }

        if (showCheckpointsInEditor && !Application.isPlaying)
        {
            foreach (var cp in _checkpoints)
            {
                if (cp == null) continue;
                Checkpoint checkpoint = cp.GetComponent<Checkpoint>();
                Gizmos.color = checkpoint.isFinishLine ? Color.red : Color.green;
                Gizmos.DrawCube(cp.transform.position, Vector3.one * 5f);
            }
        }
    }
    #endregion
}

#if UNITY_EDITOR
[CustomEditor(typeof(TrackGenerator))]
public class TrackGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TrackGenerator tg = (TrackGenerator)target;

        GUILayout.Space(10);
        GUILayout.Label("Track Controls", EditorStyles.boldLabel);

        GUI.enabled = false;
        EditorGUILayout.IntField("Current Seed", tg.seed);
        GUI.enabled = true;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("checkpointPrefab"));
        serializedObject.ApplyModifiedProperties();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate Track"))
        {
            tg.GenerateTrack();
            EditorUtility.SetDirty(tg);
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Clear Track"))
        {
            tg.ClearTrack();
            EditorUtility.SetDirty(tg);
            SceneView.RepaintAll();
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "1. Create a Checkpoint prefab with Trigger Collider and Checkpoint.cs\n" +
            "2. Assign it above\n" +
            "3. Ensure cars have 'Player' tag and PlayerRaceController.cs",
            MessageType.Info
        );
    }
}
#endif