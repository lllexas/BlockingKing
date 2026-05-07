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
            _tagIdToColor[tag.tagID] = SampleColor(tag.editorTile);
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
        return _tagIdToColor.TryGetValue(tagId, out var c) ? c : Color.magenta;
    }

    public IEnumerable<int> AllTagIDs
    {
        get {
            if (!_cacheBuilt) RebuildCache();
            return _idToTag.Keys;
        }
    }

    // ─────────── Sprite 颜色采样 ───────────

    private static Color SampleColor(TileBase tile)
    {
        Sprite sprite = GetSpriteFromTile(tile);

        if (sprite == null)
        {
            int hash = tile.name.GetHashCode();
            return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.55f, 0.75f);
        }

        Texture2D tex = sprite.texture;
        if (tex == null || !tex.isReadable)
        {
            int hash = sprite.name.GetHashCode();
            return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.55f, 0.75f);
        }

        try
        {
            Rect r = sprite.textureRect;
            Color[] pixels = tex.GetPixels(
                Mathf.FloorToInt(r.x),
                Mathf.FloorToInt(r.y),
                Mathf.FloorToInt(r.width),
                Mathf.FloorToInt(r.height));

            float sr = 0, sg = 0, sb = 0;
            int count = 0;
            foreach (var p in pixels)
            {
                if (p.a < 0.1f) continue;
                sr += p.r;
                sg += p.g;
                sb += p.b;
                count++;
            }

            if (count > 0)
                return new Color(sr / count, sg / count, sb / count, 1f);
        }
        catch { }

        return Color.gray;
    }

    private static Sprite GetSpriteFromTile(TileBase tile)
    {
        if (tile is Tile t && t.sprite != null)
            return t.sprite;

        var spriteProp = tile.GetType().GetProperty("sprite",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (spriteProp != null)
            return spriteProp.GetValue(tile) as Sprite;

        return null;
    }
}
