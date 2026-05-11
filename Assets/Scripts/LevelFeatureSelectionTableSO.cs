using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelFeatureSelectionTable", menuName = "BlockingKing/Tables/Level Feature Selection Table")]
public sealed class LevelFeatureSelectionTableSO : TableBaseSO, IPoolAnalyzable
{
    [Title("Source Pool")]
    [AssetsOnly]
    public LevelCollageSourceDatabase sourceDatabase;

    [Title("Fallback Filters")]
    [MinMaxSlider(1, 50, true), LabelText("Width")]
    public Vector2 widthRange = new(1, 50);

    [MinMaxSlider(1, 50, true), LabelText("Height")]
    public Vector2 heightRange = new(1, 50);

    [MinMaxSlider(1, 2500, true), LabelText("Area")]
    public Vector2 areaRange = new(1, 2500);

    [MinMaxSlider(0f, 1f, true), LabelText("Wall Rate")]
    public Vector2 wallRateRange = new(0f, 1f);

    [MinMaxSlider(0, 50, true), LabelText("Effective Boxes")]
    public Vector2 effectiveBoxRange = new(0, 50);

    [Title("Context Tables")]
    [AssetsOnly]
    public FloatRangeTableSO widthTable;

    [AssetsOnly]
    public FloatRangeTableSO heightTable;

    [AssetsOnly]
    public FloatRangeTableSO areaTable;

    [AssetsOnly]
    public FloatRangeTableSO wallRateTable;

    [AssetsOnly]
    public FloatRangeTableSO effectiveBoxTable;

    [Title("Roll")]
    [Tooltip("Off = equal chance among matching levels. On = use each database entry's manualWeight.")]
    public bool useEntryManualWeight;

    public bool TryRollLevel(PoolEvalContext context, System.Random random, out LevelData level)
    {
        level = null;
        if (!enabled || sourceDatabase?.entries == null || sourceDatabase.entries.Count == 0)
            return false;

        var candidates = new List<LevelCollageSourceEntry>();
        var filters = BuildFilters(context);
        foreach (var entry in sourceDatabase.entries)
        {
            if (!IsSelectable(entry, filters))
                continue;

            if (!useEntryManualWeight)
            {
                candidates.Add(entry);
                continue;
            }

            int weight = Mathf.Max(1, entry.manualWeight);
            for (int i = 0; i < weight; i++)
                candidates.Add(entry);
        }

        if (candidates.Count == 0)
            return false;

        int index = random != null ? random.Next(candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        level = candidates[index]?.level;
        return level != null;
    }

    public PoolAnalysisResult Analyze(PoolEvalContext context)
    {
        var result = new PoolAnalysisResult
        {
            poolId = tableId,
            displayName = GetResolvedDisplayName()
        };

        if (sourceDatabase?.entries == null)
        {
            PoolAnalysisMath.Finalize(result);
            return result;
        }

        var filters = BuildFilters(context);
        for (int i = 0; i < sourceDatabase.entries.Count; i++)
        {
            var entry = sourceDatabase.entries[i];
            bool selectable = enabled && IsSelectable(entry, filters);
            result.entries.Add(new PoolEntryAnalysis
            {
                id = i.ToString(),
                displayName = GetEntryDisplayName(entry, i),
                enabled = entry != null && entry.enabled,
                selectable = selectable,
                weight = selectable ? GetSelectionWeight(entry) : 0f,
                reason = BuildReason(entry, selectable, filters)
            });
        }

        PoolAnalysisMath.Finalize(result);
        return result;
    }

    private FeatureFilters BuildFilters(PoolEvalContext context)
    {
        return new FeatureFilters
        {
            width = widthTable != null ? Normalize(widthTable.Evaluate(context, widthRange), 1f) : Normalize(widthRange, 1f),
            height = heightTable != null ? Normalize(heightTable.Evaluate(context, heightRange), 1f) : Normalize(heightRange, 1f),
            area = areaTable != null ? Normalize(areaTable.Evaluate(context, areaRange), 1f) : Normalize(areaRange, 1f),
            wallRate = wallRateTable != null ? Normalize01(wallRateTable.Evaluate(context, wallRateRange)) : Normalize01(wallRateRange),
            effectiveBoxes = effectiveBoxTable != null
                ? Normalize(effectiveBoxTable.Evaluate(context, effectiveBoxRange), 0f)
                : Normalize(effectiveBoxRange, 0f)
        };
    }

    private static bool IsSelectable(LevelCollageSourceEntry entry, FeatureFilters filters)
    {
        if (entry == null || !entry.enabled || entry.level == null)
            return false;

        return InRange(entry.width, filters.width) &&
               InRange(entry.height, filters.height) &&
               InRange(entry.area, filters.area) &&
               InRange(entry.wallRate, filters.wallRate) &&
               InRange(entry.effectiveBoxCount, filters.effectiveBoxes);
    }

    private float GetSelectionWeight(LevelCollageSourceEntry entry)
    {
        if (entry == null)
            return 0f;

        return useEntryManualWeight ? Mathf.Max(1, entry.manualWeight) : 1f;
    }

    private static string GetEntryDisplayName(LevelCollageSourceEntry entry, int index)
    {
        if (entry?.level != null)
            return entry.level.name;

        return $"Level {index}";
    }

    private string BuildReason(LevelCollageSourceEntry entry, bool selectable, FeatureFilters filters)
    {
        if (!enabled)
            return "Table disabled";
        if (sourceDatabase == null)
            return "Source pool missing";
        if (entry == null)
            return "Null entry";
        if (!entry.enabled)
            return "Entry disabled";
        if (entry.level == null)
            return "Level missing";
        if (!selectable)
            return $"Filtered out by {filters}";

        return "OK";
    }

    private static bool InRange(float value, Vector2 range)
    {
        return value >= range.x && value <= range.y;
    }

    private static Vector2 Normalize(Vector2 value, float min)
    {
        float x = Mathf.Max(min, value.x);
        float y = Mathf.Max(min, value.y);
        return x <= y ? new Vector2(x, y) : new Vector2(y, x);
    }

    private static Vector2 Normalize01(Vector2 value)
    {
        float x = Mathf.Clamp01(value.x);
        float y = Mathf.Clamp01(value.y);
        return x <= y ? new Vector2(x, y) : new Vector2(y, x);
    }

    private struct FeatureFilters
    {
        public Vector2 width;
        public Vector2 height;
        public Vector2 area;
        public Vector2 wallRate;
        public Vector2 effectiveBoxes;

        public override string ToString()
        {
            return $"W {width.x:0.#}-{width.y:0.#}, H {height.x:0.#}-{height.y:0.#}, Area {area.x:0.#}-{area.y:0.#}, Wall {wallRate.x:0.##}-{wallRate.y:0.##}, Eff {effectiveBoxes.x:0.#}-{effectiveBoxes.y:0.#}";
        }
    }
}
