#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// 驻留在 TilemapLevelEditor 场景中，管理三层 Tilemap 编辑：
///   - Terrain Tilemap → LevelData.tiles (静态地形)
///   - Targets Tilemap → LevelData.tags (非占格标记)
///   - Actors Tilemap → LevelData.tags (占格单位)
/// </summary>
[ExecuteAlways]
public class LevelTilemapEditor : MonoBehaviour
{
    public Grid grid;
    public Tilemap terrainTilemap;
    public Tilemap targetTilemap;
    public Tilemap actorTilemap;

    [SerializeField] private TileMappingConfig _tileMappingConfig;

    private string _levelDataGUID;
    private bool _sessionLoaded;

    public string LevelDataGUID => _levelDataGUID;
    public bool HasActiveSession => !string.IsNullOrEmpty(_levelDataGUID);

    private void OnEnable()
    {
        if (terrainTilemap != null && targetTilemap != null && actorTilemap != null)
            LoadSession();
    }

    public void LoadSession()
    {
        if (_sessionLoaded) return;
        if (terrainTilemap == null || targetTilemap == null || actorTilemap == null) return;
        if (!LevelTilemapSessionBridge.TryGetSession(out var session)) return;

        _levelDataGUID = session.LevelDataGUID;
        _sessionLoaded = true;

        LoadLevelDataToTilemaps(_levelDataGUID);
        Debug.Log($"<color=cyan>已加载关卡到三层 Tilemap 编辑器</color> [GUID: {_levelDataGUID}]");
    }

    private void LoadLevelDataToTilemaps(string guid)
    {
        var levelData = LoadAsset(guid);
        if (levelData == null) return;

        EnsureConfig();
        if (levelData.EnsureInitialized(10, 10, GetDefaultFloorTileId()))
        {
            EditorUtility.SetDirty(levelData);
            AssetDatabase.SaveAssets();
        }

        terrainTilemap.ClearAllTiles();
        targetTilemap.ClearAllTiles();
        actorTilemap.ClearAllTiles();

        int w = levelData.width;
        int h = levelData.height;

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

        if (levelData.tags == null)
            levelData.tags = new List<LevelTagEntry>();

        foreach (var tag in levelData.tags)
        {
            TileBase tile = _tileMappingConfig?.GetTagTile(tag.tagID);
            if (tile != null)
                GetTagTilemap(tag.tagID).SetTile(TilePos(tag.x, h, tag.y), tile);
        }

        Debug.Log($"已加载 [{levelData.levelName}]: 地形 {w}x{h}, Tag {levelData.tags.Count} 个");
    }

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

        terrainTilemap.CompressBounds();
        BoundsInt terrainBounds = terrainTilemap.cellBounds;
        int w = Mathf.Max(1, terrainBounds.size.x);
        int h = Mathf.Max(1, terrainBounds.size.y);

        int[][] map2D = new int[h][];
        for (int y = 0; y < h; y++)
        {
            map2D[y] = new int[w];
            for (int x = 0; x < w; x++)
            {
                Vector3Int wp = new Vector3Int(terrainBounds.xMin + x, terrainBounds.yMin + y, 0);
                TileBase tile = terrainTilemap.GetTile(wp);
                map2D[y][x] = tile != null ? _tileMappingConfig.GetTileID(tile) : 0;
            }
        }
        levelData.SetFromMap2D(map2D);

        levelData.tags = new List<LevelTagEntry>();
        AppendTagsFromTilemap(levelData.tags, targetTilemap, terrainBounds, w, h);
        AppendTagsFromTilemap(levelData.tags, actorTilemap, terrainBounds, w, h);

        EditorUtility.SetDirty(levelData);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=green>保存 [{levelData.levelName}]: {w}x{h}, Tag {levelData.tags.Count} 个</color>");
        return true;
    }

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

    private void EnsureConfig()
    {
        if (_tileMappingConfig == null)
        {
            var guids = AssetDatabase.FindAssets("t:TileMappingConfig");
            if (guids.Length > 0)
                _tileMappingConfig = AssetDatabase.LoadAssetAtPath<TileMappingConfig>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        _tileMappingConfig?.RebuildCache();
    }

    private int GetDefaultFloorTileId()
    {
        if (_tileMappingConfig == null || _tileMappingConfig.entries == null)
            return 0;

        foreach (var entry in _tileMappingConfig.entries)
        {
            if (entry != null && !entry.isWall && entry.tileID != 0)
                return entry.tileID;
        }

        return 0;
    }

    private Tilemap GetTagTilemap(int tagId)
    {
        return IsTargetTag(tagId) ? targetTilemap : actorTilemap;
    }

    private bool IsTargetTag(int tagId)
    {
        string tagName = _tileMappingConfig != null ? _tileMappingConfig.GetTagName(tagId) : string.Empty;
        return string.Equals(tagName, "target", System.StringComparison.OrdinalIgnoreCase);
    }

    private void AppendTagsFromTilemap(List<LevelTagEntry> tags, Tilemap source, BoundsInt terrainBounds, int w, int h)
    {
        if (source == null)
            return;

        source.CompressBounds();
        BoundsInt bounds = source.cellBounds;

        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                Vector3Int wp = new Vector3Int(x, y, 0);
                TileBase tile = source.GetTile(wp);
                if (tile == null) continue;

                int tagId = _tileMappingConfig.GetTagID(tile);
                if (tagId == 0) continue;

                int lx = x - terrainBounds.xMin;
                int ly = y - terrainBounds.yMin;
                if (lx < 0 || lx >= w || ly < 0 || ly >= h) continue;

                tags.Add(new LevelTagEntry { tagID = tagId, x = lx, y = ly });
            }
        }
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
        return new Vector3Int(x, y, 0);
    }
}
#endif
