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
    private const string SelectedOverlayId = "level_3d_editor_selected_tag";
    private const int DefaultPanelWidth = 330;
    private const int SelectedPanelWidth = 330;
    private const float PanelMargin = 12f;
    private const float ToolbarHeight = 68f;
    private const float CellInspectorHeight = 92f;
    private const float FilterHeight = 116f;
    private const float FooterHeight = 36f;
    private const float SectionGap = 8f;
    private const int MinEditCanvasSize = 64;

    private readonly List<Brush> _brushes = new();
    private readonly List<Brush> _visibleBrushes = new();
    private readonly Stack<Snapshot> _undoStack = new();
    private readonly Stack<Snapshot> _redoStack = new();
    private readonly List<Vector2Int> _singleCell = new(1);
    private readonly List<Vector2Int> _selectedCell = new(1);
    private readonly HashSet<Vector2Int> _rightEraseStrokeTagClearedCells = new();

    private LevelPlayer _player;
    private LevelData _source;
    private LevelData _draft;
    private TileMappingConfig _config;
    private int _brushIndex;
    private Vector2Int _hoverCell;
    private bool _hasHover;
    private bool _dirty;
    private bool _cameraFocusedOnEntry;
    private BrushCategory _category = BrushCategory.All;
    private string _search = string.Empty;
    private Vector2 _paletteScroll;
    private string _lastAppliedKey;
    private bool _hasSelectedTag;
    private Vector2Int _selectedTagCell;
    private int _selectedTagId;
    private int _selectedTagCycleIndex = -1;
    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _selectedButtonStyle;
    private GUIStyle _miniLabelStyle;
    private GUIStyle _selectedPanelTitleStyle;
    private GUIStyle _selectedPanelLabelStyle;
    private GUIStyle _selectedPanelValueStyle;
    private GUIStyle _colorSwatchStyle;

    public void Configure(LevelPlayer player, LevelData source, TileMappingConfig config)
    {
        bool targetChanged = _source != source;
        _player = player;
        _source = source;
        _config = config;
        _config?.RebuildCache();

        _draft = CreateEditCanvas(source);
        _dirty = false;
        _lastAppliedKey = null;
        _undoStack.Clear();
        _redoStack.Clear();
        _rightEraseStrokeTagClearedCells.Clear();
        ClearSelectedTag();
        if (targetChanged)
            _cameraFocusedOnEntry = false;

        BuildBrushes();
        DisableRuntimeInput();
        RebuildPreview();
        if (!_cameraFocusedOnEntry)
        {
            FocusCameraOnContent();
            _cameraFocusedOnEntry = true;
        }
    }

    public LevelData SourceLevel => _source;
    public bool HasUnsavedChanges => _dirty;

    private void OnDisable()
    {
        GridOverlayDrawSystem.Instance?.RemoveOverlay(HoverOverlayId);
        GridOverlayDrawSystem.Instance?.RemoveOverlay(SelectedOverlayId);
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
        GUILayout.Label("LMB paint   Alt+LMB select tag   RMB clear tags; terrain only with Terrain brush   1-9 quick select   Ctrl+Z/Y undo/redo   Ctrl+S save   Esc exit", _miniLabelStyle);
        GUILayout.EndArea();

        DrawSelectedTagPanel();
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
        string boundsText = TryGetContentBounds(_draft, out var bounds) ? $"{bounds.width}x{bounds.height}" : "empty";
        return $"{levelName}   canvas {_draft.width}x{_draft.height}   content {boundsText}   ECS {entityCount}   {cellText}\n{dirtyText}   brush: {brushText}";
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
        if (!Input.GetMouseButton(1))
            _rightEraseStrokeTagClearedCells.Clear();
        else if (Input.GetMouseButtonDown(1))
            _rightEraseStrokeTagClearedCells.Clear();

        if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            _lastAppliedKey = null;

        if (!_hasHover || IsPointerOverEditorPanel())
        {
            return;
        }

        if (Input.GetMouseButtonDown(0) && IsAltHeld())
        {
            _lastAppliedKey = null;
            _rightEraseStrokeTagClearedCells.Clear();
            SelectNextTagAt(_hoverCell);
        }
        else if (Input.GetMouseButton(0) && !IsAltHeld())
        {
            _rightEraseStrokeTagClearedCells.Clear();
            ApplyBrush(_hoverCell);
        }
        else if (Input.GetMouseButtonDown(1) || Input.GetMouseButton(1))
            EraseCell(_hoverCell);
        else
        {
            _lastAppliedKey = null;
            _rightEraseStrokeTagClearedCells.Clear();
        }
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
        UpdateSelectedOverlay();
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
        if (Input.mousePosition.y < 0f || Input.mousePosition.y > Screen.height)
            return false;

        bool overLeftPanel = Input.mousePosition.x <= PanelMargin + width;
        Vector2 guiMouse = new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        bool overRightPanel = _hasSelectedTag && GetSelectedPanelRect().Contains(guiMouse);
        return overLeftPanel || overRightPanel;
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
        {
            _lastAppliedKey = applyKey;
            return;
        }

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

        string eraseKey = $"{cell.x}:{cell.y}:erase";
        if (tagCount > 0)
        {
            _lastAppliedKey = eraseKey;
            _rightEraseStrokeTagClearedCells.Add(cell);
            PushUndo();
            _draft.ClearTagsAt(cell.x, cell.y);
            ClearSelectedTagIfCell(cell);
            MarkDraftChanged();
            return;
        }

        if (!IsTerrainBrushSelected() || _rightEraseStrokeTagClearedCells.Contains(cell))
            return;

        _lastAppliedKey = eraseKey;
        PushUndo();
        _draft.SetTile(cell.x, cell.y, 0);
        MarkDraftChanged();
    }

    private bool IsTerrainBrushSelected()
    {
        if (_brushes.Count == 0)
            return false;

        Brush brush = _brushes[Mathf.Clamp(_brushIndex, 0, _brushes.Count - 1)];
        return brush.Kind == BrushKind.Terrain;
    }

    private void SelectTag(Vector2Int cell, int tagId)
    {
        _hasSelectedTag = true;
        _selectedTagCell = cell;
        _selectedTagId = tagId;
        _selectedTagCycleIndex = FindTagCycleIndex(cell, tagId);
        UpdateSelectedOverlay();
    }

    private void SelectNextTagAt(Vector2Int cell)
    {
        var candidates = GetSelectableTagsAt(cell);
        if (candidates.Count == 0)
        {
            ClearSelectedTag();
            return;
        }

        int nextIndex = 0;
        if (_hasSelectedTag && _selectedTagCell == cell)
            nextIndex = (_selectedTagCycleIndex + 1 + candidates.Count) % candidates.Count;

        var selected = candidates[nextIndex];
        _hasSelectedTag = true;
        _selectedTagCell = cell;
        _selectedTagId = selected.tagID;
        _selectedTagCycleIndex = nextIndex;
        UpdateSelectedOverlay();
    }

    private List<LevelTagEntry> GetSelectableTagsAt(Vector2Int cell)
    {
        var result = new List<LevelTagEntry>();
        if (_draft?.tags == null)
            return result;

        for (int pass = 0; pass < 2; pass++)
        {
            bool wantTarget = pass == 1;
            for (int i = 0; i < _draft.tags.Count; i++)
            {
                var tag = _draft.tags[i];
                if (tag == null || tag.x != cell.x || tag.y != cell.y)
                    continue;

                if (IsTargetTag(tag.tagID) == wantTarget)
                    result.Add(tag);
            }
        }

        return result;
    }

    private int FindTagCycleIndex(Vector2Int cell, int tagId)
    {
        var candidates = GetSelectableTagsAt(cell);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].tagID == tagId)
                return i;
        }

        return -1;
    }

    private void ClearSelectedTag()
    {
        _hasSelectedTag = false;
        _selectedTagCell = default;
        _selectedTagId = 0;
        _selectedTagCycleIndex = -1;
        GridOverlayDrawSystem.Instance?.RemoveOverlay(SelectedOverlayId);
    }

    private void ClearSelectedTagIfCell(Vector2Int cell)
    {
        if (_hasSelectedTag && _selectedTagCell == cell)
            ClearSelectedTag();
    }

    private bool TryGetSelectedTag(out LevelTagEntry selectedTag)
    {
        selectedTag = null;
        if (!_hasSelectedTag || _draft?.tags == null)
            return false;

        for (int i = 0; i < _draft.tags.Count; i++)
        {
            var tag = _draft.tags[i];
            if (tag == null)
                continue;

            if (tag.x == _selectedTagCell.x &&
                tag.y == _selectedTagCell.y &&
                tag.tagID == _selectedTagId)
            {
                selectedTag = tag;
                return true;
            }
        }

        return false;
    }

    private bool IsTargetTag(int tagId)
    {
        string tagName = _config != null ? _config.GetTagName(tagId) : string.Empty;
        return IsTargetTagName(tagName);
    }

    private static bool IsAltHeld()
    {
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }

    private void UpdateSelectedOverlay()
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        if (!TryGetSelectedTag(out _))
        {
            if (_hasSelectedTag)
                ClearSelectedTag();
            else
                overlay.RemoveOverlay(SelectedOverlayId);
            return;
        }

        _selectedCell.Clear();
        _selectedCell.Add(_selectedTagCell);
        overlay.SetOverlay(
            SelectedOverlayId,
            _selectedCell,
            GridOverlayStyle.SelectionRing,
            new Color(1f, 0.86f, 0.22f, 0.95f),
            0.042f,
            140);
    }

    private void DrawSelectedTagPanel()
    {
        if (!_hasSelectedTag)
            return;

        if (!TryGetSelectedTag(out var selectedTag))
        {
            ClearSelectedTag();
            return;
        }

        EnsureStyles();

        var rect = GetSelectedPanelRect();

        GUILayout.BeginArea(rect, GUIContent.none, _panelStyle);
        GUILayout.Label("Selected Tag", _selectedPanelTitleStyle);
        GUILayout.Label($"{selectedTag.x},{selectedTag.y}  tag: {selectedTag.tagID} {ResolveTagName(selectedTag.tagID)}", _selectedPanelLabelStyle);
        GUILayout.Space(8f);

        EntityBP defaultBP = _config != null ? _config.GetTagEntityBP(selectedTag.tagID) : null;
        bool usesOverride = selectedTag.entityBPOverride != null;
        GUILayout.Label(usesOverride ? "当前使用专有 BP" : "当前使用默认值", _selectedPanelValueStyle);
        GUILayout.Label($"默认 BP: {FormatBPName(defaultBP)}", _selectedPanelLabelStyle);
        if (!usesOverride && defaultBP == null)
            GUILayout.Label("没有默认 BP；可以直接创建专有 BP。", _selectedPanelLabelStyle);

        EditorGUI.BeginChangeCheck();
        var overrideBP = (EntityBP)EditorGUILayout.ObjectField(
            "专有 BP",
            selectedTag.entityBPOverride,
            typeof(EntityBP),
            false);
        if (EditorGUI.EndChangeCheck())
            SetSelectedTagOverride(selectedTag, overrideBP);

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(selectedTag.entityBPOverride == null))
        {
            if (GUILayout.Button("使用默认值", _buttonStyle, GUILayout.Height(30f)))
                SetSelectedTagOverride(selectedTag, null);
        }

        if (GUILayout.Button("创建专有 BP", _buttonStyle, GUILayout.Height(30f)))
        {
            var clone = CreateOverrideBPAsset(selectedTag, defaultBP);
            if (clone != null)
                SetSelectedTagOverride(selectedTag, clone);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(selectedTag.entityBPOverride == null && defaultBP == null))
        {
            if (GUILayout.Button("定位 BP", _buttonStyle, GUILayout.Height(30f)))
            {
                var bp = selectedTag.entityBPOverride != null ? selectedTag.entityBPOverride : defaultBP;
                EditorGUIUtility.PingObject(bp);
                Selection.activeObject = bp;
            }
        }

        if (GUILayout.Button("取消选择", _buttonStyle, GUILayout.Height(30f)))
            ClearSelectedTag();
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        var resolved = selectedTag.ResolveEntityBP(_config);
        DrawBPStats("默认参数", defaultBP);
        if (selectedTag.entityBPOverride != null)
            DrawOverrideBPEditor(selectedTag.entityBPOverride);
        if (selectedTag.entityBPOverride != null)
        {
            GUILayout.Space(4f);
            DrawBPStats("最终使用", resolved);
        }

        GUILayout.EndArea();
    }

    private static Rect GetSelectedPanelRect()
    {
        float width = Mathf.Min(SelectedPanelWidth, Screen.width - PanelMargin * 2f);
        float height = Mathf.Min(440f, Mathf.Max(260f, Screen.height - PanelMargin * 2f));
        float x = Mathf.Max(PanelMargin, Screen.width - PanelMargin - width);
        float y = Mathf.Max(PanelMargin, Screen.height - PanelMargin - height);
        return new Rect(x, y, width, height);
    }

    private void DrawBPStats(string title, EntityBP bp)
    {
        GUILayout.Label($"{title}: {FormatBPName(bp)}", _selectedPanelValueStyle);
        if (bp == null)
        {
            GUILayout.Label("Health -   Attack -", _selectedPanelLabelStyle);
            GUILayout.Label("Spawn Interval -   Spawn BP <none>", _selectedPanelLabelStyle);
            return;
        }

        GUILayout.Label($"Health {bp.health}   Attack {bp.attack}", _selectedPanelLabelStyle);
        GUILayout.Label($"Spawn Interval {bp.spawnInterval}   Spawn BP {FormatBPName(bp.spawnEntityBP)}", _selectedPanelLabelStyle);
    }

    private void DrawOverrideBPEditor(EntityBP bp)
    {
        if (bp == null)
            return;

        GUILayout.Space(6f);
        GUILayout.Label("专有 BP 参数", _selectedPanelValueStyle);

        bool changed = false;
        int health = bp.health;
        int attack = bp.attack;
        int spawnInterval = bp.spawnInterval;
        changed |= DrawIntTextField("Health", bp.health, 1, out health);
        changed |= DrawIntTextField("Attack", bp.attack, 0, out attack);
        changed |= DrawIntTextField("Spawn Interval", bp.spawnInterval, 0, out spawnInterval);

        EditorGUI.BeginChangeCheck();
        var spawnBP = (EntityBP)EditorGUILayout.ObjectField("Spawn BP", bp.spawnEntityBP, typeof(EntityBP), false);
        changed |= EditorGUI.EndChangeCheck();
        if (!changed)
            return;

        UnityEditor.Undo.RecordObject(bp, "Edit Level Tag Override BP");
        bp.health = Mathf.Max(1, health);
        bp.attack = Mathf.Max(0, attack);
        bp.spawnInterval = Mathf.Max(0, spawnInterval);
        bp.spawnEntityBP = spawnBP;
        EditorUtility.SetDirty(bp);
        AssetDatabase.SaveAssets();
        MarkDraftChanged();
    }

    private bool DrawIntTextField(string label, int currentValue, int minValue, out int value)
    {
        value = currentValue;

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _selectedPanelLabelStyle, GUILayout.Width(110f));
        string text = GUILayout.TextField(currentValue.ToString(), GUILayout.Height(22f));
        GUILayout.EndHorizontal();

        if (text == currentValue.ToString())
            return false;

        if (!int.TryParse(text, out int parsed))
            return false;

        value = Mathf.Max(minValue, parsed);
        return value != currentValue;
    }

    private void SetSelectedTagOverride(LevelTagEntry selectedTag, EntityBP overrideBP)
    {
        if (selectedTag == null || selectedTag.entityBPOverride == overrideBP)
            return;

        PushUndo();
        selectedTag.entityBPOverride = overrideBP;
        MarkDraftChanged();
        UpdateSelectedOverlay();
    }

    private EntityBP CreateOverrideBPAsset(LevelTagEntry selectedTag, EntityBP defaultBP)
    {
        if (selectedTag == null)
            return null;

        string levelName = SanitizeAssetName(_source != null ? _source.name : "Level");
        string sourcePath = _source != null ? AssetDatabase.GetAssetPath(_source) : string.Empty;
        string sourceFolder = string.IsNullOrEmpty(sourcePath)
            ? "Assets"
            : System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(sourceFolder))
            sourceFolder = "Assets";

        string levelFolder = $"{sourceFolder}/{levelName}_EntityBPOverrides";
        EnsureAssetFolder(levelFolder);

        string tagName = SanitizeAssetName(_config != null ? _config.GetTagName(selectedTag.tagID) : $"Tag{selectedTag.tagID}");
        string fileName = $"{levelName}_Tag{selectedTag.tagID}_{tagName}_X{selectedTag.x}_Y{selectedTag.y}_EntityBPOverride.asset";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{levelFolder}/{fileName}");

        var clone = ScriptableObject.CreateInstance<EntityBP>();
        if (defaultBP != null)
            EditorUtility.CopySerialized(defaultBP, clone);
        clone.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        AssetDatabase.CreateAsset(clone, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(clone);
        return clone;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            return;

        EnsureAssetFolder(parent);
        if (!AssetDatabase.IsValidFolder(folderPath))
            AssetDatabase.CreateFolder(parent, folderName);
    }

    private static string SanitizeAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unnamed";

        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            for (int j = 0; j < invalid.Length; j++)
            {
                if (chars[i] == invalid[j])
                {
                    chars[i] = '_';
                    break;
                }
            }
        }

        return new string(chars).Replace(' ', '_');
    }

    private static string FormatBPName(EntityBP bp)
    {
        return bp != null ? bp.name : "<none>";
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

    private void FocusCameraOnContent()
    {
        var cameraController = Camera.main != null ? Camera.main.GetComponent<CameraController>() : null;
        if (cameraController == null || _draft == null)
            return;

        if (TryGetContentBounds(_draft, out var bounds))
        {
            float cellSize = _player != null ? _player.cellSize : 1f;
            Vector2 center = new(
                (bounds.xMin + bounds.xMax + 1) * 0.5f * cellSize,
                (bounds.yMin + bounds.yMax + 1) * 0.5f * cellSize);
            Vector2 size = new(bounds.width * cellSize, bounds.height * cellSize);
            cameraController.SetWorldBounds(center, size);
            cameraController.FocusOn(center);
            return;
        }

        float fallbackCellSize = _player != null ? _player.cellSize : 1f;
        Vector2 fallbackCenter = new(_draft.width * 0.5f * fallbackCellSize, _draft.height * 0.5f * fallbackCellSize);
        cameraController.SetWorldBounds(fallbackCenter, new Vector2(_draft.width * fallbackCellSize, _draft.height * fallbackCellSize));
        cameraController.FocusOn(fallbackCenter);
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

        WriteCroppedDraftToSource();
        EditorUtility.SetDirty(_source);
        AssetDatabase.SaveAssets();
        _dirty = false;
        Debug.Log($"[Level3DEditor] Saved {_source.name}: {_source.width}x{_source.height}, tags={_source.tags.Count}");
    }

    public void DiscardChanges()
    {
        if (_source == null)
            return;

        _draft = CreateEditCanvas(_source);
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

    private static LevelData CreateEditCanvas(LevelData source)
    {
        var canvas = ScriptableObject.CreateInstance<LevelData>();
        if (source == null)
        {
            canvas.levelName = "Draft";
            canvas.width = MinEditCanvasSize;
            canvas.height = MinEditCanvasSize;
            canvas.tiles = new int[canvas.width * canvas.height];
            canvas.tags = new List<LevelTagEntry>();
            return canvas;
        }

        int sourceWidth = Mathf.Max(1, source.width);
        int sourceHeight = Mathf.Max(1, source.height);
        int width = Mathf.Max(MinEditCanvasSize, NextPowerOfTwoAtLeast(sourceWidth * 3));
        int height = Mathf.Max(MinEditCanvasSize, NextPowerOfTwoAtLeast(sourceHeight * 3));
        int offsetX = Mathf.Max(0, (width - sourceWidth) / 2);
        int offsetY = Mathf.Max(0, (height - sourceHeight) / 2);

        canvas.levelName = source.levelName;
        canvas.width = width;
        canvas.height = height;
        canvas.tiles = new int[width * height];
        canvas.tags = new List<LevelTagEntry>();

        source.EnsureInitialized(sourceWidth, sourceHeight);
        for (int y = 0; y < sourceHeight; y++)
        {
            for (int x = 0; x < sourceWidth; x++)
                canvas.SetTile(x + offsetX, y + offsetY, source.GetTile(x, y));
        }

        if (source.tags != null)
        {
            foreach (var tag in source.tags)
            {
                if (tag == null)
                    continue;
                canvas.tags.Add(new LevelTagEntry
                {
                    tagID = tag.tagID,
                    x = tag.x + offsetX,
                    y = tag.y + offsetY,
                    entityBPOverride = tag.entityBPOverride
                });
            }
        }

        return canvas;
    }

    private void WriteCroppedDraftToSource()
    {
        if (_source == null || _draft == null)
            return;

        if (!TryGetContentBounds(_draft, out var bounds))
        {
            _source.levelName = _draft.levelName;
            _source.width = 1;
            _source.height = 1;
            _source.tiles = new int[1];
            _source.tags = new List<LevelTagEntry>();
            return;
        }

        int[] croppedTiles = new int[bounds.width * bounds.height];
        for (int y = 0; y < bounds.height; y++)
        {
            for (int x = 0; x < bounds.width; x++)
                croppedTiles[y * bounds.width + x] = _draft.GetTile(bounds.xMin + x, bounds.yMin + y);
        }

        var croppedTags = new List<LevelTagEntry>();
        if (_draft.tags != null)
        {
            foreach (var tag in _draft.tags)
            {
                if (tag == null)
                    continue;
                if (!bounds.Contains(tag.x, tag.y))
                    continue;
                croppedTags.Add(new LevelTagEntry
                {
                    tagID = tag.tagID,
                    x = tag.x - bounds.xMin,
                    y = tag.y - bounds.yMin,
                    entityBPOverride = tag.entityBPOverride
                });
            }
        }

        _source.levelName = _draft.levelName;
        _source.width = bounds.width;
        _source.height = bounds.height;
        _source.tiles = croppedTiles;
        _source.tags = croppedTags;
    }

    private static bool TryGetContentBounds(LevelData level, out IntBounds bounds)
    {
        bounds = default;
        if (level == null)
            return false;

        bool hasContent = false;
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        if (level.tiles != null)
        {
            for (int y = 0; y < level.height; y++)
            {
                for (int x = 0; x < level.width; x++)
                {
                    if (level.GetTile(x, y) == 0)
                        continue;
                    IncludePoint(x, y, ref hasContent, ref minX, ref minY, ref maxX, ref maxY);
                }
            }
        }

        if (level.tags != null)
        {
            foreach (var tag in level.tags)
            {
                if (tag == null)
                    continue;
                if (tag.x < 0 || tag.x >= level.width || tag.y < 0 || tag.y >= level.height)
                    continue;
                IncludePoint(tag.x, tag.y, ref hasContent, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        if (!hasContent)
            return false;

        bounds = new IntBounds(minX, minY, maxX, maxY);
        return true;
    }

    private static void IncludePoint(
        int x,
        int y,
        ref bool hasContent,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY)
    {
        hasContent = true;
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    private static int NextPowerOfTwoAtLeast(int value)
    {
        int result = 1;
        int target = Mathf.Max(1, value);
        while (result < target)
            result <<= 1;
        return result;
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

            result.Add(tag.Clone());
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
        _selectedPanelTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        _selectedPanelLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            normal = { textColor = new Color(0.86f, 0.9f, 0.94f, 1f) }
        };
        _selectedPanelValueStyle = new GUIStyle(_selectedPanelLabelStyle)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.88f, 0.28f, 1f) }
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

    private readonly struct IntBounds
    {
        public readonly int xMin;
        public readonly int yMin;
        public readonly int xMax;
        public readonly int yMax;
        public int width => xMax - xMin + 1;
        public int height => yMax - yMin + 1;

        public IntBounds(int xMin, int yMin, int xMax, int yMax)
        {
            this.xMin = xMin;
            this.yMin = yMin;
            this.xMax = xMax;
            this.yMax = yMax;
        }

        public bool Contains(int x, int y)
        {
            return x >= xMin && x <= xMax && y >= yMin && y <= yMax;
        }
    }
}
#endif
