using System.Collections.Generic;
using UnityEngine;

public enum BlockingKingUnitMeshKind
{
    PlayerPawn,
    GoEnemy,
    GrenadierEnemy,
    CrossbowEnemy,
    ArtilleryEnemy,
    MahjongEnemy
}

public static class BlockingKingUnitMeshFactory
{
    public const string ResourceFolder = "GeneratedMeshes/Units";

    private static readonly Dictionary<PrimitiveType, Mesh> PrimitiveCache = new();

    public static string GetAssetName(BlockingKingUnitMeshKind kind) => kind switch
    {
        BlockingKingUnitMeshKind.PlayerPawn => "Player.Pawn.Mesh",
        BlockingKingUnitMeshKind.GoEnemy => "Go.Enemy.Mesh",
        BlockingKingUnitMeshKind.GrenadierEnemy => "Grenadier.Enemy.Mesh",
        BlockingKingUnitMeshKind.CrossbowEnemy => "Crossbow.Enemy.Mesh",
        BlockingKingUnitMeshKind.ArtilleryEnemy => "Artillery.Enemy.Mesh",
        BlockingKingUnitMeshKind.MahjongEnemy => "Mahjong.Enemy.Mesh",
        _ => kind.ToString()
    };

    public static Mesh LoadGeneratedMesh(BlockingKingUnitMeshKind kind)
    {
        return Resources.Load<Mesh>($"{ResourceFolder}/{GetAssetName(kind)}");
    }

