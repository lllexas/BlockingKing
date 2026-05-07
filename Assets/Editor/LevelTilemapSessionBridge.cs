using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// ──────────────────────────────────────
// 1. 会话数据
// ──────────────────────────────────────

public struct LevelTilemapSession
{
    /// <summary>LevelData SO 的 GUID</summary>
    public string LevelDataGUID;
    /// <summary>从哪个场景跳过来的</summary>
    public string ReturnScenePath;
    /// <summary>跳转前的场景状态（用于恢复）</summary>
    public SceneSetup[] SceneSetup;
}

// ──────────────────────────────────────
// 2. 跨场景会话桥接 (EditorPrefs)
// ──────────────────────────────────────

public static class LevelTilemapSessionBridge
{
    private const string Prefix = "BlockingKing_TilemapEditor_";

    public static bool IsReturning
        => EditorPrefs.GetBool(Prefix + "IsReturning", false);

    public static void SetSession(string levelDataGUID, string returnScenePath, SceneSetup[] sceneSetup)
    {
        EditorPrefs.SetString(Prefix + "LevelDataGUID", levelDataGUID ?? string.Empty);
        EditorPrefs.SetString(Prefix + "ReturnScenePath", returnScenePath ?? string.Empty);
        EditorPrefs.SetString(Prefix + "SceneSetupJson", SceneSetupUtil.Serialize(sceneSetup));
        EditorPrefs.SetBool(Prefix + "IsReturning", false);
    }

    public static void MarkReturning()
    {
        EditorPrefs.SetBool(Prefix + "IsReturning", true);
    }

    public static bool TryGetSession(out LevelTilemapSession session)
    {
        session = new LevelTilemapSession
        {
            LevelDataGUID = EditorPrefs.GetString(Prefix + "LevelDataGUID", string.Empty),
            ReturnScenePath = EditorPrefs.GetString(Prefix + "ReturnScenePath", string.Empty),
            SceneSetup = SceneSetupUtil.Deserialize(
                EditorPrefs.GetString(Prefix + "SceneSetupJson", string.Empty))
        };

        return !string.IsNullOrEmpty(session.LevelDataGUID);
    }

    public static void ClearSession()
    {
        EditorPrefs.DeleteKey(Prefix + "LevelDataGUID");
        EditorPrefs.DeleteKey(Prefix + "ReturnScenePath");
        EditorPrefs.DeleteKey(Prefix + "SceneSetupJson");
        EditorPrefs.DeleteKey(Prefix + "IsReturning");
    }
}

// ──────────────────────────────────────
// 3. SceneSetup 序列化工具
// ──────────────────────────────────────

public static class SceneSetupUtil
{
    [Serializable]
    private class StateCollection
    {
        public List<SceneState> scenes = new List<SceneState>();
    }

    [Serializable]
    private struct SceneState
    {
        public string path;
        public bool isLoaded;
        public bool isActive;
    }

    public static string Serialize(SceneSetup[] setup)
    {
        var collection = new StateCollection();
        if (setup != null)
        {
            foreach (var s in setup)
            {
                collection.scenes.Add(new SceneState
                {
                    path = s.path,
                    isLoaded = s.isLoaded,
                    isActive = s.isActive
                });
            }
        }
        return JsonUtility.ToJson(collection);
    }

    public static SceneSetup[] Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var collection = JsonUtility.FromJson<StateCollection>(json);
            if (collection?.scenes == null || collection.scenes.Count == 0)
                return null;

            var setup = new SceneSetup[collection.scenes.Count];
            for (int i = 0; i < collection.scenes.Count; i++)
            {
                var s = collection.scenes[i];
                setup[i] = new SceneSetup
                {
                    path = s.path,
                    isLoaded = s.isLoaded,
                    isActive = s.isActive
                };
            }
            return setup;
        }
        catch { return null; }
    }
}

// ──────────────────────────────────────
// 4. 编辑器场景创建工具
// ──────────────────────────────────────

public static class TilemapEditorSceneUtility
{
    public const string EditorScenePath = "Assets/Scenes/TilemapLevelEditor.unity";

    public static string EnsureSceneExists()
    {
        if (System.IO.File.Exists(EditorScenePath))
            return EditorScenePath;

        string dir = System.IO.Path.GetDirectoryName(EditorScenePath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);

        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera
        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 10f;
        cam.transform.position = new Vector3(0f, 0f, -10f);

        // Grid
        var gridGo = new GameObject("LevelGrid");
        var grid = gridGo.AddComponent<Grid>();

        // Terrain Tilemap（静态地形层）
        var terrainGo = new GameObject("Terrain");
        terrainGo.transform.SetParent(gridGo.transform);
        var terrainTilemap = terrainGo.AddComponent<Tilemap>();
        terrainGo.AddComponent<TilemapRenderer>();

        // Objects Tilemap（Tag 标记层，放在 Terrain 上面）
        var objectsGo = new GameObject("Objects");
        objectsGo.transform.SetParent(gridGo.transform);
        var objectsTilemap = objectsGo.AddComponent<Tilemap>();
        var objectsRenderer = objectsGo.AddComponent<TilemapRenderer>();
        objectsRenderer.sortingOrder = 1; // 确保在 Terrain 上面

        // LevelTilemapEditor 组件
        var editorRoot = new GameObject("TilemapLevelEditor");
        var editor = editorRoot.AddComponent<LevelTilemapEditor>();

        // 注入引用
        var so = new SerializedObject(editor);
        so.FindProperty("grid").objectReferenceValue = grid;
        so.FindProperty("terrainTilemap").objectReferenceValue = terrainTilemap;
        so.FindProperty("objectsTilemap").objectReferenceValue = objectsTilemap;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, EditorScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"<color=cyan>已创建编辑器场景: {EditorScenePath}</color>");
        return EditorScenePath;
    }
}
