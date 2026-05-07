using UnityEngine;
using UnityEditor;

/// <summary>
/// LevelTilemapEditor 的自定义 Inspector，显示编辑工具按钮
/// </summary>
[CustomEditor(typeof(LevelTilemapEditor))]
public class LevelTilemapEditorInspector : Editor
{
    private LevelTilemapEditor _editor;

    private void OnEnable()
    {
        _editor = (LevelTilemapEditor)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("编辑操作", EditorStyles.boldLabel);

        // 编辑中关卡信息
        if (_editor.HasActiveSession)
        {
            var levelData = AssetDatabase.LoadAssetAtPath<LevelData>(
                AssetDatabase.GUIDToAssetPath(_editor.LevelDataGUID));
            if (levelData != null)
            {
                EditorGUILayout.HelpBox(
                    $"当前编辑: {levelData.levelName}\n尺寸: {levelData.width} x {levelData.height}",
                    MessageType.Info);
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "无活跃会话。请从 LevelData SO 的 Inspector 中点击 [在 Tilemap 编辑器中打开] 按钮。",
                MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // ── 保存（不返回）──
        GUI.enabled = _editor.HasActiveSession;
        if (GUILayout.Button("保存关卡", GUILayout.Height(35)))
        {
            _editor.SaveOnly();
        }

        EditorGUILayout.Space(5);

        // ── 保存并返回 ──
        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("保存并返回原场景", GUILayout.Height(40)))
        {
            _editor.SaveAndReturn();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        EditorGUILayout.Space(10);

        // ── 直接返回（不保存）──
        GUI.enabled = _editor.HasActiveSession;
        GUI.backgroundColor = new Color(0.9f, 0.6f, 0.4f);
        if (GUILayout.Button("放弃修改并返回", GUILayout.Height(30)))
        {
            ReturnWithoutSaving();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
    }

    private void ReturnWithoutSaving()
    {
        if (!EditorUtility.DisplayDialog("确认返回",
                "不会保存当前修改，确定返回吗？", "返回（不保存）", "取消"))
            return;

        LevelTilemapSessionBridge.MarkReturning();

        if (LevelTilemapSessionBridge.TryGetSession(out var session))
        {
            if (session.SceneSetup != null && session.SceneSetup.Length > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager
                    .RestoreSceneManagerSetup(session.SceneSetup);
                return;
            }

            if (!string.IsNullOrEmpty(session.ReturnScenePath))
            {
                var returnScene = UnityEditor.SceneManagement.EditorSceneManager
                    .OpenScene(session.ReturnScenePath,
                        UnityEditor.SceneManagement.OpenSceneMode.Additive);
                if (returnScene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager
                        .SetActiveScene(returnScene);

                var currentScene = _editor.gameObject.scene;
                if (currentScene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager
                        .CloseScene(currentScene, true);
            }
        }
    }
}
