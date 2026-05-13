using UnityEditor;
using UnityEngine;

public sealed class EscortLevelGeneratorWindow : EditorWindow
{
    private RunRoundConfigSO _roundConfig;
    private int _roundIndex = 1;
    private int _seed;
    private string _levelName = "Escort_Round_01";
    private string _status;

    [MenuItem("Tools/推箱子/Escort Level 生成器")]
    public static void ShowWindow()
    {
        var window = GetWindow<EscortLevelGeneratorWindow>("Escort Level 生成器");
        window.minSize = new Vector2(420, 230);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Escort LevelData 生成器", EditorStyles.boldLabel);
        EditorGUILayout.Space(8f);

        _roundConfig = (RunRoundConfigSO)EditorGUILayout.ObjectField(
            "Round Config",
            _roundConfig,
            typeof(RunRoundConfigSO),
            false);

        _roundIndex = Mathf.Max(1, EditorGUILayout.IntField("Round Index", _roundIndex));
        _seed = EditorGUILayout.IntField("Seed (0 = auto)", _seed);
        _levelName = EditorGUILayout.TextField("Level Name", _levelName);

        string outputFolder = ResolveSelectedProjectFolder();
        EditorGUILayout.HelpBox($"输出目录: {outputFolder}", MessageType.Info);

        GUI.enabled = _roundConfig != null;
        if (GUILayout.Button("生成 Escort LevelData 到当前 Project 路径", GUILayout.Height(36f)))
            Generate(outputFolder);
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.None);
    }

    private void Generate(string outputFolder)
    {
        if (_roundConfig == null)
        {
            EditorUtility.DisplayDialog("错误", "请先指定 RunRoundConfigSO。", "确定");
            return;
        }

        var sourceDatabase = ResolveEscortSourceDatabase(_roundConfig);
        if (sourceDatabase == null)
        {
            EditorUtility.DisplayDialog("错误", "Round Config 缺少 Escort source database。", "确定");
            return;
        }

        int roundCount = Mathf.Max(_roundIndex, _roundConfig.totalRounds);
        int seed = _seed != 0 ? _seed : StableSeed($"{_roundConfig.name}:escort:{_roundIndex}");
        var random = new System.Random(seed);
        var context = new PoolEvalContext
        {
            routeLayer = _roundIndex,
            routeLayerCount = Mathf.Max(1, roundCount),
            progress = roundCount > 1 ? Mathf.Clamp01((_roundIndex - 1) / (float)(roundCount - 1)) : 0f,
            difficulty = 1f,
            seed = seed
        };

        var resolved = ResolveEscortGeneration(_roundConfig, context, random);
        var sourceEntries = _roundConfig.escortFeatureSelectionTable != null
            ? _roundConfig.escortFeatureSelectionTable.BuildCandidates(context, _roundConfig.levelSourceDatabase)
            : null;
        LevelData level = EscortLevelGenerator.CreateFromRandomClassicMap(new EscortLevelBuildRequest
        {
            Seed = seed,
            ManhattanDistance = resolved.ManhattanDistance,
            LogSlope = resolved.LogSlope,
            Context = context,
            Constraints = resolved.Constraints,
            SourceDatabase = sourceDatabase,
            SourceEntries = sourceEntries
        });

        if (level == null)
        {
            EditorUtility.DisplayDialog("生成失败", "EscortLevelGenerator 没有生成 LevelData。请检查 sourceDatabase 和过滤条件。", "确定");
            return;
        }

        level.levelName = string.IsNullOrWhiteSpace(_levelName)
            ? $"Escort_Round_{_roundIndex:00}"
            : _levelName.Trim();
        level.name = level.levelName;

        if (!AssetDatabase.IsValidFolder(outputFolder))
            outputFolder = "Assets";

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{level.levelName}.asset");
        AssetDatabase.CreateAsset(level, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = level;
        EditorGUIUtility.PingObject(level);
        _status = $"生成完成: {assetPath}\nseed={seed}, round={_roundIndex}, distance={resolved.ManhattanDistance}, size={level.width}x{level.height}, tags={level.tags?.Count ?? 0}";
        Debug.Log($"[EscortLevelGeneratorWindow] {_status}");
    }

    private static LevelCollageSourceDatabase ResolveEscortSourceDatabase(RunRoundConfigSO config)
    {
        if (config == null)
            return null;

        if (config.escortFeatureSelectionTable != null && config.escortFeatureSelectionTable.sourceDatabase != null)
            return config.escortFeatureSelectionTable.sourceDatabase;

        if (config.levelSourceDatabase != null)
            return config.levelSourceDatabase;

        return config.legacyEscortGenerationSettings != null
            ? config.legacyEscortGenerationSettings.sourceDatabase
            : null;
    }

    private static EscortGenerationResolvedConfig ResolveEscortGeneration(
        RunRoundConfigSO config,
        PoolEvalContext context,
        System.Random random)
    {
        if (config.escortGenerationConfig != null)
            return config.escortGenerationConfig.Resolve(context, random);

        var legacySettings = config.legacyEscortGenerationSettings;
        var constraints = legacySettings != null
            ? legacySettings.ToConstraints(context)
            : EscortLevelGenerationConstraints.Default;

        int distance = legacySettings != null
            ? legacySettings.ClampEscortManhattanDistance(legacySettings.defaultManhattanDistance, context)
            : constraints.DefaultManhattanDistance;

        return new EscortGenerationResolvedConfig(constraints, distance, 0f);
    }

    private static string ResolveSelectedProjectFolder()
    {
        string path = "Assets";
        Object selected = Selection.activeObject;
        if (selected == null)
            return path;

        string selectedPath = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(selectedPath))
            return path;

        if (AssetDatabase.IsValidFolder(selectedPath))
            return selectedPath;

        string directory = System.IO.Path.GetDirectoryName(selectedPath)?.Replace('\\', '/');
        return !string.IsNullOrEmpty(directory) && AssetDatabase.IsValidFolder(directory)
            ? directory
            : path;
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];
            }

            return hash;
        }
    }
}
