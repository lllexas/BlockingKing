using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector.Editor;

/// <summary>
/// LevelData 的自定义 Odin Inspector：
/// 1. Inspector 网格预览 + Tag 标记
/// 2. Project 窗口缩略图预览
/// 3. "在 Tilemap 编辑器中打开" 按钮
/// </summary>
[CustomEditor(typeof(LevelData))]
public class LevelDataInspector : OdinEditor
{
    private const int DefaultNewLevelWidth = 10;
    private const int DefaultNewLevelHeight = 10;

    private LevelData _data;
    private TileMappingConfig _config;

    protected override void OnEnable()
    {
        base.OnEnable();
        _data = target as LevelData;
        _config = FindConfig();
    }

    // ─────────── Project 窗口预览 ───────────

    public override bool HasPreviewGUI() => _data != null && _data.tiles != null && _data.tiles.Length > 0;

    public override GUIContent GetPreviewTitle()
    {
        return new GUIContent(_data?.levelName ?? "LevelData");
    }

    public override void OnPreviewGUI(Rect r, GUIStyle background)
    {
        if (_data == null || _data.width <= 0 || _data.height <= 0) return;

        // 填满可用区域
        float fitSize = Mathf.Min(r.width / _data.width, r.height / _data.height);
        float gridW = _data.width * fitSize;
        float gridH = _data.height * fitSize;
        float ox = r.x + (r.width - gridW) / 2f;
        float oy = r.y + (r.height - gridH) / 2f;

        for (int y = 0; y < _data.height; y++)
        {
            for (int x = 0; x < _data.width; x++)
            {
                int id = _data.GetTile(x, y);
                Rect cr = new Rect(
                    ox + x * fitSize,
                    oy + (_data.height - 1 - y) * fitSize,
                    fitSize, fitSize);

                if (_config != null)
                {
                    TileBase tile = _config.GetTileAsset(id);
                    DrawTileInRect(cr, tile);
                }
                else
                {
                    EditorGUI.DrawRect(cr, GetTerrainColor(id, null));
                }
            }
        }

        // Tag 标记
        if (_data.tags != null && _config != null)
        {
            foreach (var tag in _data.tags)
            {
                if (tag.x < 0 || tag.x >= _data.width || tag.y < 0 || tag.y >= _data.height) continue;
                Rect cr = new Rect(
                    ox + tag.x * fitSize,
                    oy + (_data.height - 1 - tag.y) * fitSize,
                    fitSize, fitSize);
                TileBase tagTile = _config.GetTagTile(tag.tagID);
                if (tagTile != null)
                    DrawTileInRect(cr, tagTile, 0.7f);
            }
        }
    }

    public override void OnInspectorGUI()
    {
        Tree.Draw();

        if (_data == null) return;

        EditorGUILayout.Space(10);

        // ────── Tag 统计 ──────
        if (_config == null) _config = FindConfig();
        if (_data.tags != null && _data.tags.Count > 0)
        {
            string summary = "";
            var counted = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var t in _data.tags)
            {
                counted.TryGetValue(t.tagID, out int c);
                counted[t.tagID] = c + 1;
            }
            foreach (var kv in counted)
            {
                string name = _config != null ? _config.GetTagName(kv.Key) : $"Tag[{kv.Key}]";
                summary += $"{name}: {kv.Value}  ";
            }
            EditorGUILayout.HelpBox($"Tag: {summary}", MessageType.Info);
        }

        // ────── 操作按钮 ──────
        EditorGUILayout.LabelField("编辑器操作", EditorStyles.boldLabel);
        Draw3DEditorRuntimeControls();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Tilemap 编辑", GUILayout.Height(30)))
            OpenTilemapEditor();

        if (GUILayout.Button("3D 编辑", GUILayout.Height(30)))
            Open3DEditor();

        GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
        if (GUILayout.Button("快速播放", GUILayout.Height(30)))
            QuickPlay();
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // ────── 地图预览 ──────
        if (_data.tiles == null || _data.tiles.Length == 0) return;
        if (_data.width <= 0 || _data.height <= 0) return;

