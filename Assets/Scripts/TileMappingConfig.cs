using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "TileMappingConfig", menuName = "BlockingKing/Tile Mapping Config")]
public class TileMappingConfig : ScriptableObject
{
    [System.Serializable]
    public class TileMappingEntry
    {
        [PreviewField(60)]
        public TileBase tileAsset;
        public int tileID;

        [FoldoutGroup("几何属性")]
        public bool isWall;
    }

    // ─────────── Tag 注册表 ───────────

    [System.Serializable]
    public class TagDefinition
    {
        [PreviewField(50)]
        public TileBase editorTile;
        public int tagID;
        public string tagName;
        public EntityBP entityBP;
        public Color color = Color.white;
    }

    // ─────────── 地形表 ───────────

    [InfoBox("@GetDuplicateTileIDMessage()", InfoMessageType.Error, visibleIfMemberName: "HasDuplicateTileIDs")]
    [TableList(ShowIndexLabels = true)]
    public List<TileMappingEntry> entries = new List<TileMappingEntry>();

    // ─────────── Tag 表 ───────────

    [Space]
    [InfoBox("@GetDuplicateTagIDMessage()", InfoMessageType.Error, visibleIfMemberName: "HasDuplicateTagIDs")]
    [TableList(ShowIndexLabels = true)]
    public List<TagDefinition> tagDefinitions = new List<TagDefinition>();

    // ─────────── ID=0 自动推号（delayCall 避免和 Odin 绘制冲突）───────────

#if UNITY_EDITOR
    [OnInspectorGUI]
    private void DetectUnsetIDs()
    {
        if (_assignScheduled) return;
        if (Application.isPlaying) return;

        bool hasUnset = false;
        foreach (var e in entries)
            if (e.tileID == 0) { hasUnset = true; break; }
        if (!hasUnset)
        {
            foreach (var t in tagDefinitions)
                if (t.tagID == 0) { hasUnset = true; break; }
        }
        if (!hasUnset) return;

        _assignScheduled = true;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            _assignScheduled = false;
            if (this == null) return; // SO 已被删除
            AutoAssignIDs();
            RebuildCache();
        };
    }

    private bool _assignScheduled;
