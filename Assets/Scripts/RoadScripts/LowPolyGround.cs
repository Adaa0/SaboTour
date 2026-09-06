using UnityEngine;

// Prosedürel low-poly zemin mesh'i üretiyor. cellSize büyütülerek üçgen
// sayısı düşürüldü (CLAUDE.md: cellSize 1 → 30, 4.5M → 5.000 üçgen), detay
// artık üçgenden değil dokudan geliyor (useTexture + textureTileMeters).
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LowPolyGround : MonoBehaviour
{
    [Header("Boyut")]
    public float areaSize = 1500f;
    public float cellSize = 30f;

    [Header("Organik görünüm")]
    [Range(0f, 1f)] public float cornerJitter = 0.3f;
    public float maxDepth = 0.08f;
    public float patchScale = 0.12f;
    public bool flatShading = false;
    [Range(0f, 1f)] public float edgeNoise = 0.4f;

    [Header("Doku")]
    public bool useTexture = true;
    public float textureTileMeters = 80f;

    [Header("Materyaller")]
    public Material[] materials;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;

    void Awake()
    {
        Generate();
    }

    [ContextMenu("Zemini Yeniden Üret")]
    public void Generate()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        int cells = Mathf.Max(1, Mathf.RoundToInt(areaSize / cellSize));
        int verts = cells + 1;
        float half = areaSize * 0.5f;

        // Perlin ofsetleri: aynı seed her defasında aynı deseni versin diye
        // sabit, ama tekil bir zemin objesi için tekrar sorunu yok
        // (bu bir arka plan zemini, döşenen bir doku değil).
        float noiseOffsetX = 1000f;
        float noiseOffsetZ = 1000f;

        var positions = new Vector3[verts * verts];
        var uvs = new Vector2[verts * verts];

        for (int z = 0; z < verts; z++)
        {
            for (int x = 0; x < verts; x++)
            {
                float px = -half + x * cellSize;
                float pz = -half + z * cellSize;

                bool isEdge = (x == 0 || z == 0 || x == verts - 1 || z == verts - 1);

                // Köşe jitter'ı — iç noktalarda XZ'yi hafifçe kaydırıp
                // düzgün ızgara hissini kırıyor. Kenarlarda jitter yok,
                // yoksa komşu zemin parçalarıyla (varsa) dikiş açılırdı.
                if (!isEdge && cornerJitter > 0f)
                {
                    float jx = (Mathf.PerlinNoise(x * 0.37f + noiseOffsetX, z * 0.61f) - 0.5f) * 2f;
                    float jz = (Mathf.PerlinNoise(x * 0.61f, z * 0.37f + noiseOffsetZ) - 0.5f) * 2f;
                    px += jx * cornerJitter * cellSize * 0.5f;
                    pz += jz * cornerJitter * cellSize * 0.5f;
                }

                // Yükseklik — geniş dalgalar (patchScale) + kenarlarda ince
                // pürüz (edgeNoise), ikisi çarpılıp maxDepth ile ölçekleniyor.
                float heightNoise = Mathf.PerlinNoise(px * patchScale + noiseOffsetX, pz * patchScale + noiseOffsetZ);
                float fineNoise = Mathf.PerlinNoise(px * patchScale * 4f, pz * patchScale * 4f);
                float py = (heightNoise - 0.5f) * maxDepth + (fineNoise - 0.5f) * maxDepth * edgeNoise;

                positions[z * verts + x] = new Vector3(px, py, pz);

                uvs[z * verts + x] = useTexture
                    ? new Vector2(px / textureTileMeters, pz / textureTileMeters)
                    : new Vector2((float)x / cells, (float)z / cells);
            }
        }

        var triangles = new int[cells * cells * 6];
        int ti = 0;
        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                int a = z * verts + x;
                int b = z * verts + x + 1;
                int c = (z + 1) * verts + x;
                int d = (z + 1) * verts + x + 1;

                triangles[ti++] = a; triangles[ti++] = c; triangles[ti++] = b;
                triangles[ti++] = b; triangles[ti++] = c; triangles[ti++] = d;
            }
        }

        var mesh = new Mesh();
        mesh.name = "LowPolyGround_Generated";
        mesh.indexFormat = positions.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = positions;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        if (flatShading)
        {
            mesh = MakeFlatShaded(mesh);
        }
        else
        {
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        meshFilter.sharedMesh = mesh;

        if (materials != null && materials.Length > 0)
            meshRenderer.sharedMaterials = materials;
    }

    // Her üçgen kendi vertex kopyasına sahip olsun diye ayırıyor (paylaşılan
    // vertex'lerde normal ortalaması alınamaz), sonra düz normal hesaplıyor.
    Mesh MakeFlatShaded(Mesh source)
    {
        var srcVerts = source.vertices;
        var srcUv = source.uv;
        var srcTris = source.triangles;

        var newVerts = new Vector3[srcTris.Length];
        var newUv = new Vector2[srcTris.Length];
        var newTris = new int[srcTris.Length];

        for (int i = 0; i < srcTris.Length; i++)
        {
            newVerts[i] = srcVerts[srcTris[i]];
            newUv[i] = srcUv[srcTris[i]];
            newTris[i] = i;
        }

        var flat = new Mesh();
        flat.name = source.name;
        flat.indexFormat = newVerts.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        flat.vertices = newVerts;
        flat.uv = newUv;
        flat.triangles = newTris;
        flat.RecalculateNormals();
        return flat;
    }
}
