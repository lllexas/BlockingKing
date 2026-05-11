using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "FloatRangeTable", menuName = "BlockingKing/Tables/Float Range Table")]
public sealed class FloatRangeTableSO : TableBaseSO, IPoolAnalyzable
{
    [Serializable]
    public sealed class Row
    {
        public bool enabled = true;
        public string label;

        [MinMaxSlider(0f, 1f, true), LabelText("Progress")]
        public Vector2 progressRange = new(0f, 1f);

        [MinMaxSlider(0f, 10f, true), LabelText("Difficulty")]
        public Vector2 difficultyRange = new(0f, 10f);

        [LabelText("Route Layer Min")]
        public int minRouteLayer = 0;

        [LabelText("Route Layer Max (-1 = any)")]
        public int maxRouteLayer = -1;

        [LabelText("Output Range")]
        public Vector2 outputRange;

        [MinValue(0f)]
        public float weight = 1f;
    }

    [Title("Fallback")]
    public Vector2 fallbackRange = new(0f, 1f);

    [Title("Rows")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<Row> rows = new List<Row>();

    public Vector2 Evaluate(PoolEvalContext context, Vector2 fallback)
    {
        if (!enabled || rows == null || rows.Count == 0)
            return Normalize(fallback);

        float totalWeight = 0f;
        Vector2 weighted = Vector2.zero;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!IsRowSelectable(row, context))
                continue;

            float weight = Mathf.Max(0f, row.weight);
            if (weight <= 0f)
                continue;

            weighted += Normalize(row.outputRange) * weight;
            totalWeight += weight;
        }

        return totalWeight > 0f ? Normalize(weighted / totalWeight) : Normalize(fallback);
    }

    public Vector2 Evaluate(PoolEvalContext context)
    {
        return Evaluate(context, fallbackRange);
    }

    public Vector2Int EvaluateIntRange(PoolEvalContext context, Vector2 fallback)
    {
        var range = Evaluate(context, fallback);
        return new Vector2Int(
            Mathf.RoundToInt(range.x),
            Mathf.RoundToInt(range.y));
    }

    public PoolAnalysisResult Analyze(PoolEvalContext context)
    {
        var result = new PoolAnalysisResult
        {
            poolId = tableId,
            displayName = GetResolvedDisplayName()
        };

        if (rows == null)
        {
            PoolAnalysisMath.Finalize(result);
            return result;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool selectable = IsRowSelectable(row, context);
            result.entries.Add(new PoolEntryAnalysis
            {
                id = i.ToString(),
                displayName = GetRowDisplayName(row, i),
                enabled = row != null && row.enabled,
                selectable = selectable,
                weight = selectable ? Mathf.Max(0f, row.weight) : 0f,
                reason = BuildReason(row, selectable)
            });
        }

        PoolAnalysisMath.Finalize(result);
        return result;
    }

    private static bool IsRowSelectable(Row row, PoolEvalContext context)
    {
        if (row == null || !row.enabled)
            return false;

        var progress = Normalize01(row.progressRange);
        if (context.progress < progress.x || context.progress > progress.y)
            return false;

        var difficulty = Normalize(row.difficultyRange);
        if (context.difficulty < difficulty.x || context.difficulty > difficulty.y)
            return false;

        if (context.routeLayer < Mathf.Max(0, row.minRouteLayer))
            return false;

        return row.maxRouteLayer < 0 || context.routeLayer <= row.maxRouteLayer;
    }

    private static string GetRowDisplayName(Row row, int index)
    {
        if (row != null && !string.IsNullOrWhiteSpace(row.label))
            return row.label;

        return $"Row {index}";
    }

    private string BuildReason(Row row, bool selectable)
    {
        if (!enabled)
            return "Table disabled";
        if (row == null)
            return "Null row";
        if (!row.enabled)
            return "Row disabled";
        if (!selectable)
            return "Out of context range";

        return $"Range {Normalize(row.outputRange).x:0.###}-{Normalize(row.outputRange).y:0.###}";
    }

    private static Vector2 Normalize(Vector2 value)
    {
        return value.x <= value.y ? value : new Vector2(value.y, value.x);
    }

    private static Vector2 Normalize01(Vector2 value)
    {
        float x = Mathf.Clamp01(value.x);
        float y = Mathf.Clamp01(value.y);
        return x <= y ? new Vector2(x, y) : new Vector2(y, x);
    }
}