#endif

    private void AutoAssignIDs()
    {
        // 地形
        int nextId = 1;
        foreach (var e in entries)
            if (e.tileID >= nextId) nextId = e.tileID + 1;
        foreach (var e in entries)
            if (e.tileID == 0) e.tileID = nextId++;

        // Tag
        nextId = 1;
        foreach (var t in tagDefinitions)
            if (t.tagID >= nextId) nextId = t.tagID + 1;
        foreach (var t in tagDefinitions)
            if (t.tagID == 0) t.tagID = nextId++;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ─────────── 校验 ───────────

    private bool HasDuplicateTileIDs()
    {
        return entries.GroupBy(e => e.tileID).Any(g => g.Count() > 1);
    }

    private string GetDuplicateTileIDMessage()
    {
        var dups = entries.GroupBy(e => e.tileID)
            .Where(g => g.Count() > 1)
            .Select(g => $"ID {g.Key}: {g.Count()} 个");
        return "重复地形 ID: " + string.Join(", ", dups);
    }

    private bool HasDuplicateTagIDs()
    {
        return tagDefinitions.GroupBy(t => t.tagID).Any(g => g.Count() > 1);
    }

    private string GetDuplicateTagIDMessage()
    {
        var dups = tagDefinitions.GroupBy(t => t.tagID)
            .Where(g => g.Count() > 1)
            .Select(g => $"Tag ID {g.Key}: {g.Count()} 个");
        return "重复 Tag ID: " + string.Join(", ", dups);
    }

    // ─────────── 缓存 ───────────

    private Dictionary<TileBase, int> _assetToId;
    private Dictionary<int, TileBase> _idToAsset;
    private Dictionary<int, Color> _idToColor;
    private HashSet<int> _wallIds;

    private Dictionary<TileBase, int> _tagTileToId;
    private Dictionary<int, TagDefinition> _idToTag;
    private Dictionary<int, Color> _tagIdToColor;

    private bool _cacheBuilt;

    public void RebuildCache()
    {
        _assetToId = new Dictionary<TileBase, int>();
        _idToAsset = new Dictionary<int, TileBase>();
        _idToColor = new Dictionary<int, Color>();
        _wallIds = new HashSet<int>();

        _tagTileToId = new Dictionary<TileBase, int>();
        _idToTag = new Dictionary<int, TagDefinition>();
        _tagIdToColor = new Dictionary<int, Color>();

        foreach (var entry in entries)
        {
            if (entry.tileAsset == null) continue;
            if (entry.tileID == 0) continue;

            _assetToId[entry.tileAsset] = entry.tileID;
            _idToAsset[entry.tileID] = entry.tileAsset;
            _idToColor[entry.tileID] = SampleColor(entry.tileAsset);

            if (entry.isWall)
                _wallIds.Add(entry.tileID);
        }

        foreach (var tag in tagDefinitions)
        {
            if (tag.editorTile == null) continue;
            if (tag.tagID == 0) continue;

            _tagTileToId[tag.editorTile] = tag.tagID;
            _idToTag[tag.tagID] = tag;
            _tagIdToColor[tag.tagID] = tag.color;
        }

        _cacheBuilt = true;
    }

    // ─────────── 地形查询 ───────────

    public int GetTileID(TileBase tile)
    {
        if (tile == null) return 0;
        if (!_cacheBuilt) RebuildCache();
        return _assetToId.TryGetValue(tile, out int id) ? id : 0;
    }

    public TileBase GetTileAsset(int tileId)
    {
        if (tileId == 0) return null;
        if (!_cacheBuilt) RebuildCache();
        return _idToAsset.TryGetValue(tileId, out var asset) ? asset : null;
    }

    public Color GetTileColor(int tileId)
    {
        if (!_cacheBuilt) RebuildCache();
        return _idToColor.TryGetValue(tileId, out var c) ? c : Color.magenta;
    }

    public bool IsWall(int tileId)
    {
        if (!_cacheBuilt) RebuildCache();
        return _wallIds.Contains(tileId);
    }

    // ─────────── Tag 查询 ───────────

    public int GetTagID(TileBase tile)
    {
        if (tile == null) return 0;
        if (!_cacheBuilt) RebuildCache();
        return _tagTileToId.TryGetValue(tile, out int id) ? id : 0;
    }

    public TileBase GetTagTile(int tagId)
    {
        if (!_cacheBuilt) RebuildCache();
        return _idToTag.TryGetValue(tagId, out var tag) ? tag.editorTile : null;
    }

    public string GetTagName(int tagId)
    {
        if (!_cacheBuilt) RebuildCache();
        return _idToTag.TryGetValue(tagId, out var tag) ? tag.tagName : "Unknown";
    }

    public Color GetTagColor(int tagId)
    {
        if (!_cacheBuilt) RebuildCache();
        return _tagIdToColor.TryGetValue(tagId, out var c) ? c : Color.white;
    }

    public EntityBP GetTagEntityBP(int tagId)
    {
        if (!_cacheBuilt) RebuildCache();
        return _idToTag.TryGetValue(tagId, out var tag) ? tag.entityBP : null;
    }

    public IEnumerable<int> AllTagIDs
    {
        get {
            if (!_cacheBuilt) RebuildCache();
            return _idToTag.Keys;
        }
    }

    // ─────────── 颜色生成（基于名称哈希，不采样 Tile 纹理） ───────────

    private static Color SampleColor(TileBase tile)
    {
        string key = tile != null ? tile.name : "unknown";
        int hash = key.GetHashCode();
        return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.6f, 0.8f);
    }
}
