using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// 驻留在 TilemapLevelEditor 场景中，管理双层 Tilemap 编辑：
///   - Terrain Tilemap → LevelData.tiles (静态地形)
///   - Objects Tilemap → LevelData.tags (Tag 标记)
/// </summary>
[ExecuteAlways]
public class LevelTilemapEditor : MonoBehaviour
{
    public Grid grid;
    public Tilemap terrainTilemap;
    public Tilemap objectsTilemap;

    [SerializeField] private TileMappingConfig _tileMappingConfig;

    private string _levelDataGUID;
    private bool _sessionLoaded;

    public string LevelDataGUID => _levelDataGUID;
    public bool HasActiveSession => !string.IsNullOrEmpty(_levelDataGUID);

    private void OnEnable()
    {
        LoadSession();
    }

    // ─────────── 从会话加载 ───────────

    private void LoadSession()
    {
        if (_sessionLoaded) return;
        if (!LevelTilemapSessionBridge.TryGetSession(out var session)) return;

        _levelDataGUID = session.LevelDataGUID;
        _sessionLoaded = true;

        LoadLevelDataToTilemaps(_levelDataGUID);
        Debug.Log($"<color=cyan>已加载关卡到双层 Tilemap 编辑器</color> [GUID: {_levelDataGUID}]");
    }

    // ─────────── 加载 SO → 双层 Tilemap ───────────

    private void LoadLevelDataToTilemaps(string guid)
    {
        var levelData = LoadAsset(guid);
        if (levelData == null) return;

        EnsureConfig();
        terrainTilemap.ClearAllTiles();
        objectsTilemap.ClearAllTiles();

        int w = levelData.width;
        int h = levelData.height;

        // Terrain 层
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int id = levelData.GetTile(x, y);
                if (id == 0) continue;

                TileBase tile = _tileMappingConfig?.GetTileAsset(id);
                if (tile != null)
                    terrainTilemap.SetTile(TilePos(x, h, y), tile);
            }
        }

        // Objects 层：每个 tag 放一个 Tile
        foreach (var tag in levelData.tags)
        {
            TileBase tile = _tileMappingConfig?.GetTagTile(tag.tagID);
            if (tile != null)
                objectsTilemap.SetTile(TilePos(tag.x, h, tag.y), tile);
        }

        Debug.Log($"已加载 [{levelData.levelName}]: 地形 {w}x{h}, Tag {levelData.tags.Count} 个");
    }

    // ─────────── 保存双层 Tilemap → SO ───────────

    public bool SaveToLevelData()
    {
        if (string.IsNullOrEmpty(_levelDataGUID))
        {
            Debug.LogError("<color=red>无会话 GUID。</color>");
            return false;
        }

        var levelData = LoadAsset(_levelDataGUID);
        if (levelData == null) return false;

        EnsureConfig();

        // ── 计算地形包围盒 ──
        terrainTilemap.CompressBounds();
        BoundsInt terrainBounds = terrainTilemap.cellBounds;
        int w = Mathf.Max(1, terrainBounds.size.x);
        int h = Mathf.Max(1, terrainBounds.size.y);

        int[][] map2D = new int[h][];
        for (int y = 0; y < h; y++)
        {
            map2D[h - 1 - y] = new int[w];
            for (int x = 0; x < w; x++)
            {
                Vector3Int wp = new Vector3Int(terrainBounds.xMin + x, terrainBounds.yMin + y, 0);
                TileBase tile = terrainTilemap.GetTile(wp);
                map2D[h - 1 - y][x] = tile != null ? _tileMappingConfig.GetTileID(tile) : 0;
            }
        }
        levelData.SetFromMap2D(map2D);

        // ── 收集 Tag ──
        levelData.tags = new List<LevelTagEntry>();
        objectsTilemap.CompressBounds();
        BoundsInt objectsBounds = objectsTilemap.cellBounds;

        for (int y = objectsBounds.yMin; y < objectsBounds.yMax; y++)
        {
            for (int x = objectsBounds.xMin; x < objectsBounds.xMax; x++)
            {
                Vector3Int wp = new Vector3Int(x, y, 0);
                TileBase tile = objectsTilemap.GetTile(wp);
                if (tile == null) continue;

                int tagId = _tileMappingConfig.GetTagID(tile);
                if (tagId == 0) continue;

                int lx = x - terrainBounds.xMin;
                int ly = (terrainBounds.yMax - 1) - y;
                if (lx < 0 || lx >= w || ly < 0 || ly >= h) continue;

                levelData.tags.Add(new LevelTagEntry { tagID = tagId, x = lx, y = ly });
            }
        }

        EditorUtility.SetDirty(levelData);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=green>保存 [{levelData.levelName}]: {w}x{h}, Tag {levelData.tags.Count} 个</color>");
        return true;
    }

    // ─────────── 保存并返回 ───────────

    public void SaveAndReturn()
    {
        if (!SaveToLevelData()) return;

        LevelTilemapSessionBridge.MarkReturning();

        if (LevelTilemapSessionBridge.TryGetSession(out var session))
        {
            if (session.SceneSetup != null && session.SceneSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(session.SceneSetup);
                return;
            }

            if (!string.IsNullOrEmpty(session.ReturnScenePath))
            {
                var returnScene = EditorSceneManager.OpenScene(session.ReturnScenePath, OpenSceneMode.Additive);
                if (returnScene.IsValid())
                    EditorSceneManager.SetActiveScene(returnScene);

                var currentScene = gameObject.scene;
                if (currentScene.IsValid())
                    EditorSceneManager.CloseScene(currentScene, true);
            }
        }
    }

    public void SaveOnly()
    {
        SaveToLevelData();
    }

    // ─────────── 辅助 ───────────

    private void EnsureConfig()
    {
        if (_tileMappingConfig != null) return;
        var guids = AssetDatabase.FindAssets("t:TileMappingConfig");
        if (guids.Length > 0)
            _tileMappingConfig = AssetDatabase.LoadAssetAtPath<TileMappingConfig>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    private LevelData LoadAsset(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError($"<color=red>GUID 无效: {guid}</color>");
            return null;
        }
        return AssetDatabase.LoadAssetAtPath<LevelData>(path);
    }

    private Vector3Int TilePos(int x, int h, int y)
    {
        return new Vector3Int(x, h - 1 - y, 0);
    }
}
