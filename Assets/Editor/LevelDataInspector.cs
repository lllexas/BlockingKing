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
    private LevelData _data;
    private TileMappingConfig _config;

    // 快速播放场景切换状态
    private static string _quickPlayOriginPath;
    private static SceneSetup[] _quickPlayOriginSetup;

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

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Tilemap 编辑", GUILayout.Height(30)))
            OpenTilemapEditor();

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

        EditorSceneManager.CloseScene(originScene, false);
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

    // ─────────── 快速播放（Play Mode）───────────

    private void QuickPlay()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 1. 保存当前场景，记录当前 load 的场景
        var originScene = EditorSceneManager.GetActiveScene();
        if (!originScene.IsValid() || string.IsNullOrEmpty(originScene.path))
        {
            EditorUtility.DisplayDialog("错误", "当前没有活动场景。", "确定");
            return;
        }
        _quickPlayOriginPath = originScene.path;
        _quickPlayOriginSetup = EditorSceneManager.GetSceneManagerSetup();

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

        // 3. 设置 StageScene 为播放模式启动场景 → Unity 会 unload 当前场景并 load StageScene
        const string stageScenePath = "Assets/Scenes/StageScene.unity";
        EditorSceneManager.playModeStartScene =
            AssetDatabase.LoadAssetAtPath<SceneAsset>(stageScenePath);

        // 4. 进入播放模式（StageScene 中 LevelPlayer.Start() 执行 mesh 操作）
        EditorApplication.playModeStateChanged += OnQuickPlayStateChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void OnQuickPlayStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
                // 进入播放模式，清理 playModeStartScene
                EditorSceneManager.playModeStartScene = null;
                break;

            case PlayModeStateChange.EnteredEditMode:
                // 5. 从播放模式退出，恢复步骤 1 中记录的场景 load 状态
                EditorApplication.playModeStateChanged -= OnQuickPlayStateChanged;
                RestoreQuickPlayOrigin();
                break;
        }
    }

    private static void RestoreQuickPlayOrigin()
    {
        if (string.IsNullOrEmpty(_quickPlayOriginPath)) return;

        var originPath = _quickPlayOriginPath;
        var setup = _quickPlayOriginSetup;
        _quickPlayOriginPath = null;
        _quickPlayOriginSetup = null;

        if (setup != null)
            EditorSceneManager.RestoreSceneManagerSetup(setup);

        Debug.Log($"[QuickPlay] 已恢复编辑场景: {originPath}");
    }
}
