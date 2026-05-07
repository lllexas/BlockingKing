using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;

/// <summary>
/// 关卡烘焙窗口：从场景双层 Tilemap 生成 LevelData SO
///   - Terrain Tilemap → int[] tiles
///   - Objects Tilemap → List<LevelTagEntry> tags
/// </summary>
public class LevelBakerWindow : EditorWindow
{
    private string _levelId = "Level_01";
    private TileMappingConfig _mappingConfig;
    private Tilemap _terrainTilemap;
    private Tilemap _objectsTilemap;
    private string _outputPath = "Assets/Levels/";

    [MenuItem("Tools/推箱子/关卡烘焙机")]
    public static void ShowWindow()
    {
        var window = GetWindow<LevelBakerWindow>("关卡烘焙机");
        window.minSize = new Vector2(400, 380);
    }

    private void OnGUI()
    {
        GUILayout.Label("关卡烘焙工具", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        _levelId = EditorGUILayout.TextField("关卡 ID", _levelId);
        _outputPath = EditorGUILayout.TextField("输出目录", _outputPath);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("拖入引用：", EditorStyles.label);

        _mappingConfig = (TileMappingConfig)EditorGUILayout.ObjectField(
            "Tile 映射配置", _mappingConfig, typeof(TileMappingConfig), false);

        _terrainTilemap = (Tilemap)EditorGUILayout.ObjectField(
            "地形 Tilemap", _terrainTilemap, typeof(Tilemap), true);

        _objectsTilemap = (Tilemap)EditorGUILayout.ObjectField(
            "对象 Tilemap", _objectsTilemap, typeof(Tilemap), true);

        EditorGUILayout.Space(20);

        // 预览
        if (_terrainTilemap != null)
        {
            _terrainTilemap.CompressBounds();
            var b = _terrainTilemap.cellBounds;
            EditorGUILayout.HelpBox(
                $"地形范围: {b.size.x} x {b.size.y}", MessageType.Info);
        }

        if (_objectsTilemap != null)
        {
            _objectsTilemap.CompressBounds();
            var b = _objectsTilemap.cellBounds;
            int count = 0;
            for (int y = b.yMin; y < b.yMax; y++)
                for (int x = b.xMin; x < b.xMax; x++)
                    if (_objectsTilemap.GetTile(new Vector3Int(x, y, 0)) != null)
                        count++;
            EditorGUILayout.HelpBox(
                $"对象 Tilemap: {b.size.x} x {b.size.y}, 有效标记: {count}", MessageType.Info);
        }

        EditorGUILayout.Space(10);

        GUI.enabled = _mappingConfig != null && _terrainTilemap != null;
        if (GUILayout.Button("烘焙为 LevelData SO", GUILayout.Height(40)))
        {
            Bake();
        }
        GUI.enabled = true;
    }

    private void Bake()
    {
        if (_mappingConfig == null)
        {
            EditorUtility.DisplayDialog("错误", "请先拖入 Tile Mapping Config！", "确定");
            return;
        }
        if (_terrainTilemap == null)
        {
            EditorUtility.DisplayDialog("错误", "请先拖入地形 Tilemap！", "确定");
            return;
        }

        _mappingConfig.RebuildCache();

        _terrainTilemap.CompressBounds();
        BoundsInt terrainBounds = _terrainTilemap.cellBounds;
        if (terrainBounds.size.x == 0 || terrainBounds.size.y == 0)
        {
            EditorUtility.DisplayDialog("错误", "地形 Tilemap 是空的！", "确定");
            return;
        }

        int w = terrainBounds.size.x;
        int h = terrainBounds.size.y;
        int unregistered = 0;

        // ── 烤地形 ──
        int[][] map2D = new int[h][];
        for (int y = 0; y < h; y++)
        {
            map2D[h - 1 - y] = new int[w];
            for (int x = 0; x < w; x++)
            {
                Vector3Int wp = new Vector3Int(terrainBounds.xMin + x, terrainBounds.yMin + y, 0);
                TileBase tile = _terrainTilemap.GetTile(wp);
                int id = tile != null ? _mappingConfig.GetTileID(tile) : 0;
                if (id == 0 && tile != null)
                {
                    unregistered++;
                    Debug.LogWarning($"<color=yellow>【未注册地形 Tile】</color> {tile.name} @ {wp}");
                }
                map2D[h - 1 - y][x] = id;
            }
        }

        // ── 烤 Tag ──
        var tags = new List<LevelTagEntry>();
        int tagUnregistered = 0;

        if (_objectsTilemap != null)
        {
            _objectsTilemap.CompressBounds();
            BoundsInt objBounds = _objectsTilemap.cellBounds;

            for (int y = objBounds.yMin; y < objBounds.yMax; y++)
            {
                for (int x = objBounds.xMin; x < objBounds.xMax; x++)
                {
                    Vector3Int wp = new Vector3Int(x, y, 0);
                    TileBase tile = _objectsTilemap.GetTile(wp);
                    if (tile == null) continue;

                    int tagId = _mappingConfig.GetTagID(tile);
                    if (tagId == 0)
                    {
                        tagUnregistered++;
                        Debug.LogWarning($"<color=yellow>【未注册 Tag Tile】</color> {tile.name} @ {wp}");
                        continue;
                    }

                    int lx = x - terrainBounds.xMin;
                    int ly = (terrainBounds.yMax - 1) - y;
                    if (lx >= 0 && lx < w && ly >= 0 && ly < h)
                    {
                        tags.Add(new LevelTagEntry { tagID = tagId, x = lx, y = ly });
                    }
                }
            }
        }

        // ── 创建 SO ──
        var levelData = ScriptableObject.CreateInstance<LevelData>();
        levelData.levelName = _levelId;
        levelData.SetFromMap2D(map2D);
        levelData.tags = tags;

        if (!System.IO.Directory.Exists(_outputPath))
            System.IO.Directory.CreateDirectory(_outputPath);

        string assetPath = $"{_outputPath}{_levelId}.asset";
        AssetDatabase.CreateAsset(levelData, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string warn = "";
        if (unregistered > 0) warn += $"\n⚠ {unregistered} 个未注册地形 Tile";
        if (tagUnregistered > 0) warn += $"\n⚠ {tagUnregistered} 个未注册 Tag Tile";

        Debug.Log(
            $"<color=green>关卡 [{_levelId}] 烘焙完成</color>\n" +
            $"地形: {w}x{h}, Tag: {tags.Count} 个\n输出: {assetPath}{warn}");

        EditorUtility.DisplayDialog("烘焙完成",
            $"关卡 [{_levelId}] 已创建\n地形: {w}x{h}\nTag: {tags.Count} 个" + warn,
            "确定");

        Selection.activeObject = levelData;
        EditorGUIUtility.PingObject(levelData);
    }
}
