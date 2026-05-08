using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

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

        // Targets Tilemap（非占格标记层）
        var targetsGo = new GameObject("Targets");
        targetsGo.transform.SetParent(gridGo.transform);
        var targetsTilemap = targetsGo.AddComponent<Tilemap>();
        var targetsRenderer = targetsGo.AddComponent<TilemapRenderer>();
        targetsRenderer.sortingOrder = 1;

        // Actors Tilemap（占格单位层）
        var actorsGo = new GameObject("Actors");
        actorsGo.transform.SetParent(gridGo.transform);
        var actorsTilemap = actorsGo.AddComponent<Tilemap>();
        var actorsRenderer = actorsGo.AddComponent<TilemapRenderer>();
        actorsRenderer.sortingOrder = 2;

        // LevelTilemapEditor 组件
        var editorRoot = new GameObject("TilemapLevelEditor");
        var editor = editorRoot.AddComponent<LevelTilemapEditor>();
        if (editor == null)
            throw new System.InvalidOperationException("无法添加 LevelTilemapEditor。请确认该 MonoBehaviour 不在 Assets/Editor 目录下。");

        // 注入引用
        var so = new SerializedObject(editor);
        so.FindProperty("grid").objectReferenceValue = grid;
        so.FindProperty("terrainTilemap").objectReferenceValue = terrainTilemap;
        so.FindProperty("targetTilemap").objectReferenceValue = targetsTilemap;
        so.FindProperty("actorTilemap").objectReferenceValue = actorsTilemap;
        so.ApplyModifiedPropertiesWithoutUndo();
        editor.LoadSession();

        EditorSceneManager.SaveScene(scene, EditorScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"<color=cyan>已创建编辑器场景: {EditorScenePath}</color>");
        return EditorScenePath;
    }

    public static LevelTilemapEditor EnsureSceneObjects(Scene scene)
    {
        if (!scene.IsValid())
            return null;

        Grid grid = null;
        Tilemap terrainTilemap = null;
        Tilemap targetTilemap = null;
        Tilemap actorTilemap = null;
        LevelTilemapEditor editor = null;

        foreach (var root in scene.GetRootGameObjects())
        {
            grid ??= root.GetComponentInChildren<Grid>(true);
            editor ??= root.GetComponentInChildren<LevelTilemapEditor>(true);

            foreach (var tilemap in root.GetComponentsInChildren<Tilemap>(true))
            {
                if (tilemap.name == "Terrain")
                    terrainTilemap ??= tilemap;
                else if (tilemap.name == "Targets")
                    targetTilemap ??= tilemap;
                else if (tilemap.name == "Actors")
                    actorTilemap ??= tilemap;
                else if (tilemap.name == "Objects")
                {
                    tilemap.name = "Actors";
                    actorTilemap ??= tilemap;
                }
            }
        }

        if (grid == null)
        {
            var gridGo = new GameObject("LevelGrid");
            SceneManager.MoveGameObjectToScene(gridGo, scene);
            grid = gridGo.AddComponent<Grid>();
        }

        if (terrainTilemap == null)
            terrainTilemap = CreateTilemap(scene, "Terrain", grid.transform, 0);

        if (targetTilemap == null)
            targetTilemap = CreateTilemap(scene, "Targets", grid.transform, 1);

        if (actorTilemap == null)
            actorTilemap = CreateTilemap(scene, "Actors", grid.transform, 2);

        if (editor == null)
        {
            var editorRoot = new GameObject("TilemapLevelEditor");
            SceneManager.MoveGameObjectToScene(editorRoot, scene);
            editor = editorRoot.AddComponent<LevelTilemapEditor>();
        }

        if (editor == null)
            throw new System.InvalidOperationException("无法添加 LevelTilemapEditor。请确认该 MonoBehaviour 不在 Assets/Editor 目录下。");

        var so = new SerializedObject(editor);
        so.FindProperty("grid").objectReferenceValue = grid;
        so.FindProperty("terrainTilemap").objectReferenceValue = terrainTilemap;
        so.FindProperty("targetTilemap").objectReferenceValue = targetTilemap;
        so.FindProperty("actorTilemap").objectReferenceValue = actorTilemap;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        editor.LoadSession();
        return editor;
    }

    private static Tilemap CreateTilemap(Scene scene, string name, Transform parent, int sortingOrder)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.SetParent(parent);
        var tilemap = go.AddComponent<Tilemap>();
        var renderer = go.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = sortingOrder;
        return tilemap;
    }
}
