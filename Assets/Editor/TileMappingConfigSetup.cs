using UnityEngine;
using UnityEditor;

/// <summary>
/// 快速初始化 TileMappingConfig 的工具
/// </summary>
public class TileMappingConfigSetup
{
    [MenuItem("Tools/推箱子/创建 Tile Mapping Config")]
    public static void CreateTileMappingConfig()
    {
        string path = "Assets/Settings/TileMappingConfig.asset";

        // 确保 Settings 目录存在
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        // 检查是否已存在
        var existing = AssetDatabase.LoadAssetAtPath<TileMappingConfig>(path);
        if (existing != null)
        {
            EditorUtility.DisplayDialog("提示",
                $"TileMappingConfig 已存在：{path}\n请在 Project 窗口中选中它进行编辑。",
                "确定");
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            return;
        }

        var config = ScriptableObject.CreateInstance<TileMappingConfig>();
        AssetDatabase.CreateAsset(config, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("创建成功",
            $"TileMappingConfig 已创建：{path}\n请在它的 Inspector 中配置 Tile 与 ID 的映射。",
            "确定");

        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }
}
