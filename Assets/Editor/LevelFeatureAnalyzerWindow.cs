using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class LevelFeatureAnalyzerWindow : EditorWindow
{
    private const int WallTileID = 1;
    private const int BoxTagID = 2;
    private const int TargetTagID = 3;

    private string _rootFolder = "Assets/Resources/Levels";
    private Vector2 _scroll;
    private readonly List<LevelFeatureRow> _rows = new();

    private int _minEffectiveBoxes;
    private int _maxEffectiveBoxes = 12;
    private float _maxWallRate = 0.75f;
    private bool _showOnlyFiltered;
    private bool _showFilters;

    [MenuItem("Tools/推箱子/关卡特征分析")]
    public static void ShowWindow()
    {
        var window = GetWindow<LevelFeatureAnalyzerWindow>("关卡特征分析");
        window.minSize = new Vector2(980f, 520f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("LevelData 特征分析", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        EditorGUILayout.BeginHorizontal();
        _rootFolder = EditorGUILayout.TextField("扫描目录", _rootFolder);
        if (GUILayout.Button("选择", GUILayout.Width(64f)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择 LevelData 目录", Application.dataPath, "");
            if (!string.IsNullOrWhiteSpace(selected))
                _rootFolder = ToAssetPath(selected);
        }
        if (GUILayout.Button("扫描", GUILayout.Width(80f)))
            Scan();
        if (GUILayout.Button("导出 CSV", GUILayout.Width(90f)))
            ExportCsv();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8f);
        _showFilters = EditorGUILayout.Foldout(_showFilters, "显示过滤（只影响下方列表，不影响全量扫描/导出）", true);
        if (_showFilters)
        {
            EditorGUILayout.BeginHorizontal();
            _showOnlyFiltered = EditorGUILayout.ToggleLeft("启用显示过滤", _showOnlyFiltered, GUILayout.Width(120f));
            _minEffectiveBoxes = EditorGUILayout.IntField("有效箱下限", _minEffectiveBoxes, GUILayout.Width(180f));
            _maxEffectiveBoxes = EditorGUILayout.IntField("有效箱上限", _maxEffectiveBoxes, GUILayout.Width(180f));
            _maxWallRate = EditorGUILayout.Slider("最大墙率", _maxWallRate, 0f, 1f);
            EditorGUILayout.EndHorizontal();
        }

        DrawSummary();
        DrawTable();
    }

    private void Scan()
    {
        _rows.Clear();

        string[] folders = string.IsNullOrWhiteSpace(_rootFolder)
            ? null
            : new[] { _rootFolder };

        string[] guids = AssetDatabase.FindAssets("t:LevelData", folders);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (level == null)
                continue;

            _rows.Add(Analyze(level, path));
        }

        _rows.Sort((a, b) => a.Path.CompareTo(b.Path));
    }

    private static LevelFeatureRow Analyze(LevelData level, string path)
    {
        int area = Mathf.Max(1, level.width * level.height);
        int wallCount = 0;
        for (int y = 0; y < level.height; y++)
        {
            for (int x = 0; x < level.width; x++)
            {
                if (level.GetTile(x, y) == WallTileID)
                    wallCount++;
            }
        }

        var boxCells = new HashSet<Vector2Int>();
        var targetCells = new HashSet<Vector2Int>();
        if (level.tags != null)
        {
            foreach (var tag in level.tags)
            {
                if (tag == null)
                    continue;

                var pos = new Vector2Int(tag.x, tag.y);
                if (tag.tagID == BoxTagID)
                    boxCells.Add(pos);
                else if (tag.tagID == TargetTagID)
                    targetCells.Add(pos);
            }
        }

        int overlapped = 0;
        foreach (var boxCell in boxCells)
        {
            if (targetCells.Contains(boxCell))
                overlapped++;
        }

        int effectiveBoxes = Mathf.Max(0, boxCells.Count - overlapped);
        return new LevelFeatureRow
        {
            LevelName = string.IsNullOrWhiteSpace(level.levelName) ? level.name : level.levelName,
            Path = path,
            Width = level.width,
            Height = level.height,
            Area = area,
            WallCount = wallCount,
            BoxCount = boxCells.Count,
            TargetCount = targetCells.Count,
            BoxOnTargetCount = overlapped,
            EffectiveBoxCount = effectiveBoxes,
            WallRate = wallCount / (float)area,
            BoxRate = boxCells.Count / (float)area,
            EffectiveBoxRate = effectiveBoxes / (float)area
        };
    }

    private void DrawSummary()
    {
        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("尚未扫描。", MessageType.Info);
            return;
        }

        int visibleCount = 0;
        int minEffective = int.MaxValue;
        int maxEffective = int.MinValue;
        float wallRateSum = 0f;
        float effectiveRateSum = 0f;

        foreach (var row in _rows)
        {
            if (_showOnlyFiltered && !PassesFilter(row))
                continue;

            visibleCount++;
            minEffective = Mathf.Min(minEffective, row.EffectiveBoxCount);
            maxEffective = Mathf.Max(maxEffective, row.EffectiveBoxCount);
            wallRateSum += row.WallRate;
            effectiveRateSum += row.EffectiveBoxRate;
        }

        if (visibleCount == 0)
        {
            EditorGUILayout.HelpBox($"全量扫描 {_rows.Count} 张；当前显示过滤后 0 张。", MessageType.Warning);
            return;
        }

        string summary =
            $"全量扫描 {_rows.Count} 张，当前显示 {visibleCount} 张 | " +
            $"有效箱范围 {minEffective}-{maxEffective} | " +
            $"平均墙率 {wallRateSum / visibleCount:P1} | " +
            $"平均有效箱率 {effectiveRateSum / visibleCount:P1}";
        EditorGUILayout.HelpBox(summary, MessageType.Info);
    }

    private void DrawTable()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        Header("Name", 180f);
        Header("Size", 70f);
        Header("Wall%", 70f);
        Header("Box%", 70f);
        Header("EffBox%", 80f);
        Header("Box", 48f);
        Header("Eff", 48f);
        Header("B+T", 48f);
        Header("Path", 360f);
        EditorGUILayout.EndHorizontal();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var row in _rows)
        {
            if (_showOnlyFiltered && !PassesFilter(row))
                continue;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(row.LevelName, GUILayout.Width(180f));
            EditorGUILayout.LabelField($"{row.Width}x{row.Height}", GUILayout.Width(70f));
            EditorGUILayout.LabelField(row.WallRate.ToString("P1"), GUILayout.Width(70f));
            EditorGUILayout.LabelField(row.BoxRate.ToString("P1"), GUILayout.Width(70f));
            EditorGUILayout.LabelField(row.EffectiveBoxRate.ToString("P1"), GUILayout.Width(80f));
            EditorGUILayout.LabelField(row.BoxCount.ToString(), GUILayout.Width(48f));
            EditorGUILayout.LabelField(row.EffectiveBoxCount.ToString(), GUILayout.Width(48f));
            EditorGUILayout.LabelField(row.BoxOnTargetCount.ToString(), GUILayout.Width(48f));
            if (GUILayout.Button(row.Path, EditorStyles.linkLabel, GUILayout.Width(360f)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<LevelData>(row.Path);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private bool PassesFilter(LevelFeatureRow row)
    {
        return row.EffectiveBoxCount >= _minEffectiveBoxes &&
               row.EffectiveBoxCount <= _maxEffectiveBoxes &&
               row.WallRate <= _maxWallRate;
    }

    private void ExportCsv()
    {
        if (_rows.Count == 0)
            Scan();

        if (_rows.Count == 0)
        {
            EditorUtility.DisplayDialog("导出失败", "没有可导出的分析结果。", "确定");
            return;
        }

        string path = EditorUtility.SaveFilePanel("导出关卡特征 CSV", Application.dataPath, "level_features.csv", "csv");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var builder = new StringBuilder();
        builder.AppendLine("name,path,width,height,area,wall_count,wall_rate,box_count,box_rate,target_count,box_on_target_count,effective_box_count,effective_box_rate");
        foreach (var row in _rows)
        {
            builder.Append(Escape(row.LevelName)).Append(',')
                .Append(Escape(row.Path)).Append(',')
                .Append(row.Width).Append(',')
                .Append(row.Height).Append(',')
                .Append(row.Area).Append(',')
                .Append(row.WallCount).Append(',')
                .Append(Float(row.WallRate)).Append(',')
                .Append(row.BoxCount).Append(',')
                .Append(Float(row.BoxRate)).Append(',')
                .Append(row.TargetCount).Append(',')
                .Append(row.BoxOnTargetCount).Append(',')
                .Append(row.EffectiveBoxCount).Append(',')
                .Append(Float(row.EffectiveBoxRate)).AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        AssetDatabase.Refresh();
    }

    private static void Header(string text, float width)
    {
        EditorGUILayout.LabelField(text, EditorStyles.boldLabel, GUILayout.Width(width));
    }

    private static string ToAssetPath(string absolutePath)
    {
        string normalized = absolutePath.Replace('\\', '/');
        string dataPath = Application.dataPath.Replace('\\', '/');
        if (normalized.StartsWith(dataPath))
            return "Assets" + normalized[dataPath.Length..];

        return normalized;
    }

    private static string Float(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed class LevelFeatureRow
    {
        public string LevelName;
        public string Path;
        public int Width;
        public int Height;
        public int Area;
        public int WallCount;
        public int BoxCount;
        public int TargetCount;
        public int BoxOnTargetCount;
        public int EffectiveBoxCount;
        public float WallRate;
        public float BoxRate;
        public float EffectiveBoxRate;
    }
}
