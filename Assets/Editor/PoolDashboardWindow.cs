using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class PoolDashboardWindow : EditorWindow
{
    private readonly List<PoolBaseSO> _pools = new List<PoolBaseSO>();
    private Vector2 _poolScroll;
    private Vector2 _detailScroll;
    private PoolBaseSO _selectedPool;
    private PoolEvalContext _context = PoolEvalContext.Default;

    [MenuItem("Tools/BlockingKing/Pool Dashboard")]
    public static void Open()
    {
        GetWindow<PoolDashboardWindow>("Pool Dashboard");
    }

    private void OnEnable()
    {
        RefreshPools();
    }

    private void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        DrawPoolList();
        DrawSelectedPool();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            RefreshPools();

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPoolList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(280f));
        EditorGUILayout.LabelField("Pools", EditorStyles.boldLabel);
        _poolScroll = EditorGUILayout.BeginScrollView(_poolScroll);
        foreach (var pool in _pools)
        {
            if (pool == null)
                continue;

            bool selected = pool == _selectedPool;
            string label = $"{pool.GetType().Name}: {pool.GetResolvedDisplayName()}";
            if (GUILayout.Toggle(selected, label, "Button") && !selected)
                _selectedPool = pool;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawSelectedPool()
    {
        EditorGUILayout.BeginVertical();
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

        if (_selectedPool == null)
        {
            EditorGUILayout.HelpBox("Select a pool.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.LabelField(_selectedPool.GetResolvedDisplayName(), EditorStyles.boldLabel);
        EditorGUILayout.ObjectField("Asset", _selectedPool, typeof(PoolBaseSO), false);
        DrawContextFields();

        if (_selectedPool is not IPoolAnalyzable analyzable)
        {
            EditorGUILayout.HelpBox("This pool does not implement analysis.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        var result = analyzable.Analyze(_context);
        DrawSummary(result);
        DrawBarChart(result);
        DrawEntryTable(result);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawContextFields()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Context", EditorStyles.boldLabel);
        _context.progress = EditorGUILayout.Slider("Progress", _context.progress, 0f, 1f);
        _context.difficulty = Mathf.Max(0f, EditorGUILayout.FloatField("Difficulty", _context.difficulty));
        _context.routeLayer = Mathf.Max(0, EditorGUILayout.IntField("Route Layer", _context.routeLayer));
        _context.routeLayerCount = Mathf.Max(1, EditorGUILayout.IntField("Route Layer Count", _context.routeLayerCount));
        _context.seed = EditorGUILayout.IntField("Seed", _context.seed);

        if (_context.routeLayerCount > 1)
            _context.progress = Mathf.Clamp01(_context.routeLayer / (float)(_context.routeLayerCount - 1));
    }

    private static void DrawSummary(PoolAnalysisResult result)
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Entries", $"{result.selectableEntries}/{result.totalEntries}");
        EditorGUILayout.LabelField("Total Weight", result.totalWeight.ToString("0.###"));
        EditorGUILayout.LabelField("Entropy", $"{result.entropy:0.###} ({result.normalizedEntropy:P0})");
        EditorGUILayout.LabelField("Weight StdDev", result.standardDeviation.ToString("0.###"));
    }

    private static void DrawBarChart(PoolAnalysisResult result)
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Probability Distribution", EditorStyles.boldLabel);

        Rect chartRect = GUILayoutUtility.GetRect(10f, 180f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(chartRect, new Color(0.14f, 0.14f, 0.14f));

        if (result.entries.Count == 0)
            return;

        float maxProbability = 0f;
        foreach (var entry in result.entries)
            maxProbability = Mathf.Max(maxProbability, entry.probability);

        if (maxProbability <= 0f)
            return;

        float gap = 3f;
        float barWidth = Mathf.Max(3f, (chartRect.width - gap * (result.entries.Count + 1)) / result.entries.Count);
        for (int i = 0; i < result.entries.Count; i++)
        {
            var entry = result.entries[i];
            float normalized = entry.probability / maxProbability;
            float height = Mathf.Clamp01(normalized) * (chartRect.height - 22f);
            var barRect = new Rect(
                chartRect.x + gap + i * (barWidth + gap),
                chartRect.yMax - height - 18f,
                barWidth,
                height);

            var color = entry.selectable
                ? new Color(0.25f, 0.65f, 1f, 0.92f)
                : new Color(0.45f, 0.45f, 0.45f, 0.65f);
            EditorGUI.DrawRect(barRect, color);
        }
    }

    private static void DrawEntryTable(PoolAnalysisResult result)
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel);
        foreach (var entry in result.entries)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(entry.displayName, GUILayout.MinWidth(160f));
            EditorGUILayout.LabelField(entry.selectable ? "Active" : "Inactive", GUILayout.Width(64f));
            EditorGUILayout.LabelField($"W {entry.weight:0.###}", GUILayout.Width(80f));
            EditorGUILayout.LabelField($"{entry.probability:P1}", GUILayout.Width(64f));
            EditorGUILayout.LabelField(entry.reason);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void RefreshPools()
    {
        _pools.Clear();
        string[] guids = AssetDatabase.FindAssets("t:PoolBaseSO");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var pool = AssetDatabase.LoadAssetAtPath<PoolBaseSO>(path);
            if (pool != null)
                _pools.Add(pool);
        }

        if (_selectedPool == null && _pools.Count > 0)
            _selectedPool = _pools[0];
    }
}
