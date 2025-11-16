using UnityEngine;
using System.Collections.Generic;
using System.Linq;
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
    public bool generate3DTrack = false;
    public float noiseScale = 15f;
    [Range(5f, 50f)] public float roadWidth = 25f;
    public float uvTiling = 10f;

    [Header("Debug Info")]
    [SerializeField] private int _seed;
    public int seed { get { return _seed; } private set { _seed = value; } }

    private float[,] heightMap;
    private List<Vector3> _trackPoints3D;
    private List<Vector2> _refinedPoints;

    void Start()
    {
        if (generateOnStart && Application.isPlaying)
            GenerateTrack();
    }

    public void GenerateTrack()
    {
        ClearTrack();
        seed = (int)(System.DateTime.Now.Ticks % int.MaxValue);
        Random.InitState(seed);
        _trackPoints3D = CreateRacetrack(generate3DTrack);
        if (_trackPoints3D != null && _trackPoints3D.Count > 2)
        {
            GenerateRoadMesh(_trackPoints3D);
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

        _trackPoints3D = null;
        _refinedPoints = null;

        Debug.Log("Track cleared.");
    }

    public List<Vector3> CreateRacetrack(bool is3D)
    {
        heightMap = GenerateNoise(2);

        List<Vector2> basePoints = null;
        int attempts = 0;
        const int maxAttempts = 100000;

        do
        {
            if (attempts > 0)
            {
                seed = Random.Range(0, int.MaxValue);
                Random.InitState(seed);
                Debug.Log($"Attempting new track with seed: {seed} (Attempt {attempts + 1})");
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

        if (is3D)
        {
            var points3D = AddHeightToTrack(_refinedPoints);
            points3D = SmoothTrackElevation(points3D, 3, 0.7f);
            return points3D;
        }

        return _refinedPoints.Select(p => new Vector3(p.x, 0, p.y)).ToList();
    }

    #region Track Shape Generation
    private bool ValidateTrackAnglesAndDistances(List<Vector2> points)
    {
        if (points.Count < 3) return false;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 prev = points[(i - 1 + points.Count) % points.Count];
            Vector2 curr = points[i];
            Vector2 next = points[(i + 1) % points.Count];

            if (Vector2.Distance(prev, curr) < roadWidth * 0.5f || Vector2.Distance(curr, next) < roadWidth * 0.5f)
                return false;

            float angle = Vector2.Angle(curr - prev, next - curr);
            if (angle < minTurnAngle || angle > maxTurnAngle)
                return false;

            for (int j = i + 2; j < points.Count + i - 1; j++)
            {
                int index = j % points.Count;
                if (index == i || index == (i - 1 + points.Count) % points.Count) continue;
                if (Vector2.Distance(curr, points[index]) < roadWidth * 1.2f)
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
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float disp = Random.Range(roadWidth * 1.5f, Mathf.Sqrt(maxEdgeLengthSq) * 0.4f);
                if (Random.value < 0.5f) disp = -disp;

                Vector2 newPoint = mid + normal * disp;

                if (IsHairpin(p1, newPoint, p2)) continue;
                if (!IsPointTooCloseToTrack(refined, newPoint, roadWidth * 1.8f))
                {
                    refined.Insert(longestEdgeIndex + 1, newPoint);
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

    #region Mesh & Utilities
    private float[,] GenerateNoise(int octaves)
    {
        int xpix = Mathf.Max(1, (int)(xBounds.y - xBounds.x));
        int ypix = Mathf.Max(1, (int)(yBounds.y - yBounds.x));
        var noiseMap = new float[xpix, ypix];
        float seedX = Random.Range(0f, 1000f);
        float seedY = Random.Range(0f, 1000f);

        for (int y = 0; y < ypix; y++)
            for (int x = 0; x < xpix; x++)
            {
                float sampleX = (float)x / xpix * octaves + seedX;
                float sampleY = (float)y / ypix * octaves + seedY;
                noiseMap[x, y] = Mathf.PerlinNoise(sampleX, sampleY) * noiseScale;
            }

        return noiseMap;
    }

    private List<Vector3> AddHeightToTrack(List<Vector2> points2D)
    {
        var points3D = new List<Vector3>();
        int xpix = Mathf.Max(1, (int)(xBounds.y - xBounds.x));
        int ypix = Mathf.Max(1, (int)(yBounds.y - yBounds.x));

        foreach (var p in points2D)
        {
            int xi = Mathf.FloorToInt(Mathf.InverseLerp(xBounds.x, xBounds.y, p.x) * xpix);
            int yi = Mathf.FloorToInt(Mathf.InverseLerp(yBounds.x, yBounds.y, p.y) * ypix);

            xi = Mathf.Clamp(xi, 0, xpix - 1);
            yi = Mathf.Clamp(yi, 0, ypix - 1);

            float h = heightMap[xi, yi];
            points3D.Add(new Vector3(p.x, h, p.y));
        }
        return points3D;
    }

    private List<Vector3> SmoothTrackElevation(List<Vector3> points, int iterations, float blend)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            var smoothed = new List<Vector3>(points);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 prev = points[(i - 1 + points.Count) % points.Count];
                Vector3 curr = points[i];
                Vector3 next = points[(i + 1) % points.Count];
                float y = (prev.y + curr.y + next.y) / 3f;
                smoothed[i] = new Vector3(curr.x, Mathf.Lerp(curr.y, y, blend), curr.z);
            }
            points = smoothed;
        }
        return points;
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
        mesh.RecalculateTangents();

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
                int nextIndex = (i + 1) % _refinedPoints.Count;

                Vector3 p1, p2;
                if (_trackPoints3D != null && _trackPoints3D.Count > i && _trackPoints3D.Count > nextIndex)
                {
                    p1 = _trackPoints3D[i];
                    p2 = _trackPoints3D[nextIndex];
                }
                else
                {
                    p1 = new Vector3(_refinedPoints[i].x, 0, _refinedPoints[i].y);
                    p2 = new Vector3(_refinedPoints[nextIndex].x, 0, _refinedPoints[nextIndex].y);
                }

                Gizmos.DrawSphere(p1, 1f);
                Gizmos.DrawLine(p1, p2);
            }
        }
    }
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
    }
}
#endif
#endregion