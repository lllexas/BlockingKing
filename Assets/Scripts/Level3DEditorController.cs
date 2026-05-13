#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play Mode 3D level editor. It edits an in-memory draft and writes back to the
/// source LevelData only when Save is requested.
/// </summary>
public sealed class Level3DEditorController : MonoBehaviour
{
    private const string HoverOverlayId = "level_3d_editor_hover";
    private const int DefaultPanelWidth = 330;
    private const float PanelMargin = 12f;
    private const float ToolbarHeight = 68f;
    private const float CellInspectorHeight = 92f;
    private const float FilterHeight = 116f;
    private const float FooterHeight = 36f;
    private const float SectionGap = 8f;

    private readonly List<Brush> _brushes = new();
    private readonly List<Brush> _visibleBrushes = new();
    private readonly Stack<Snapshot> _undoStack = new();
    private readonly Stack<Snapshot> _redoStack = new();
    private readonly List<Vector2Int> _singleCell = new(1);

    private LevelPlayer _player;
    private LevelData _source;
    private LevelData _draft;
    private TileMappingConfig _config;
    private int _brushIndex;
    private Vector2Int _hoverCell;
    private bool _hasHover;
    private bool _dirty;
    private BrushCategory _category = BrushCategory.All;
    private string _search = string.Empty;
    private Vector2 _paletteScroll;
    private string _lastAppliedKey;
    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _selectedButtonStyle;
    private GUIStyle _miniLabelStyle;
    private GUIStyle _colorSwatchStyle;

    public void Configure(LevelPlayer player, LevelData source, TileMappingConfig config)
    {
        _player = player;
        _source = source;
        _config = config;
        _config?.RebuildCache();

        _draft = CloneLevelData(source);
        BuildBrushes();
        DisableRuntimeInput();
        RebuildPreview();
    }

    public LevelData SourceLevel => _source;
    public bool HasUnsavedChanges => _dirty;

    private void OnDisable()
    {
        GridOverlayDrawSystem.Instance?.RemoveOverlay(HoverOverlayId);
    }

    private void Update()
    {
        if (_player == null || _draft == null)
            return;

        UpdateHover();
        HandleShortcuts();
        HandlePointerEdit();
    }

    private void OnGUI()
    {
        if (_player == null || _draft == null)
            return;

        EnsureStyles();

        float width = Mathf.Min(DefaultPanelWidth, Screen.width - PanelMargin * 2f);
        float height = Mathf.Max(220f, Screen.height - PanelMargin * 2f);
        var rect = new Rect(PanelMargin, PanelMargin, width, height);
        GUILayout.BeginArea(rect, GUIContent.none, _panelStyle);

        GUILayout.Label("3D Level Editor", _titleStyle);
        GUILayout.Label(BuildStatusText(), _miniLabelStyle);
        GUILayout.Space(8f);

        DrawToolbarButtons(ToolbarHeight);
        GUILayout.Space(SectionGap);
        DrawCellInspector(CellInspectorHeight);
        GUILayout.Space(SectionGap);
        DrawPaletteFilters(FilterHeight);
        GUILayout.Space(SectionGap);

        float usedHeight = 60f + ToolbarHeight + CellInspectorHeight + FilterHeight + FooterHeight + SectionGap * 4f;
        float paletteHeight = Mathf.Max(64f, height - usedHeight);
        DrawBrushButtons(paletteHeight);

        GUILayout.Space(SectionGap);
        GUILayout.Label("LMB paint   RMB erase   1-9 quick select   Ctrl+Z/Y undo/redo   Ctrl+S save   Esc exit", _miniLabelStyle);
        GUILayout.EndArea();
    }