    public static Mesh Create(BlockingKingUnitMeshKind kind)
    {
        Mesh mesh = kind switch
        {
            BlockingKingUnitMeshKind.PlayerPawn => CreatePlayerPawn(),
            BlockingKingUnitMeshKind.GoEnemy => CreateGo(),
            BlockingKingUnitMeshKind.GrenadierEnemy => CreateCannonStone(),
            BlockingKingUnitMeshKind.CrossbowEnemy => CreateCrossbow(),
            BlockingKingUnitMeshKind.ArtilleryEnemy => CreateCannonFire(),
            BlockingKingUnitMeshKind.MahjongEnemy => CreateMahjongTile(),
            _ => CreateGo()
        };

        mesh.name = GetAssetName(kind);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Mesh CreatePlayerPawn()
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        var rings = new[]
        {
            CreateOctRing(0.30f, 0.00f),
            CreateOctRing(0.30f, 0.08f),
            CreateOctRing(0.24f, 0.16f),
            CreateOctRing(0.18f, 0.22f),
            CreateOctRing(0.11f, 0.30f),
            CreateOctRing(0.11f, 0.54f)
        };

        AddPolygonCap(vertices, triangles, rings[0], false);
        for (int i = 0; i < rings.Length - 1; i++)
            AddRingQuads(vertices, triangles, rings[i], rings[i + 1]);
        AddPointFan(vertices, triangles, rings[^1], new Vector3(0f, 0.60f, 0f));
        AddOctahedron(vertices, triangles, new Vector3(0f, 0.60f, 0f), 0.20f, 0.20f);
        AddFlatRing(vertices, triangles, 12, 0.17f, 0.24f, 1.07f, 0.025f);

        var uvs = new Vector2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
            uvs[i] = new Vector2(vertices[i].x + 0.5f, vertices[i].z + 0.5f);

        var mesh = new Mesh
        {
            name = "Player.Pawn.Mesh",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateGo()
    {
        return CreateGoStoneMesh("Go.Enemy.Mesh", 0.42f, 0.24f, 40, 8);
    }

    private static Mesh CreateMahjongTile()
    {
        return CreateMahjongTileBodyMesh("Mahjong.Enemy.Mesh", 0.5167f, 0.7f, 0.35f, 0.04f, 2f / 3f);
    }

    private static Mesh CreateCannonStone()
    {
        return CreateRoundTileBodyMesh("Grenadier.Enemy.Mesh", 0.34f, 0.38f, 0.35f, 0.04f, 0.5f, 32);
    }

    private static Mesh CreateCannonFire()
    {
        return CreateRoundTileBodyMesh("Artillery.Enemy.Mesh", 0.38f, 0.43f, 0.35f, 0.04f, 0.5f, 32);
    }

    private static Mesh CreateCrossbow()
    {
        var parts = new List<CombineInstance>();
        AddMesh(parts, CreateWedgeMesh(), Matrix4x4.TRS(new Vector3(0f, 0.22f, 0f), Quaternion.identity, new Vector3(0.74f, 0.44f, 0.74f)));
        AddCube(parts, new Vector3(0f, 0.52f, 0f), new Vector3(0.42f, 0.48f, 0.42f));
        AddCube(parts, new Vector3(0f, 0.78f, 0f), new Vector3(0.58f, 0.12f, 0.46f));
        AddCube(parts, new Vector3(-0.23f, 0.92f, 0f), new Vector3(0.12f, 0.20f, 0.42f));
        AddCube(parts, new Vector3(0.23f, 0.92f, 0f), new Vector3(0.12f, 0.20f, 0.42f));
        AddCube(parts, new Vector3(0f, 0.90f, -0.36f), new Vector3(0.10f, 0.10f, 0.44f));
        return Combine("Crossbow.Enemy.Mesh", parts);
    }


    private static void AddRaisedMark(List<CombineInstance> parts, float radius, bool fire)
    {
        AddCylinder(parts, new Vector3(0f, 0.48f, 0f), radius, 0.045f);
        AddCube(parts, new Vector3(0f, 0.53f, 0f), new Vector3(radius * 1.25f, 0.04f, 0.08f));
        AddCube(parts, new Vector3(0f, 0.535f, 0f), new Vector3(0.08f, 0.04f, radius * 1.25f));
        if (fire)
        {
            AddCube(parts, new Vector3(-0.16f, 0.54f, -0.14f), new Vector3(0.08f, 0.045f, 0.20f), Quaternion.Euler(0f, 35f, 0f));
            AddCube(parts, new Vector3(0.16f, 0.54f, 0.14f), new Vector3(0.08f, 0.045f, 0.20f), Quaternion.Euler(0f, 35f, 0f));
        }
        else
        {
            AddSphere(parts, new Vector3(-0.16f, 0.54f, 0.15f), 0.055f);
            AddSphere(parts, new Vector3(0.16f, 0.54f, -0.15f), 0.055f);
        }
    }

    private static void AddCube(List<CombineInstance> parts, Vector3 position, Vector3 scale)
    {
        AddCube(parts, position, scale, Quaternion.identity);
    }

    private static void AddCube(List<CombineInstance> parts, Vector3 position, Vector3 scale, Quaternion rotation)
    {
        AddPrimitive(parts, PrimitiveType.Cube, position, rotation, scale);
    }

    private static void AddCylinder(List<CombineInstance> parts, Vector3 position, float radius, float height)
    {
        AddCylinder(parts, position, radius, height, Quaternion.identity);
    }

    private static void AddCylinder(List<CombineInstance> parts, Vector3 position, float radius, float height, Quaternion rotation)
    {
        AddPrimitive(parts, PrimitiveType.Cylinder, position, rotation, new Vector3(radius * 2f, height * 0.5f, radius * 2f));
    }

    private static void AddSphere(List<CombineInstance> parts, Vector3 position, float radius)
    {
        AddSphere(parts, position, Vector3.one * (radius * 2f));
    }

    private static void AddSphere(List<CombineInstance> parts, Vector3 position, Vector3 scale)
    {
        AddPrimitive(parts, PrimitiveType.Sphere, position, Quaternion.identity, scale);
    }

    private static void AddPrimitive(List<CombineInstance> parts, PrimitiveType primitive, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        AddMesh(parts, GetPrimitiveMesh(primitive), Matrix4x4.TRS(position, rotation, scale));
    }

    private static void AddMesh(List<CombineInstance> parts, Mesh mesh, Matrix4x4 matrix)
    {
        parts.Add(new CombineInstance
        {
            mesh = mesh,
            subMeshIndex = 0,
            transform = matrix
        });
    }

    private static Mesh Combine(string name, List<CombineInstance> parts)
    {
        var mesh = new Mesh
        {
            name = name,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.CombineMeshes(parts.ToArray(), true, true, false);
        return mesh;
    }

    private static Mesh GetPrimitiveMesh(PrimitiveType primitive)
    {
        if (PrimitiveCache.TryGetValue(primitive, out var mesh) && mesh != null)
            return mesh;

        var go = GameObject.CreatePrimitive(primitive);
        mesh = go.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying)
            Object.Destroy(go);
        else
            Object.DestroyImmediate(go);

        PrimitiveCache[primitive] = mesh;
        return mesh;
    }

    private static Mesh CreateMahjongTileBodyMesh(string name, float faceWidth, float faceLength, float thickness, float bevel, float seamRatio)
    {
        float halfFaceWidth = faceWidth * 0.5f;
        float halfFaceLength = faceLength * 0.5f;
        float halfWaistWidth = halfFaceWidth + bevel;
        float halfWaistLength = halfFaceLength + bevel;
        float bottomY = 0f;
        float bottomBevelY = bevel;
        float topBevelY = thickness - bevel;
        float seamY = Mathf.Lerp(bottomBevelY, topBevelY, Mathf.Clamp01(seamRatio));
        float topY = thickness;

        var rings = new[]
        {
            CreateRectRing(halfFaceWidth, halfFaceLength, bottomY),
            CreateRectRing(halfWaistWidth, halfWaistLength, bottomBevelY),
            CreateRectRing(halfWaistWidth, halfWaistLength, seamY),
            CreateRectRing(halfWaistWidth, halfWaistLength, seamY),
            CreateRectRing(halfWaistWidth, halfWaistLength, topBevelY),
            CreateRectRing(halfFaceWidth, halfFaceLength, topY)
        };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var semanticUvs = new List<Vector2>();

        AddQuad(vertices, triangles, semanticUvs, rings[0][0], rings[0][3], rings[0][2], rings[0][1], 0f);
        AddRingQuads(vertices, triangles, semanticUvs, rings[0], rings[1], 0f);
        AddRingQuads(vertices, triangles, semanticUvs, rings[1], rings[2], 0f, 0f, 1f);
        AddRingQuads(vertices, triangles, semanticUvs, rings[3], rings[4], 1f, 1f, 0f);
        AddRingQuads(vertices, triangles, semanticUvs, rings[4], rings[5], 1f);
        AddQuad(vertices, triangles, semanticUvs, rings[5][0], rings[5][1], rings[5][2], rings[5][3], 1f);

        var uvs = new Vector2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
            uvs[i] = new Vector2(vertices[i].x / (faceWidth + bevel * 2f) + 0.5f, vertices[i].z / (faceLength + bevel * 2f) + 0.5f);

        var mesh = new Mesh
        {
            name = name,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.uv = uvs;
        mesh.SetUVs(1, semanticUvs);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateRoundTileBodyMesh(string name, float faceRadius, float waistRadius, float thickness, float bevel, float seamRatio, int segments)
    {
        float bottomY = 0f;
        float bottomBevelY = bevel;
        float topBevelY = thickness - bevel;
        float seamY = Mathf.Lerp(bottomBevelY, topBevelY, Mathf.Clamp01(seamRatio));
        float topY = thickness;

        var rings = new[]
        {
            CreateRegularRing(segments, faceRadius, bottomY, 0f),
            CreateRegularRing(segments, waistRadius, bottomBevelY, 0f),
            CreateRegularRing(segments, waistRadius, seamY, 0f),
            CreateRegularRing(segments, waistRadius, seamY, 0f),
            CreateRegularRing(segments, waistRadius, topBevelY, 0f),
            CreateRegularRing(segments, faceRadius, topY, 0f)
        };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        AddPolygonCap(vertices, triangles, rings[0], false);
        AddRingQuads(vertices, triangles, rings[0], rings[1]);
        AddRingQuads(vertices, triangles, rings[1], rings[2]);
        AddRingQuads(vertices, triangles, rings[3], rings[4]);
        AddRingQuads(vertices, triangles, rings[4], rings[5]);
        AddPolygonCap(vertices, triangles, rings[5], true);

        var uvs = new Vector2[vertices.Count];
        float uvScale = waistRadius * 2f;
        for (int i = 0; i < vertices.Count; i++)
            uvs[i] = new Vector2(vertices[i].x / uvScale + 0.5f, vertices[i].z / uvScale + 0.5f);

        var mesh = new Mesh
        {
            name = name,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateGoStoneMesh(string name, float radius, float thickness, int radialSegments, int heightSegments)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        for (int yIndex = 0; yIndex <= heightSegments; yIndex++)
        {
            float t = (float)yIndex / heightSegments;
            float y = Mathf.Lerp(0f, thickness, t);
            float centerDistance = Mathf.Abs(t - 0.5f) * 2f;
            float ringRadius = radius * Mathf.Sqrt(Mathf.Clamp01(1f - centerDistance * centerDistance));
            ringRadius = Mathf.Lerp(radius * 0.92f, ringRadius, centerDistance);

            vertices.AddRange(CreateRegularRing(radialSegments, ringRadius, y, 0f));
        }

        for (int yIndex = 0; yIndex < heightSegments; yIndex++)
        {
            int fromStart = yIndex * radialSegments;
            int toStart = (yIndex + 1) * radialSegments;
            for (int i = 0; i < radialSegments; i++)
            {
                int next = (i + 1) % radialSegments;
                AddQuadByIndex(triangles, fromStart + i, fromStart + next, toStart + next, toStart + i);
            }
        }

        AddIndexedCap(vertices, triangles, 0, radialSegments, false);
        AddIndexedCap(vertices, triangles, heightSegments * radialSegments, radialSegments, true);

        var uvs = new Vector2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
            uvs[i] = new Vector2(vertices[i].x / (radius * 2f) + 0.5f, vertices[i].z / (radius * 2f) + 0.5f);

        var mesh = new Mesh
        {
            name = name,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Vector3[] CreateRectRing(float halfWidth, float halfLength, float y)
    {
        return new[]
        {
            new Vector3(-halfWidth, y, -halfLength),
            new Vector3(halfWidth, y, -halfLength),
            new Vector3(halfWidth, y, halfLength),
            new Vector3(-halfWidth, y, halfLength)
        };
    }

    private static Vector3[] CreateOctRing(float radius, float y)
    {
        return CreateRegularRing(8, radius, y, Mathf.PI / 8f);
    }

    private static Vector3[] CreateRegularRing(int segments, float radius, float y, float angleOffset)
    {
        var ring = new Vector3[segments];
        for (int i = 0; i < ring.Length; i++)
        {
            float angle = Mathf.PI * 2f * i / ring.Length + angleOffset;
            ring[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        return ring;
    }

    private static void AddFlatRing(List<Vector3> vertices, List<int> triangles, int segments, float innerRadius, float outerRadius, float centerY, float height)
    {
        float halfHeight = height * 0.5f;
        var topInner = CreateRegularRing(segments, innerRadius, centerY + halfHeight, 0f);
        var topOuter = CreateRegularRing(segments, outerRadius, centerY + halfHeight, 0f);
        var bottomInner = CreateRegularRing(segments, innerRadius, centerY - halfHeight, 0f);
        var bottomOuter = CreateRegularRing(segments, outerRadius, centerY - halfHeight, 0f);

        AddReverseRingQuads(vertices, triangles, topInner, topOuter);
        AddReverseRingQuads(vertices, triangles, bottomOuter, bottomInner);
        AddReverseRingQuads(vertices, triangles, bottomOuter, topOuter);
        AddReverseRingQuads(vertices, triangles, topInner, bottomInner);
    }

    private static void AddRingQuads(List<Vector3> vertices, List<int> triangles, Vector3[] from, Vector3[] to)
    {
        for (int i = 0; i < from.Length; i++)
        {
            int next = (i + 1) % from.Length;
            AddQuad(vertices, triangles, from[i], from[next], to[next], to[i]);
        }
    }

    private static void AddRingQuads(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs2, Vector3[] from, Vector3[] to, float region)
    {
        AddRingQuads(vertices, triangles, uvs2, from, to, region, 0f, 0f);
    }

    private static void AddRingQuads(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs2, Vector3[] from, Vector3[] to, float region, float fromSeam, float toSeam)
    {
        for (int i = 0; i < from.Length; i++)
        {
            int next = (i + 1) % from.Length;
            AddQuad(vertices, triangles, uvs2, from[i], from[next], to[next], to[i], region, fromSeam, fromSeam, toSeam, toSeam);
        }
    }

    private static void AddReverseRingQuads(List<Vector3> vertices, List<int> triangles, Vector3[] from, Vector3[] to)
    {
        for (int i = 0; i < from.Length; i++)
        {
            int next = (i + 1) % from.Length;
            AddQuad(vertices, triangles, from[i], to[i], to[next], from[next]);
        }
    }

    private static void AddQuadByIndex(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(b);
        triangles.Add(a);
        triangles.Add(d);
        triangles.Add(c);
    }

    private static void AddIndexedCap(List<Vector3> vertices, List<int> triangles, int ringStart, int segments, bool top)
    {
        var center = Vector3.zero;
        for (int i = 0; i < segments; i++)
            center += vertices[ringStart + i];
        center /= segments;

        int centerIndex = vertices.Count;
        vertices.Add(center);
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            if (top)
            {
                triangles.Add(centerIndex);
                triangles.Add(ringStart + next);
                triangles.Add(ringStart + i);
            }
            else
            {
                triangles.Add(centerIndex);
                triangles.Add(ringStart + i);
                triangles.Add(ringStart + next);
            }
        }
    }

    private static void AddPolygonCap(List<Vector3> vertices, List<int> triangles, Vector3[] ring, bool top)
    {
        var center = Vector3.zero;
        for (int i = 0; i < ring.Length; i++)
            center += ring[i];
        center /= ring.Length;

        for (int i = 0; i < ring.Length; i++)
        {
            int next = (i + 1) % ring.Length;
            int start = vertices.Count;
            vertices.Add(center);
            vertices.Add(ring[i]);
            vertices.Add(ring[next]);
            if (top)
            {
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
            }
            else
            {
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }
        }
    }

    private static void AddPointFan(List<Vector3> vertices, List<int> triangles, Vector3[] ring, Vector3 point)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            int next = (i + 1) % ring.Length;
            int start = vertices.Count;
            vertices.Add(point);
            vertices.Add(ring[i]);
            vertices.Add(ring[next]);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
        }
    }

    private static void AddOctahedron(List<Vector3> vertices, List<int> triangles, Vector3 bottom, float radius, float halfHeight)
    {
        Vector3 top = bottom + Vector3.up * (halfHeight * 2f);
        float centerY = bottom.y + halfHeight;
        var equator = new[]
        {
            new Vector3(radius, centerY, 0f),
            new Vector3(0f, centerY, radius),
            new Vector3(-radius, centerY, 0f),
            new Vector3(0f, centerY, -radius)
        };

        AddReverseTriangle(vertices, triangles, bottom, equator[0], equator[1]);
        AddReverseTriangle(vertices, triangles, bottom, equator[1], equator[2]);
        AddReverseTriangle(vertices, triangles, bottom, equator[2], equator[3]);
        AddReverseTriangle(vertices, triangles, bottom, equator[3], equator[0]);
        AddReverseTriangle(vertices, triangles, top, equator[1], equator[0]);
        AddReverseTriangle(vertices, triangles, top, equator[2], equator[1]);
        AddReverseTriangle(vertices, triangles, top, equator[3], equator[2]);
        AddReverseTriangle(vertices, triangles, top, equator[0], equator[3]);
    }

    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
    }

    private static void AddReverseTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs2, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float region)
    {
        AddQuad(vertices, triangles, uvs2, a, b, c, d, region, 0f, 0f, 0f, 0f);
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs2, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float region, float seamA, float seamB, float seamC, float seamD)
    {
        AddQuad(vertices, triangles, a, b, c, d);
        uvs2.Add(new Vector2(region, seamA));
        uvs2.Add(new Vector2(region, seamB));
        uvs2.Add(new Vector2(region, seamC));
        uvs2.Add(new Vector2(region, seamD));
    }

    private static Mesh CreateWedgeMesh()
    {
        var vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(0f, 0.5f, -0.42f),
            new Vector3(0f, 0.5f, 0.42f)
        };
        var triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            0, 1, 4,
            3, 5, 2,
            0, 4, 5, 0, 5, 3,
            1, 2, 5, 1, 5, 4
        };
        var uvs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            uvs[i] = new Vector2(vertices[i].x + 0.5f, vertices[i].z + 0.5f);

        var mesh = new Mesh
        {
            name = "Wedge"
        };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }
}
