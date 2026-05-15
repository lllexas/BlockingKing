using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

// ─────────── Tag 数据 ───────────

[System.Serializable]
public class LevelTagEntry
{
    public int tagID;
    public int x;
    public int y;
    public EntityBP entityBPOverride;

    public EntityBP ResolveEntityBP(TileMappingConfig config)
    {
        if (entityBPOverride != null)
            return entityBPOverride;

        return config != null ? config.GetTagEntityBP(tagID) : null;
    }

    public LevelTagEntry Clone()
    {
        return new LevelTagEntry
        {
            tagID = tagID,
            x = x,
            y = y,
            entityBPOverride = entityBPOverride
        };
    }
}

// ─────────── 关卡 SO ───────────

[CreateAssetMenu(fileName = "LevelData", menuName = "BlockingKing/Levels/Level Data")]
public class LevelData : ScriptableObject
{
    [BoxGroup("基本信息")]
    public string levelName;

    [BoxGroup("尺寸")]
    [Range(1, 100)]
    public int width;

    [BoxGroup("尺寸")]
    [Range(1, 100)]
    public int height;

    [BoxGroup("地形")]
    [HideInInspector]
    public int[] tiles;                     // index = y * width + x, 0=空, 1=墙, 2=地板

    [BoxGroup("Tag")]
    public List<LevelTagEntry> tags = new List<LevelTagEntry>();

    public bool EnsureInitialized(int defaultWidth, int defaultHeight, int defaultTileId = 0)
    {
        bool changed = false;

        if (width <= 0)
        {
            width = Mathf.Max(1, defaultWidth);
            changed = true;
        }

        if (height <= 0)
        {
            height = Mathf.Max(1, defaultHeight);
            changed = true;
        }

        int expectedLength = width * height;
        if (tiles == null || tiles.Length != expectedLength)
        {
            tiles = new int[expectedLength];
            for (int i = 0; i < tiles.Length; i++)
                tiles[i] = defaultTileId;
            changed = true;
        }

        if (tags == null)
        {
            tags = new List<LevelTagEntry>();
            changed = true;
        }

        return changed;
    }

    // ─────────── 地形 ───────────

    public int GetTile(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return -1;
        if (tiles == null || tiles.Length != width * height) return 0;
        return tiles[y * width + x];
    }

    public void SetTile(int x, int y, int id)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        if (tiles == null || tiles.Length != width * height)
            EnsureInitialized(width, height);
        tiles[y * width + x] = id;
    }

    public int[][] GetMap2D()
    {
        if (width <= 0 || height <= 0) return null;
        if (tiles == null || tiles.Length != width * height)
            EnsureInitialized(width, height);

        int[][] result = new int[height][];
        for (int y = 0; y < height; y++)
        {
            result[y] = new int[width];
            for (int x = 0; x < width; x++)
                result[y][x] = tiles[y * width + x];
        }
        return result;
    }

    public void SetFromMap2D(int[][] map)
    {
        if (map == null || map.Length == 0) return;
        height = map.Length;
        width = map[0].Length;
        tiles = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tiles[y * width + x] = map[y][x];
    }

    public int[,] To2DArray()
    {
        if (width <= 0 || height <= 0) return new int[0, 0];
        if (tiles == null || tiles.Length != width * height)
            EnsureInitialized(width, height);

        int[,] result = new int[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                result[y, x] = tiles[y * width + x];
        return result;
    }

    // ─────────── Tag ───────────

    /// <summary>获取指定坐标的所有 tag</summary>
    public List<LevelTagEntry> GetTagsAt(int x, int y)
    {
        var result = new List<LevelTagEntry>();
        foreach (var tag in tags)
            if (tag.x == x && tag.y == y)
                result.Add(tag);
        return result;
    }

    /// <summary>指定坐标是否有某个 tag</summary>
    public bool HasTag(int x, int y, int tagId)
    {
        foreach (var tag in tags)
            if (tag.x == x && tag.y == y && tag.tagID == tagId)
                return true;
        return false;
    }

    /// <summary>获取某个 tag 类型的第一个位置（用于玩家初始位置等唯一实体）</summary>
    public bool TryGetTagPosition(int tagId, out int x, out int y)
    {
        foreach (var tag in tags)
        {
            if (tag.tagID == tagId)
            {
                x = tag.x;
                y = tag.y;
                return true;
            }
        }
        x = 0; y = 0;
        return false;
    }

    /// <summary>获取某个 tag 类型的所有位置（用于箱子、目标点等）</summary>
    public List<Vector2Int> GetAllTagsOfType(int tagId)
    {
        var result = new List<Vector2Int>();
        foreach (var tag in tags)
            if (tag.tagID == tagId)
                result.Add(new Vector2Int(tag.x, tag.y));
        return result;
    }

    /// <summary>添加 tag</summary>
    public void AddTag(int x, int y, int tagId, EntityBP entityBPOverride = null)
    {
        tags.Add(new LevelTagEntry
        {
            tagID = tagId,
            x = x,
            y = y,
            entityBPOverride = entityBPOverride
        });
    }

    /// <summary>移除指定坐标的指定 tag</summary>
    public void RemoveTag(int x, int y, int tagId)
    {
        tags.RemoveAll(t => t.x == x && t.y == y && t.tagID == tagId);
    }

    /// <summary>清空指定坐标的所有 tag</summary>
    public void ClearTagsAt(int x, int y)
    {
        tags.RemoveAll(t => t.x == x && t.y == y);
    }

    /// <summary>获取所有唯一存在的 tag ID（用于渲染）</summary>
    public HashSet<int> GetUniqueTagIDs()
    {
        var set = new HashSet<int>();
        foreach (var tag in tags)
            set.Add(tag.tagID);
        return set;
    }
}
