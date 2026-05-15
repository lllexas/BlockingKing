using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LevelSolverWindow : EditorWindow
{
    private const string SessionPath = "Assets/Resources/LevelSolverSession.asset";
    private const string StageScenePath = "Assets/Scenes/StageScene.unity";

    [SerializeField] private LevelData targetLevel;
    [SerializeField] private TileMappingConfig config;
    [SerializeField] private int startingMaxHp = 80;
    [SerializeField] private int startingHp = 80;
    [SerializeField] private int startingAttack = 4;
    [SerializeField] private int startingBlock;
    [SerializeField] private LevelSolverVictoryCondition victoryCondition = LevelSolverVictoryCondition.AllBoxesOnTargets;
    [SerializeField] private int maxDepth = 64;
    [SerializeField] private int maxNodes = 200000;
    [SerializeField] private bool includeNoop = true;
    [SerializeField] private bool stopOnFirstSolution = true;
    [SerializeField] private string reportPath = "Plan/Active/RuntimeLevelSolverReport.md";

    [MenuItem("Tools/BlockingKing/Level Solver")]
    public static void Open()
    {
        GetWindow<LevelSolverWindow>("Level Solver").Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Runtime Level Solver", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Runs the real runtime systems in Play Mode. It enumerates player move/noop/card intents, restores snapshots between branches, and writes a markdown report.", MessageType.Info);

        targetLevel = (LevelData)EditorGUILayout.ObjectField("Level Data", targetLevel, typeof(LevelData), false);
        config = (TileMappingConfig)EditorGUILayout.ObjectField("Tile Config", config, typeof(TileMappingConfig), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Initial Player Stats", EditorStyles.boldLabel);
        startingMaxHp = EditorGUILayout.IntField("Max HP", startingMaxHp);
        startingHp = EditorGUILayout.IntField("HP", startingHp);
        startingAttack = EditorGUILayout.IntField("Attack", startingAttack);
        startingBlock = EditorGUILayout.IntField("Block", startingBlock);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Search", EditorStyles.boldLabel);
        victoryCondition = (LevelSolverVictoryCondition)EditorGUILayout.EnumPopup("Victory", victoryCondition);
        maxDepth = EditorGUILayout.IntField("Max Depth", maxDepth);
        maxNodes = EditorGUILayout.IntField("Max Nodes", maxNodes);
        includeNoop = EditorGUILayout.Toggle("Include Noop", includeNoop);
        stopOnFirstSolution = EditorGUILayout.Toggle("Stop On First Solution", stopOnFirstSolution);
        reportPath = EditorGUILayout.TextField("Report Path", reportPath);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(targetLevel == null || EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Solve In Play Mode", GUILayout.Height(34f)))
                StartSolverPlayMode();
        }
    }

    private void StartSolverPlayMode()
    {
        if (targetLevel == null)
        {
            EditorUtility.DisplayDialog("Level Solver", "Select a LevelData first.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        LevelSolverSession session = LoadOrCreateSession();
        session.targetLevel = targetLevel;
        session.config = config;
        session.active = true;
        session.startingMaxHp = Mathf.Max(1, startingMaxHp);
        session.startingHp = Mathf.Clamp(startingHp, 1, session.startingMaxHp);
        session.startingAttack = Mathf.Max(0, startingAttack);
        session.startingBlock = Mathf.Max(0, startingBlock);
        session.victoryCondition = victoryCondition;
        session.maxDepth = Mathf.Max(1, maxDepth);
        session.maxNodes = Mathf.Max(1, maxNodes);
        session.includeNoop = includeNoop;
        session.stopOnFirstSolution = stopOnFirstSolution;
        session.reportPath = reportPath;
        EditorUtility.SetDirty(session);
        AssetDatabase.SaveAssets();

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StageScenePath) == null)
        {
            EditorUtility.DisplayDialog("Level Solver", "StageScene.unity not found at Assets/Scenes/StageScene.unity.", "OK");
            return;
        }

        EditorSceneManager.playModeStartScene = null;
        Scene scene = EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("Level Solver", "Failed to open StageScene.unity.", "OK");
            return;
        }

        var flow = Object.FindObjectOfType<GameFlowController>();
        if (flow != null)
        {
            var serializedFlow = new SerializedObject(flow);
            var modeProperty = serializedFlow.FindProperty("mode");
            if (modeProperty != null)
            {
                modeProperty.enumValueIndex = (int)GameFlowMode.Solver;
                serializedFlow.ApplyModifiedProperties();
                EditorUtility.SetDirty(flow);
                EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
            }
        }

        Debug.Log($"[LevelSolverWindow] Starting solver play mode: level={targetLevel.name}");
        EditorApplication.EnterPlaymode();
    }

    private static LevelSolverSession LoadOrCreateSession()
    {
        var session = AssetDatabase.LoadAssetAtPath<LevelSolverSession>(SessionPath);
        if (session != null)
            return session;

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        session = CreateInstance<LevelSolverSession>();
        AssetDatabase.CreateAsset(session, SessionPath);
        return session;
    }
}
