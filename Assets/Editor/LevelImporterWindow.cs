using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 经典 Sokoban 关卡导入器：读取 .txt/.xsb 文本文件，根据 ImportMappingConfig
/// 批量转换为 LevelData SO。
/// </summary>
public class LevelImporterWindow : EditorWindow
{
    private ImportMappingConfig _mapping;
    private string _sourcePath = "";
    private string _outputDir = "Assets/Levels/";
    private string _levelNamePrefix = "Level_";

    [MenuItem("Tools/推箱子/关卡导入器")]
    public static void ShowWindow()
    {
        var w = GetWindow<LevelImporterWindow>("关卡导入器");
        w.minSize = new Vector2(420, 380);
    }

    private void OnGUI()
    {
        GUILayout.Label("经典 Sokoban 关卡导入", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        _mapping = (ImportMappingConfig)EditorGUILayout.ObjectField(
            "导入映射配置", _mapping, typeof(ImportMappingConfig), false);

        EditorGUILayout.BeginHorizontal();
        _sourcePath = EditorGUILayout.TextField("源文件路径", _sourcePath);
        if (GUILayout.Button("选择文件", GUILayout.Width(70)))
        {
            string selected = EditorUtility.OpenFilePanel(
                "选择 Sokoban 关卡文件", Application.dataPath, "txt,xsb");
            if (!string.IsNullOrEmpty(selected))
                _sourcePath = selected;
        }
        EditorGUILayout.EndHorizontal();

        _outputDir = EditorGUILayout.TextField("输出目录", _outputDir);
        _levelNamePrefix = EditorGUILayout.TextField("关卡名前缀", _levelNamePrefix);

        EditorGUILayout.Space(10);

        GUI.enabled = _mapping != null && _sourcePath.Length > 0;
        if (GUILayout.Button("批量导入", GUILayout.Height(40)))
        {
            Import();
        }
        GUI.enabled = true;
    }

    private void Import()
    {
        if (_mapping == null)
        {
            EditorUtility.DisplayDialog("错误", "请拖入 ImportMappingConfig。", "确定");
            return;
        }
        if (!System.IO.File.Exists(_sourcePath))
        {
            EditorUtility.DisplayDialog("错误", "源文件不存在。", "确定");
            return;
        }

        string text = System.IO.File.ReadAllText(_sourcePath);
        var blocks = SplitLevelBlocks(text);
        if (blocks.Count == 0)
        {
            EditorUtility.DisplayDialog("结果", "未找到任何关卡块。", "确定");
            return;
        }

        if (!System.IO.Directory.Exists(_outputDir))
            System.IO.Directory.CreateDirectory(_outputDir);

        int created = 0;
        foreach (var block in blocks)
        {
            int levelNumber = block.number;
            var lines = block.lines;
            if (lines.Count == 0) continue;

            // 解析字符网格
            int w = lines.Max(l => l.Length);
            int h = lines.Count;

            int[][] map2D = new int[h][];
            var tags = new List<LevelTagEntry>();

            for (int y = 0; y < h; y++)
            {
                string line = lines[h - 1 - y]; // 翻转 Y
                map2D[y] = new int[w];
                for (int x = 0; x < w; x++)
                {
                    char c = x < line.Length ? line[x] : ' ';
                    HandleCell(c, x, y, w, h, map2D, tags);
                }
            }

            // 创建 SO
            var levelData = ScriptableObject.CreateInstance<LevelData>();
            levelData.levelName = $"{_levelNamePrefix}{levelNumber:D3}";
            levelData.SetFromMap2D(map2D);
            levelData.tags = tags;

            string fileName = $"{_levelNamePrefix}{levelNumber:D3}.asset";
            string assetPath = System.IO.Path.Combine(_outputDir, fileName);
            assetPath = assetPath.Replace('\\', '/');

            AssetDatabase.CreateAsset(levelData, assetPath);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("导入完成",
            $"共处理 {blocks.Count} 个关卡块，成功创建 {created} 个 LevelData SO。\n输出: {_outputDir}",
            "确定");
    }

    private void HandleCell(char c, int x, int y, int w, int h,
        int[][] map2D, List<LevelTagEntry> tags)
    {
        // 先查复合符号（* +），拆成多个 tag + 地板
        var tagIDs = _mapping.GetTagIDs(c);
        if (tagIDs != null && tagIDs.Count > 0)
        {
            // 复合符号：地形填 floor，额外加多条 tag
            map2D[y][x] = _mapping.floorTerrainID;
            foreach (int tid in tagIDs)
                tags.Add(new LevelTagEntry { tagID = tid, x = x, y = y });
            return;
        }

        // 纯地形符号
        int terrainId = _mapping.GetTerrainID(c);
        if (terrainId >= 0)
        {
            map2D[y][x] = terrainId;
            return;
        }

        // 未知字符（可能是注释行尾空白或异常字符）→ 填空地
        map2D[y][x] = _mapping.floorTerrainID;
    }

    // ─────────── 关卡块分割 ───────────

    private struct LevelBlock
    {
        public int number;
        public List<string> lines;
    }

    private static List<LevelBlock> SplitLevelBlocks(string text)
    {
        var blocks = new List<LevelBlock>();
        var allLines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        var currentLines = new List<string>();
        int currentNumber = -1;

        foreach (var rawLine in allLines)
        {
            string line = rawLine.TrimEnd();

            // 注释行：可能包含关卡编号
            if (line.StartsWith(";"))
            {
                // 尝试解析 ; N 格式的编号
                string numPart = line.Substring(1).Trim();
                if (int.TryParse(numPart, out int n) && n > 0)
                {
                    // 上一块结束
                    if (currentLines.Count > 0)
                    {
                        blocks.Add(new LevelBlock
                        {
                            number = currentNumber > 0 ? currentNumber : blocks.Count + 1,
                            lines = currentLines
                        });
                    }
                    currentLines = new List<string>();
                    currentNumber = n;
                }
                continue;
            }

            // 空行：分隔块
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentLines.Count > 0)
                {
                    blocks.Add(new LevelBlock
                    {
                        number = currentNumber > 0 ? currentNumber : blocks.Count + 1,
                        lines = currentLines
                    });
                }
                currentLines = new List<string>();
                currentNumber = -1;
                continue;
            }

            // 关卡行
            currentLines.Add(line);
        }

        // 最后一块
        if (currentLines.Count > 0)
        {
            blocks.Add(new LevelBlock
            {
                number = currentNumber > 0 ? currentNumber : blocks.Count + 1,
                lines = currentLines
            });
        }

        return blocks;
    }
}