        EditorGUILayout.LabelField("关卡地图预览", EditorStyles.boldLabel);

        // 用实际 Tile Sprite 绘制，而非色块
        float maxWidth = EditorGUIUtility.currentViewWidth - 40f;
        float cellSize = Mathf.Clamp(maxWidth / _data.width, 8f, 48f);
        float gridW = _data.width * cellSize;
        float gridH = _data.height * cellSize;

        Rect gridRect = GUILayoutUtility.GetRect(gridW + 4f, gridH + 4f);
        Rect gridArea = gridRect; gridArea.width -= 4f; gridArea.height -= 4f;

        // 先画网格线
        for (int y = 0; y <= _data.height; y++)
            EditorGUI.DrawRect(new Rect(gridArea.x, gridArea.y + y * cellSize, gridW, 1), Color.gray * 0.5f);
        for (int x = 0; x <= _data.width; x++)
            EditorGUI.DrawRect(new Rect(gridArea.x + x * cellSize, gridArea.y, 1, gridH), Color.gray * 0.5f);

        for (int y = 0; y < _data.height; y++)
        {
            for (int x = 0; x < _data.width; x++)
            {
                int tileId = _data.GetTile(x, y);
                Rect cr = new Rect(
                    gridArea.x + x * cellSize + 1,
                    gridArea.y + (_data.height - 1 - y) * cellSize + 1,
                    cellSize - 1, cellSize - 1);

                // 绘制 Tile Sprite
                if (_config != null)
                {
                    TileBase tile = _config.GetTileAsset(tileId);
                    DrawTileInRect(cr, tile);
                }
                else
                {
                    EditorGUI.DrawRect(cr, GetTerrainColor(tileId, null));
                }
            }
        }