    private void DrawToolbarButtons(float height)
    {
        GUILayout.BeginVertical(GUILayout.Height(height));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", _buttonStyle, GUILayout.Height(30f)))
            SaveChanges();
        if (GUILayout.Button("Undo", _buttonStyle, GUILayout.Height(30f)))
            Undo();
        if (GUILayout.Button("Redo", _buttonStyle, GUILayout.Height(30f)))
            Redo();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Discard", _buttonStyle, GUILayout.Height(30f)))
            DiscardChanges();
        if (GUILayout.Button("Exit", _buttonStyle, GUILayout.Height(30f)))
            ExitEditor();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void DrawBrushButtons(float height)
    {
        RebuildVisibleBrushes();
        GUILayout.Label($"Palette ({_visibleBrushes.Count}/{_brushes.Count})", _miniLabelStyle);
        _paletteScroll = GUILayout.BeginScrollView(_paletteScroll, GUILayout.Height(height));
        for (int i = 0; i < _visibleBrushes.Count; i++)
        {
            Brush brush = _visibleBrushes[i];
            int brushIndex = _brushes.IndexOf(brush);
            GUIStyle style = brushIndex == _brushIndex ? _selectedButtonStyle : _buttonStyle;
            string hotkey = i < 9 ? $"{i + 1}. " : string.Empty;
            GUILayout.BeginHorizontal();
            DrawColorSwatch(brush.Color);
            if (GUILayout.Button(hotkey + brush.Label, style, GUILayout.Height(28f)))
                _brushIndex = brushIndex;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    private string BuildStatusText()
    {
        string levelName = _source != null ? _source.levelName : "<none>";
        string cellText = _hasHover ? $"cell {_hoverCell.x},{_hoverCell.y}" : "cell -";
        string dirtyText = _dirty ? "modified" : "saved";
        string brushText = _brushes.Count > 0 ? _brushes[Mathf.Clamp(_brushIndex, 0, _brushes.Count - 1)].Label : "<none>";
        int entityCount = EntitySystem.Instance != null && EntitySystem.Instance.entities != null ? EntitySystem.Instance.entities.entityCount : 0;
        return $"{levelName}   {_draft.width}x{_draft.height}   ECS {entityCount}   {cellText}\n{dirtyText}   brush: {brushText}";
    }

    private void DrawPaletteFilters(float height)
    {
        GUILayout.BeginVertical(GUILayout.Height(height));
        GUILayout.Label("Category", _miniLabelStyle);
        GUILayout.BeginHorizontal();
        DrawCategoryButton("All", BrushCategory.All);
        DrawCategoryButton("Terrain", BrushCategory.Terrain);
        DrawCategoryButton("Actors", BrushCategory.Actor);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        DrawCategoryButton("Targets", BrushCategory.Target);
        DrawCategoryButton("Walls", BrushCategory.Wall);
        DrawCategoryButton("Other", BrushCategory.Other);
        GUILayout.EndHorizontal();

        GUILayout.Label("Search", _miniLabelStyle);
        _search = GUILayout.TextField(_search ?? string.Empty, GUILayout.Height(24f));
        GUILayout.EndVertical();
    }

    private void DrawCategoryButton(string label, BrushCategory category)
    {
        GUIStyle style = _category == category ? _selectedButtonStyle : _buttonStyle;
        if (GUILayout.Button(label, style, GUILayout.Height(26f)))
            _category = category;
    }

    private void DrawCellInspector(float height)
    {
        GUILayout.BeginVertical(GUILayout.Height(height));
        GUILayout.Label("Cell", _miniLabelStyle);
        if (!_hasHover)
        {
            GUILayout.Label("No cell under cursor", _miniLabelStyle);
            GUILayout.EndVertical();
            return;
        }

        int tile = _draft.GetTile(_hoverCell.x, _hoverCell.y);
        string terrainName = ResolveTerrainName(tile);
        var tags = _draft.GetTagsAt(_hoverCell.x, _hoverCell.y);
        GUILayout.Label($"{_hoverCell.x},{_hoverCell.y}  terrain: {tile} {terrainName}", _miniLabelStyle);

        if (tags.Count == 0)
        {
            GUILayout.Label("tags: none", _miniLabelStyle);
            GUILayout.EndVertical();
            return;
        }

        int maxVisibleTags = Mathf.Max(1, Mathf.FloorToInt((height - 42f) / 18f));
        for (int i = 0; i < tags.Count && i < maxVisibleTags; i++)
        {
            var tag = tags[i];
            GUILayout.Label($"tag: {tag.tagID} {ResolveTagName(tag.tagID)}", _miniLabelStyle);
        }

        if (tags.Count > maxVisibleTags)
            GUILayout.Label($"+ {tags.Count - maxVisibleTags} more tags", _miniLabelStyle);
        GUILayout.EndVertical();
    }

    private void DrawColorSwatch(Color color)
    {
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = color;
        GUILayout.Box(GUIContent.none, _colorSwatchStyle, GUILayout.Width(18f), GUILayout.Height(18f));
        GUI.backgroundColor = old;
    }

    private void HandleShortcuts()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                    Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

        RebuildVisibleBrushes();
        for (int i = 0; i < Mathf.Min(9, _visibleBrushes.Count); i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                _brushIndex = _brushes.IndexOf(_visibleBrushes[i]);
        }

        if (ctrl && Input.GetKeyDown(KeyCode.S))
            SaveChanges();
        else if (ctrl && Input.GetKeyDown(KeyCode.Z))
            Undo();
        else if (ctrl && Input.GetKeyDown(KeyCode.Y))
            Redo();
        else if (Input.GetKeyDown(KeyCode.Escape))
            ExitEditor();
    }

    private void HandlePointerEdit()
    {
        if (!_hasHover || IsPointerOverEditorPanel())
        {
            _lastAppliedKey = null;
            return;
        }

        if (Input.GetMouseButton(0))
            ApplyBrush(_hoverCell);
        else if (Input.GetMouseButtonDown(1) || Input.GetMouseButton(1))
            EraseCell(_hoverCell);
        else
            _lastAppliedKey = null;
    }

    private void UpdateHover()
    {
        _hasHover = TryGetMouseGridPosition(out _hoverCell) &&
                    _hoverCell.x >= 0 && _hoverCell.x < _draft.width &&
                    _hoverCell.y >= 0 && _hoverCell.y < _draft.height;

        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        if (!_hasHover || IsPointerOverEditorPanel())
        {
            overlay.RemoveOverlay(HoverOverlayId);
            return;
        }

        _singleCell.Clear();
        _singleCell.Add(_hoverCell);
        overlay.SetOverlay(HoverOverlayId, _singleCell, new Color(0.2f, 0.85f, 1f, 0.32f), 0.032f, 100);
    }

    private bool TryGetMouseGridPosition(out Vector2Int gridPosition)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            gridPosition = default;
            return false;
        }

        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        var floorPlane = new Plane(Vector3.up, Vector3.zero);
        if (!floorPlane.Raycast(ray, out float distance))
        {
            gridPosition = default;
            return false;
        }

        Vector3 world = ray.GetPoint(distance);
        gridPosition = new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z));
        return true;
    }

    private bool IsPointerOverEditorPanel()
    {
        float width = Mathf.Min(DefaultPanelWidth, Screen.width - PanelMargin * 2f);
        return Input.mousePosition.x <= PanelMargin + width &&
               Input.mousePosition.y >= 0f &&
               Input.mousePosition.y <= Screen.height;
    }

    private void ApplyBrush(Vector2Int cell)
    {
        if (_brushes.Count == 0)
            return;

        Brush brush = _brushes[Mathf.Clamp(_brushIndex, 0, _brushes.Count - 1)];
        string applyKey = $"{cell.x}:{cell.y}:{brush.Kind}:{brush.Id}";
        if (_lastAppliedKey == applyKey)
            return;

        if (brush.Kind == BrushKind.Terrain)
        {
            if (_draft.GetTile(cell.x, cell.y) == brush.Id)
                return;

            _lastAppliedKey = applyKey;
            PushUndo();
            _draft.SetTile(cell.x, cell.y, brush.Id);
            MarkDraftChanged();
            return;
        }

        if (_draft.HasTag(cell.x, cell.y, brush.Id))
            return;

        _lastAppliedKey = applyKey;
        PushUndo();
        RemoveConflictingTags(cell, brush.Id);
        _draft.AddTag(cell.x, cell.y, brush.Id);
        MarkDraftChanged();
    }

    private void EraseCell(Vector2Int cell)
    {
        int tile = _draft.GetTile(cell.x, cell.y);
        int tagCount = _draft.GetTagsAt(cell.x, cell.y).Count;
        if (tile == 0 && tagCount == 0)
            return;

        _lastAppliedKey = $"{cell.x}:{cell.y}:erase";
        PushUndo();
        _draft.SetTile(cell.x, cell.y, 0);
        _draft.ClearTagsAt(cell.x, cell.y);
        MarkDraftChanged();
    }

    private void RemoveConflictingTags(Vector2Int cell, int newTagId)
    {
        string newTagName = _config != null ? _config.GetTagName(newTagId) : string.Empty;
        bool isTarget = IsTargetTagName(newTagName);
        bool isOccupant = !isTarget;

        if (_draft.tags == null)
            return;

        _draft.tags.RemoveAll(tag =>
        {
            if (tag.x != cell.x || tag.y != cell.y)
                return false;

            string existingName = _config != null ? _config.GetTagName(tag.tagID) : string.Empty;
            bool existingTarget = IsTargetTagName(existingName);
            return (isTarget && existingTarget) || (isOccupant && !existingTarget);
        });
    }

    private static bool IsTargetTagName(string tagName)
    {
        return !string.IsNullOrEmpty(tagName) &&
               tagName.StartsWith("Target", System.StringComparison.OrdinalIgnoreCase);
    }

    private void MarkDraftChanged()
    {
        _dirty = true;
        RebuildPreview();
    }

    private void RebuildPreview()
    {
        if (_player == null || _draft == null)
            return;

        _player.StopPlayback();
        _player.LoadLevel(_draft, _config, LevelDataSource.RuntimeRequest);
        _player.RebuildWorld();
        DisableRuntimeInput();
    }

    private void DisableRuntimeInput()
    {
        var inputReader = GetComponent<UserInputReader>();
        if (inputReader != null)
            inputReader.enabled = false;

        SpawnSystem.Instance?.StopSpawning();
        Camera.main?.GetComponent<CameraController>()?.SetFlowPaused(false);
    }

    private void PushUndo()
    {
        _undoStack.Push(Capture(_draft));
        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        _redoStack.Push(Capture(_draft));
        Restore(_draft, _undoStack.Pop());
        _dirty = true;
        RebuildPreview();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        _undoStack.Push(Capture(_draft));
        Restore(_draft, _redoStack.Pop());
        _dirty = true;
        RebuildPreview();
    }

    public void SaveChanges()
    {
        if (_source == null || _draft == null)
            return;

        _source.levelName = _draft.levelName;
        _source.SetFromMap2D(_draft.GetMap2D());
        _source.tags = CloneTags(_draft.tags);
        EditorUtility.SetDirty(_source);
        AssetDatabase.SaveAssets();
        _dirty = false;
        Debug.Log($"[Level3DEditor] Saved {_source.name}: {_source.width}x{_source.height}, tags={_source.tags.Count}");
    }

    public void DiscardChanges()
    {
        if (_source == null)
            return;

        _draft = CloneLevelData(_source);
        _undoStack.Clear();
        _redoStack.Clear();
        _dirty = false;
        RebuildPreview();
    }

    public void ExitEditor()
    {
        EditorApplication.ExitPlaymode();
    }

    private void BuildBrushes()
    {
        _brushes.Clear();
        _visibleBrushes.Clear();
        _brushes.Add(new Brush(BrushKind.Terrain, BrushCategory.Terrain, 0, "Terrain / Empty", "terrain empty 0", Color.gray));

        if (_config != null && _config.entries != null)
        {
            foreach (var entry in _config.entries)
            {
                if (entry == null || entry.tileID == 0)
                    continue;

                string tileName = entry.tileAsset != null ? entry.tileAsset.name : $"Tile {entry.tileID}";
                string label = entry.isWall ? $"Terrain / Wall / {tileName}" : $"Terrain / Floor / {tileName}";
                var category = entry.isWall ? BrushCategory.Wall : BrushCategory.Terrain;
                string search = $"{label} {entry.tileID}";
                _brushes.Add(new Brush(BrushKind.Terrain, category, entry.tileID, label, search, _config.GetTileColor(entry.tileID)));
            }
        }

        if (_config != null && _config.tagDefinitions != null)
        {
            foreach (var tag in _config.tagDefinitions)
            {
                if (tag == null || tag.tagID == 0)
                    continue;

                string label = string.IsNullOrEmpty(tag.tagName)
                    ? $"Tag / {tag.tagID}"
                    : $"Tag / {tag.tagName}";
                if (tag.entityBP != null)
                    label += $" / {tag.entityBP.name}";

                var category = ClassifyTag(tag);
                string search = $"{label} {tag.tagID} {tag.tagName} {tag.entityBP?.name}";
                _brushes.Add(new Brush(BrushKind.Tag, category, tag.tagID, label, search, tag.color));
            }
        }

        _brushIndex = Mathf.Clamp(_brushIndex, 0, Mathf.Max(0, _brushes.Count - 1));
    }

    private void RebuildVisibleBrushes()
    {
        _visibleBrushes.Clear();
        string search = (_search ?? string.Empty).Trim();
        for (int i = 0; i < _brushes.Count; i++)
        {
            Brush brush = _brushes[i];
            if (_category != BrushCategory.All && brush.Category != _category)
                continue;

            if (!string.IsNullOrEmpty(search) &&
                (brush.SearchText == null || brush.SearchText.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0))
                continue;

            _visibleBrushes.Add(brush);
        }
    }

    private static BrushCategory ClassifyTag(TileMappingConfig.TagDefinition tag)
    {
        string name = tag != null ? tag.tagName ?? string.Empty : string.Empty;
        if (name.StartsWith("Target", System.StringComparison.OrdinalIgnoreCase))
            return BrushCategory.Target;
        if (name.StartsWith("Wall", System.StringComparison.OrdinalIgnoreCase))
            return BrushCategory.Wall;
        if (name.StartsWith("Player", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Box", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Enemy", System.StringComparison.OrdinalIgnoreCase) ||
            tag?.entityBP != null)
            return BrushCategory.Actor;
        return BrushCategory.Other;
    }

    private string ResolveTerrainName(int tileId)
    {
        if (tileId == 0)
            return "Empty";
        var tile = _config != null ? _config.GetTileAsset(tileId) : null;
        return tile != null ? tile.name : "Unknown";
    }

    private string ResolveTagName(int tagId)
    {
        if (_config == null)
            return "Unknown";
        string tagName = _config.GetTagName(tagId);
        var bp = _config.GetTagEntityBP(tagId);
        return bp != null ? $"{tagName} / {bp.name}" : tagName;
    }

    private static LevelData CloneLevelData(LevelData source)
    {
        var clone = ScriptableObject.CreateInstance<LevelData>();
        if (source == null)
            return clone;

        clone.levelName = source.levelName;
        clone.width = source.width;
        clone.height = source.height;
        clone.tiles = source.tiles != null ? (int[])source.tiles.Clone() : null;
        clone.tags = CloneTags(source.tags);
        clone.EnsureInitialized(Mathf.Max(1, clone.width), Mathf.Max(1, clone.height));
        return clone;
    }

    private static List<LevelTagEntry> CloneTags(List<LevelTagEntry> source)
    {
        var result = new List<LevelTagEntry>();
        if (source == null)
            return result;

        foreach (var tag in source)
        {
            if (tag == null)
                continue;

            result.Add(new LevelTagEntry { tagID = tag.tagID, x = tag.x, y = tag.y });
        }

        return result;
    }

    private static Snapshot Capture(LevelData level)
    {
        return new Snapshot
        {
            Width = level.width,
            Height = level.height,
            Tiles = level.tiles != null ? (int[])level.tiles.Clone() : null,
            Tags = CloneTags(level.tags)
        };
    }

    private static void Restore(LevelData level, Snapshot snapshot)
    {
        level.width = snapshot.Width;
        level.height = snapshot.Height;
        level.tiles = snapshot.Tiles != null ? (int[])snapshot.Tiles.Clone() : null;
        level.tags = CloneTags(snapshot.Tags);
    }

    private void EnsureStyles()
    {
        if (_panelStyle != null)
            return;

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(12, 12, 12, 12),
            alignment = TextAnchor.UpperLeft
        };
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        _miniLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(0.82f, 0.86f, 0.9f, 1f) }
        };
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12
        };
        _selectedButtonStyle = new GUIStyle(_buttonStyle)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 0.9f, 1f, 1f) }
        };
        _colorSwatchStyle = new GUIStyle(GUI.skin.box)
        {
            margin = new RectOffset(0, 6, 5, 0)
        };
    }

    private readonly struct Brush
    {
        public readonly BrushKind Kind;
        public readonly BrushCategory Category;
        public readonly int Id;
        public readonly string Label;
        public readonly string SearchText;
        public readonly Color Color;

        public Brush(BrushKind kind, BrushCategory category, int id, string label, string searchText, Color color)
        {
            Kind = kind;
            Category = category;
            Id = id;
            Label = label;
            SearchText = searchText;
            Color = color;
        }
    }

    private enum BrushKind
    {
        Terrain,
        Tag
    }

    private enum BrushCategory
    {
        All,
        Terrain,
        Actor,
        Target,
        Wall,
        Other
    }

    private struct Snapshot
    {
        public int Width;
        public int Height;
        public int[] Tiles;
        public List<LevelTagEntry> Tags;
    }
}
#endif
