using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 根据关卡数据生成几何风格 Mesh：
///   - 地形：地面 quad + 墙壁凸起五面体
///   - Tag：浮在地面之上的彩色小标记板
/// 每个面使用独立顶点（法线不合并）
/// </summary>
public class LevelMeshBuilder
{
    public float cellSize = 1f;
    public float wallHeight = 1f;
    public float tagMarkerSize = 0.35f;
    public float tagYOffset = 0.02f;

    /// <summary>
    /// 构建完整关卡 Mesh（地形 + Tag 标记）
    /// </summary>
    public Mesh Build(int[][] map, TileMappingConfig config,
        IReadOnlyList<LevelTagEntry> tags = null)
    {
        if (map == null || map.Length == 0) return null;

        int h = map.Length;
        int w = map[0].Length;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        var colors = new List<Color>();

        BuildTerrain(map, config, w, h, verts, normals, tris, colors);
        if (tags != null && tags.Count > 0)
            BuildTagMarkers(map, tags, config, w, h, verts, normals, tris, colors);

        Mesh mesh = new Mesh();
        mesh.name = "LevelMesh";
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.SetColors(colors);
        return mesh;
    }

    // ─────────── 地形 ───────────

    private void BuildTerrain(int[][] map, TileMappingConfig config,
        int w, int h, List<Vector3> verts, List<Vector3> normals,
        List<int> tris, List<Color> colors)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int id = map[y][x];
                if (id == 0) continue;

                bool isWall = config != null && config.IsWall(id);
                // 顶点色编码: R=1 表示墙（供 Shader 区分）
                Color wallColor = new Color(1f, 0f, 0f, 1f);
                Color floorColor = new Color(0f, 0f, 0f, 0f);
                Vector3 origin = new Vector3(x * cellSize, 0f, y * cellSize);

                if (isWall)
                {
                    AddQuad(verts, normals, tris, colors,
                        origin, wallHeight, wallColor, FaceDir.Up);
                    AddQuad(verts, normals, tris, colors,
                        origin, 0f, wallColor, FaceDir.Front);
                    AddQuad(verts, normals, tris, colors,
                        origin, 0f, wallColor, FaceDir.Back);
                    AddQuad(verts, normals, tris, colors,
                        origin, 0f, wallColor, FaceDir.Right);
                    AddQuad(verts, normals, tris, colors,
                        origin, 0f, wallColor, FaceDir.Left);
                }
                else
                {
                    AddQuad(verts, normals, tris, colors,
                        origin, 0f, floorColor, FaceDir.Up);
                }
            }
        }
    }

    // ─────────── Tag 标记 ───────────

    private void BuildTagMarkers(int[][] map, IReadOnlyList<LevelTagEntry> tags,
        TileMappingConfig config, int w, int h,
        List<Vector3> verts, List<Vector3> normals,
        List<int> tris, List<Color> colors)
    {
        foreach (var tag in tags)
        {
            if (tag.x < 0 || tag.x >= w || tag.y < 0 || tag.y >= h) continue;

            Color color = config != null ? config.GetTagColor(tag.tagID) : Color.white;

            // Tag 在墙顶还是地面上？
            int terrainId = map[tag.y][tag.x];
            bool onWall = config != null && config.IsWall(terrainId);
            float yBase = onWall ? wallHeight : 0f;
            float yPos = yBase + tagYOffset;

            float hs = tagMarkerSize * cellSize * 0.5f;
            Vector3 center = new Vector3(
                (tag.x + 0.5f) * cellSize,
                yPos,
                (tag.y + 0.5f) * cellSize);

            Vector3 lb = center + new Vector3(-hs, 0, -hs);
            Vector3 rb = center + new Vector3(hs, 0, -hs);
            Vector3 lt = center + new Vector3(-hs, 0, hs);
            Vector3 rt = center + new Vector3(hs, 0, hs);

            AddQuadRaw(verts, normals, tris, colors,
                lb, rb, lt, rt, Vector3.up, color);
        }
    }

    // ─────────── Quad 构建 ───────────

    private enum FaceDir { Up, Front, Back, Left, Right }

    private void AddQuad(List<Vector3> verts, List<Vector3> normals, List<int> tris,
        List<Color> colors, Vector3 origin, float baseY, Color color, FaceDir dir)
    {
        float s = cellSize;
        Vector3 lb, rb, lt, rt;

        switch (dir)
        {
            case FaceDir.Up:
                lb = origin + new Vector3(0, baseY, 0);
                rb = origin + new Vector3(s, baseY, 0);
                lt = origin + new Vector3(0, baseY, s);
                rt = origin + new Vector3(s, baseY, s);
                break;
            case FaceDir.Front:
                lb = origin + new Vector3(0, 0, s);
                rb = origin + new Vector3(s, 0, s);
                lt = origin + new Vector3(0, wallHeight, s);
                rt = origin + new Vector3(s, wallHeight, s);
                break;
            case FaceDir.Back:
                lb = origin + new Vector3(s, 0, 0);
                rb = origin + new Vector3(0, 0, 0);
                lt = origin + new Vector3(s, wallHeight, 0);
                rt = origin + new Vector3(0, wallHeight, 0);
                break;
            case FaceDir.Right:
                lb = origin + new Vector3(s, 0, 0);
                rb = origin + new Vector3(s, 0, s);
                lt = origin + new Vector3(s, wallHeight, 0);
                rt = origin + new Vector3(s, wallHeight, s);
                break;
            case FaceDir.Left:
                lb = origin + new Vector3(0, 0, s);
                rb = origin + new Vector3(0, 0, 0);
                lt = origin + new Vector3(0, wallHeight, s);
                rt = origin + new Vector3(0, wallHeight, 0);
                break;
            default:
                lb = rb = lt = rt = Vector3.zero;
                break;
        }

        Vector3 normal;
        switch (dir)
        {
            case FaceDir.Front: normal = Vector3.forward; break;
            case FaceDir.Back:  normal = Vector3.back;   break;
            case FaceDir.Right: normal = Vector3.right;  break;
            case FaceDir.Left:  normal = Vector3.left;   break;
            default:           normal = Vector3.up;     break;
        }

        AddQuadRaw(verts, normals, tris, colors, lb, rb, lt, rt, normal, color);
    }

    private static void AddQuadRaw(List<Vector3> verts, List<Vector3> normals,
        List<int> tris, List<Color> colors,
        Vector3 lb, Vector3 rb, Vector3 lt, Vector3 rt,
        Vector3 normal, Color color)
    {
        int vi = verts.Count;
        verts.Add(lb); normals.Add(normal); colors.Add(color);
        verts.Add(rb); normals.Add(normal); colors.Add(color);
        verts.Add(lt); normals.Add(normal); colors.Add(color);
        verts.Add(rt); normals.Add(normal); colors.Add(color);

        tris.Add(vi + 0); tris.Add(vi + 1); tris.Add(vi + 2);
        tris.Add(vi + 1); tris.Add(vi + 3); tris.Add(vi + 2);
    }
}