        // Tag 覆盖：用 Tag Tile 的 Sprite 画半透明叠加
        if (_data.tags != null && _config != null)
        {
            foreach (var tag in _data.tags)
            {
                if (tag.x < 0 || tag.x >= _data.width || tag.y < 0 || tag.y >= _data.height) continue;
                Rect cr = new Rect(
                    gridArea.x + tag.x * cellSize + 1,
                    gridArea.y + (_data.height - 1 - tag.y) * cellSize + 1,
                    cellSize - 1, cellSize - 1);
                TileBase tagTile = _config.GetTagTile(tag.tagID);
                if (tagTile != null)
                    DrawTileInRect(cr, tagTile, 0.7f);
            }
        }
    }

    private void Draw3DEditorRuntimeControls()
    {
        if (!EditorApplication.isPlaying)
            return;

        var controller = Object.FindObjectOfType<Level3DEditorController>();
        if (controller == null || controller.SourceLevel != _data)
            return;

        EditorGUILayout.HelpBox(
            controller.HasUnsavedChanges ? "3D 编辑中：有未保存修改" : "3D 编辑中：没有未保存修改",
            controller.HasUnsavedChanges ? MessageType.Warning : MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.3f, 0.75f, 0.35f);
        if (GUILayout.Button("保存 3D 修改", GUILayout.Height(30)))
            controller.SaveChanges();

        GUI.backgroundColor = new Color(0.85f, 0.55f, 0.25f);
        if (GUILayout.Button("放弃 3D 修改", GUILayout.Height(30)))
            controller.DiscardChanges();

        GUI.backgroundColor = new Color(0.75f, 0.3f, 0.3f);
        if (GUILayout.Button("退出 3D 编辑", GUILayout.Height(30)))
            controller.ExitEditor();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawTileInRect(Rect r, TileBase tile, float alpha = 1f)
    {
        Sprite sp = GetSprite(tile);
        if (sp == null || sp.texture == null) return;

        var oldColor = GUI.color;
        GUI.color = new Color(1, 1, 1, alpha);
        Rect texRect = sp.textureRect;
        Rect uv = new Rect(
            texRect.x / sp.texture.width,
            texRect.y / sp.texture.height,
            texRect.width / sp.texture.width,
            texRect.height / sp.texture.height);
        GUI.DrawTextureWithTexCoords(r, sp.texture, uv);
        GUI.color = oldColor;
    }

    private static Sprite GetSprite(TileBase tile)
    {
        if (tile == null) return null;
        if (tile is Tile t && t.sprite != null) return t.sprite;
        var prop = tile.GetType().GetProperty("sprite",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(tile) as Sprite;
    }

    // ─────────── 场景切换 ───────────

    private void OpenTilemapEditor()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_data));
        if (string.IsNullOrEmpty(guid))
        {
            EditorUtility.DisplayDialog("错误", "请先保存 LevelData SO 到磁盘。", "确定");
            return;
        }

        EnsureEditableLevelData();

        var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
        var originScene = EditorSceneManager.GetActiveScene();
        if (!originScene.IsValid() || string.IsNullOrEmpty(originScene.path))
        {
            EditorUtility.DisplayDialog("错误", "当前没有活动场景。", "确定");
            return;
        }

        LevelTilemapSessionBridge.SetSession(guid, originScene.path, sceneSetup);

        string scenePath = TilemapEditorSceneUtility.EnsureSceneExists();
        var editorScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        if (!editorScene.IsValid())
        {
            LevelTilemapSessionBridge.ClearSession();
            EditorUtility.DisplayDialog("错误", "无法加载 Tilemap 编辑场景。", "确定");
            return;
        }

        var ensuredEditor = TilemapEditorSceneUtility.EnsureSceneObjects(editorScene);
        EditorSceneManager.SetActiveScene(editorScene);

        foreach (var root in editorScene.GetRootGameObjects())
        {
            var editor = root.GetComponentInChildren<LevelTilemapEditor>(true);
            if (editor != null)
            {
                editor.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
                break;
            }
        }

        ensuredEditor?.LoadSession();

        EditorSceneManager.CloseScene(originScene, false);
    }

    private void EnsureEditableLevelData()
    {
        if (_data == null) return;

        if (_config == null)
            _config = FindConfig();

        int defaultTileId = GetDefaultFloorTileId();
        if (_data.EnsureInitialized(DefaultNewLevelWidth, DefaultNewLevelHeight, defaultTileId))
        {
            if (string.IsNullOrEmpty(_data.levelName))
                _data.levelName = _data.name;

            EditorUtility.SetDirty(_data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LevelDataInspector] 初始化空 LevelData: {_data.name}, {_data.width}x{_data.height}");
        }
    }

    private int GetDefaultFloorTileId()
    {
        if (_config == null || _config.entries == null)
            return 0;

        foreach (var entry in _config.entries)
        {
            if (entry != null && !entry.isWall && entry.tileID != 0)
                return entry.tileID;
        }

        return 0;
    }

    // ─────────── 颜色 ───────────

    private TileMappingConfig FindConfig()
    {
        var guids = AssetDatabase.FindAssets("t:TileMappingConfig");
        if (guids.Length > 0)
        {
            var config = AssetDatabase.LoadAssetAtPath<TileMappingConfig>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            config?.RebuildCache();
            return config;
        }
        return null;
    }

    private Color GetTerrainColor(int tileId, TileMappingConfig config)
    {
        if (tileId == 0) return new Color(0.1f, 0.1f, 0.1f, 0.5f);
        if (config != null) return config.GetTileColor(tileId);
        return new Color(0.3f, 0.3f, 0.3f);
    }

    private Rect CellRect(float baseX, float baseY, int x, int y, float size)
    {
        return new Rect(
            baseX + x * size,
            baseY + (_data.height - 1 - y) * size,
            size, size);
    }

    private bool IsDark(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f < 0.5f;

    // ─────────── 快速播放（Domain Reload Safe）───────────

    private const string PREFS_ORIGIN_PATH = "QuickPlay_OriginPath";
    private const string PREFS_ORIGIN_SETUP = "QuickPlay_OriginSetup";
    private const string LEVEL_EDIT_PREFS_FLOW_SCENE = "LevelEdit_GameFlowScene";
    private const string LEVEL_EDIT_PREFS_FLOW_PATH = "LevelEdit_GameFlowTransformPath";
    private const string LEVEL_EDIT_PREFS_FLOW_MODE = "LevelEdit_GameFlowOriginalMode";
    private const string LEVEL_EDIT_PREFS_PLAYER_SCENE = "LevelEdit_LevelPlayerScene";
    private const string LEVEL_EDIT_PREFS_PLAYER_PATH = "LevelEdit_LevelPlayerTransformPath";
    private const string LEVEL_EDIT_PREFS_PLAYER_LEVEL = "LevelEdit_LevelPlayerOriginalLevel";

    private void QuickPlay()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        SaveActiveTilemapEditorIfNeeded();
        EnsureEditableLevelData();

        var originScene = EditorSceneManager.GetActiveScene();
        if (!originScene.IsValid() || string.IsNullOrEmpty(originScene.path))
        {
            EditorUtility.DisplayDialog("错误", "当前没有活动场景。", "确定");
            return;
        }

        // 1. EditorPrefs 持久化 origin（跨 Domain Reload）
        EditorPrefs.SetString(PREFS_ORIGIN_PATH, originScene.path);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        EditorPrefs.SetString(PREFS_ORIGIN_SETUP, SerializeSceneSetup(setup));

        Debug.Log($"[QuickPlay] 步骤1: origin={originScene.path}, scenes={setup?.Length ?? 0}");

        // 2. 准备 QuickPlaySession
        const string sessionPath = "Assets/Resources/QuickPlaySession.asset";
        var session = AssetDatabase.LoadAssetAtPath<QuickPlaySession>(sessionPath);
        if (session == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            session = ScriptableObject.CreateInstance<QuickPlaySession>();
            AssetDatabase.CreateAsset(session, sessionPath);
        }
        session.targetLevel = _data;
        session.config = FindConfig();
        session.active = true;
        EditorUtility.SetDirty(session);
        AssetDatabase.SaveAssets();

        Debug.Log($"[QuickPlay] 步骤2: QuickPlaySession target={_data.levelName}");

        // 3. 单独打开 StageScene，避免从 Tilemap 编辑场景进入播放时场景栈错乱。
        const string stageScenePath = "Assets/Scenes/StageScene.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(stageScenePath) == null)
        {
            EditorPrefs.DeleteKey(PREFS_ORIGIN_PATH);
            EditorPrefs.DeleteKey(PREFS_ORIGIN_SETUP);
            EditorUtility.DisplayDialog("错误",
                "找不到 StageScene.unity！\n请先在 Assets/Scenes/ 下创建场景，\n" +
                "放入 Camera + Light + LevelPlayer 组件。", "确定");
            return;
        }

        EditorSceneManager.playModeStartScene = null;
        var stageScene = EditorSceneManager.OpenScene(stageScenePath, OpenSceneMode.Single);
        if (!stageScene.IsValid())
        {
            EditorPrefs.DeleteKey(PREFS_ORIGIN_PATH);
            EditorPrefs.DeleteKey(PREFS_ORIGIN_SETUP);
            EditorUtility.DisplayDialog("错误", "无法打开 StageScene.unity。", "确定");
            return;
        }

        Debug.Log("[QuickPlay] 步骤3: StageScene 已单独打开 → EnterPlaymode()");

        // 4. 进入播放模式
        EditorApplication.EnterPlaymode();
    }

    private void Open3DEditor()
    {
        if (EditorApplication.isPlaying)
        {
            SwitchRunning3DEditorTarget();
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        SaveActiveTilemapEditorIfNeeded();
        EnsureEditableLevelData();

        var gameFlow = Object.FindObjectOfType<GameFlowController>();
        var levelPlayer = Object.FindObjectOfType<LevelPlayer>();
        if (gameFlow == null || levelPlayer == null)
        {
            EditorUtility.DisplayDialog("错误", "当前场景需要已有 GameFlowController 和 LevelPlayer。", "确定");
            return;
        }

        var serializedFlow = new SerializedObject(gameFlow);
        var modeProperty = serializedFlow.FindProperty("mode");
        EditorPrefs.SetString(LEVEL_EDIT_PREFS_FLOW_SCENE, gameFlow.gameObject.scene.path ?? string.Empty);
        EditorPrefs.SetString(LEVEL_EDIT_PREFS_FLOW_PATH, GetTransformIndexPath(gameFlow.transform));
        EditorPrefs.SetInt(LEVEL_EDIT_PREFS_FLOW_MODE, modeProperty.enumValueIndex);
        modeProperty.enumValueIndex = (int)GameFlowMode.LevelEdit;
        serializedFlow.ApplyModifiedProperties();

        var serializedPlayer = new SerializedObject(levelPlayer);
        var levelDataProperty = serializedPlayer.FindProperty("levelData");
        var originalLevel = levelDataProperty.objectReferenceValue as LevelData;
        EditorPrefs.SetString(LEVEL_EDIT_PREFS_PLAYER_SCENE, levelPlayer.gameObject.scene.path ?? string.Empty);
        EditorPrefs.SetString(LEVEL_EDIT_PREFS_PLAYER_PATH, GetTransformIndexPath(levelPlayer.transform));
        EditorPrefs.SetString(LEVEL_EDIT_PREFS_PLAYER_LEVEL, originalLevel != null
            ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(originalLevel))
            : string.Empty);
        levelDataProperty.objectReferenceValue = _data;
        serializedPlayer.ApplyModifiedProperties();

        EditorUtility.SetDirty(gameFlow);
        EditorUtility.SetDirty(levelPlayer);
        EditorSceneManager.MarkSceneDirty(gameFlow.gameObject.scene);

        Debug.Log($"[LevelEdit] 当前场景切换为编辑模式，target={_data.name} → EnterPlaymode()");
        EditorApplication.EnterPlaymode();
    }

    private void SwitchRunning3DEditorTarget()
    {
        EnsureEditableLevelData();

        var controller = Object.FindObjectOfType<Level3DEditorController>();
        var levelPlayer = LevelPlayer.ActiveInstance != null
            ? LevelPlayer.ActiveInstance
            : Object.FindObjectOfType<LevelPlayer>();

        if (controller == null || levelPlayer == null)
        {
            EditorUtility.DisplayDialog("错误", "当前 Play Mode 中没有可切换的 3D 关卡编辑器。", "确定");
            return;
        }

        if (controller.SourceLevel == _data)
            return;

        if (controller.HasUnsavedChanges)
        {
            string currentName = controller.SourceLevel != null ? controller.SourceLevel.name : "<unknown>";
            int choice = EditorUtility.DisplayDialogComplex(
                "切换 3D 编辑目标",
                $"当前关卡 {currentName} 有未保存的 3D 修改。\n切换到 {_data.name} 前要如何处理？",
                "保存并切换",
                "取消",
                "放弃并切换");

            if (choice == 1)
                return;
            if (choice == 0)
                controller.SaveChanges();
        }

        var config = levelPlayer.CurrentConfig != null ? levelPlayer.CurrentConfig : FindConfig();
        controller.Configure(levelPlayer, _data, config);
        Selection.activeObject = _data;
        Repaint();
        Debug.Log($"[LevelEdit] 3D 编辑目标已切换为 {_data.name}");
    }

    private void SaveActiveTilemapEditorIfNeeded()
    {
        if (!LevelTilemapSessionBridge.TryGetSession(out var session))
            return;

        string currentGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_data));
        if (string.IsNullOrEmpty(currentGuid) || currentGuid != session.LevelDataGUID)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                var editor = root.GetComponentInChildren<LevelTilemapEditor>(true);
                if (editor != null && editor.HasActiveSession)
                {
                    editor.SaveOnly();
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 跨 Domain Reload 自持的快速播放状态机。
    /// [InitializeOnLoad] 保证每次 Assembly 加载都重新订阅，不会丢失事件。
    /// </summary>
    [InitializeOnLoad]
    private static class QuickPlayTracker
    {
        static QuickPlayTracker()
        {
            EditorApplication.playModeStateChanged += OnStateChanged;
        }

        private static void OnStateChanged(PlayModeStateChange state)
        {
            bool isQuickPlay = EditorPrefs.HasKey(PREFS_ORIGIN_PATH);
            bool isLevelEdit = EditorPrefs.HasKey(LEVEL_EDIT_PREFS_FLOW_PATH);

            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if (isQuickPlay)
                        Debug.Log("[QuickPlay] ExitingEditMode → StageScene 进入播放");
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // 最安全时机：退出 Play Mode 时清空，不影响下次正常 Play
                    EditorSceneManager.playModeStartScene = null;
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    if (isLevelEdit)
                        RestoreLevelEditFlowMode();
                    if (isQuickPlay)
                        RestoreQuickPlayOrigin();
                    break;
            }
        }
    }

    private static void RestoreLevelEditFlowMode()
    {
        string flowScenePath = EditorPrefs.GetString(LEVEL_EDIT_PREFS_FLOW_SCENE, null);
        string flowTransformPath = EditorPrefs.GetString(LEVEL_EDIT_PREFS_FLOW_PATH, null);
        int originalMode = EditorPrefs.GetInt(LEVEL_EDIT_PREFS_FLOW_MODE, (int)GameFlowMode.DirectLevel);
        string playerScenePath = EditorPrefs.GetString(LEVEL_EDIT_PREFS_PLAYER_SCENE, null);
        string playerTransformPath = EditorPrefs.GetString(LEVEL_EDIT_PREFS_PLAYER_PATH, null);
        string originalLevelGuid = EditorPrefs.GetString(LEVEL_EDIT_PREFS_PLAYER_LEVEL, string.Empty);

        EditorPrefs.DeleteKey(LEVEL_EDIT_PREFS_FLOW_SCENE);
        EditorPrefs.DeleteKey(LEVEL_EDIT_PREFS_FLOW_PATH);
        EditorPrefs.DeleteKey(LEVEL_EDIT_PREFS_FLOW_MODE);
        EditorPrefs.DeleteKey(LEVEL_EDIT_PREFS_PLAYER_SCENE);
        EditorPrefs.DeleteKey(LEVEL_EDIT_PREFS_PLAYER_PATH);
        EditorPrefs.DeleteKey(LEVEL_EDIT_PREFS_PLAYER_LEVEL);

        var gameFlow = ResolveComponentInScene<GameFlowController>(flowScenePath, flowTransformPath)
                       ?? Object.FindObjectOfType<GameFlowController>();
        if (gameFlow == null)
        {
            Debug.LogWarning("[LevelEdit] 无法恢复 GameFlow mode：对象不存在");
            return;
        }

        var serializedFlow = new SerializedObject(gameFlow);
        serializedFlow.FindProperty("mode").enumValueIndex = originalMode;
        serializedFlow.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(gameFlow);
        EditorSceneManager.MarkSceneDirty(gameFlow.gameObject.scene);

        var levelPlayer = ResolveComponentInScene<LevelPlayer>(playerScenePath, playerTransformPath)
                          ?? Object.FindObjectOfType<LevelPlayer>();
        if (levelPlayer != null)
        {
            var serializedPlayer = new SerializedObject(levelPlayer);
            var levelProperty = serializedPlayer.FindProperty("levelData");
            levelProperty.objectReferenceValue = LoadLevelDataByGuid(originalLevelGuid);
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(levelPlayer);
            EditorSceneManager.MarkSceneDirty(levelPlayer.gameObject.scene);
        }

        Debug.Log($"[LevelEdit] 已恢复 GameFlow mode={(GameFlowMode)originalMode}");
    }

    private static string GetTransformIndexPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        var indices = new System.Collections.Generic.List<int>();
        Transform current = transform;
        while (current != null)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }

        indices.Reverse();
        return string.Join("/", indices);
    }

    private static T ResolveComponentInScene<T>(string scenePath, string transformIndexPath)
        where T : Component
    {
        if (string.IsNullOrEmpty(transformIndexPath))
            return null;

        Scene scene = default;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var candidate = SceneManager.GetSceneAt(i);
            if (!candidate.IsValid() || !candidate.isLoaded)
                continue;

            if (string.IsNullOrEmpty(scenePath) || candidate.path == scenePath)
            {
                scene = candidate;
                break;
            }
        }

        if (!scene.IsValid())
            return null;

        string[] parts = transformIndexPath.Split('/');
        if (parts.Length == 0 || !int.TryParse(parts[0], out int rootIndex))
            return null;

        var roots = scene.GetRootGameObjects();
        if (rootIndex < 0 || rootIndex >= roots.Length)
            return null;

        Transform current = roots[rootIndex].transform;
        for (int i = 1; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int childIndex))
                return null;
            if (childIndex < 0 || childIndex >= current.childCount)
                return null;
            current = current.GetChild(childIndex);
        }

        return current.GetComponent<T>() ?? current.GetComponentInChildren<T>(true);
    }

    private static LevelData LoadLevelDataByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return null;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<LevelData>(path);
    }

    private static void RestoreQuickPlayOrigin()
    {
        string originPath = EditorPrefs.GetString(PREFS_ORIGIN_PATH, null);
        string setupJson = EditorPrefs.GetString(PREFS_ORIGIN_SETUP, null);

        EditorPrefs.DeleteKey(PREFS_ORIGIN_PATH);
        EditorPrefs.DeleteKey(PREFS_ORIGIN_SETUP);

        if (string.IsNullOrEmpty(originPath))
        {
            Debug.LogWarning("[QuickPlay] 无 origin 记录，跳过恢复");
            return;
        }

        Debug.Log($"[QuickPlay] 步骤5: 退出 Play Mode，恢复场景 {originPath}");

        // Unity 已自动恢复编辑场景，此处额外恢复多场景加载布局
        var loadedSetups = DeserializeSceneSetup(setupJson);
        if (loadedSetups != null)
            EditorSceneManager.RestoreSceneManagerSetup(loadedSetups);
        else
            EditorSceneManager.OpenScene(originPath, OpenSceneMode.Single);

        Debug.Log("[QuickPlay] 已完成");
    }

    // ─────────── SceneSetup 序列化（EditorPrefs 用）───────────

    [System.Serializable]
    private sealed class SceneSetupEntry
    {
        public string path;
        public bool isActive;
        public bool isLoaded;
    }

    [System.Serializable]
    private sealed class SceneSetupList
    {
        public SceneSetupEntry[] items;
    }

    private static string SerializeSceneSetup(SceneSetup[] setups)
    {
        if (setups == null || setups.Length == 0) return "";
        var data = new SceneSetupList();
        data.items = new SceneSetupEntry[setups.Length];
        for (int i = 0; i < setups.Length; i++)
        {
            data.items[i] = new SceneSetupEntry
            {
                path = setups[i].path,
                isActive = setups[i].isActive,
                isLoaded = setups[i].isLoaded
            };
        }
        return JsonUtility.ToJson(data);
    }

    private static SceneSetup[] DeserializeSceneSetup(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var data = JsonUtility.FromJson<SceneSetupList>(json);
        if (data?.items == null || data.items.Length == 0) return null;

        var result = new SceneSetup[data.items.Length];
        for (int i = 0; i < data.items.Length; i++)
        {
            result[i] = new SceneSetup
            {
                path = data.items[i].path,
                isActive = data.items[i].isActive,
                isLoaded = data.items[i].isLoaded
            };
        }
        return result;
    }
}
