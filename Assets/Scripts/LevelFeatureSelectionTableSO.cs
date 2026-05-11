using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelFeatureSelectionTable", menuName = "BlockingKing/Tables/Level Feature Selection Table")]
public sealed class LevelFeatureSelectionTableSO : TableBaseSO, IPoolAnalyzable
{
    [System.Serializable]
    public sealed class Row
    {
        public bool enabled = true;

        [Min(0)]
        public int roundIndex;

        public string label;

        [AssetsOnly]
        public LevelFeatureFilterSO filter;
    }

    [Title("Source Pool")]
    [AssetsOnly]
    public LevelCollageSourceDatabase sourceDatabase;

    [Title("Fallback Filter")]
    [AssetsOnly]
    public LevelFeatureFilterSO fallbackFilter;

    [Title("Round Rows")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 160)]
    public List<Row> rows = new List<Row>();

    [Title("Roll")]
    [Tooltip("Off = equal chance among matching levels. On = use each database entry's manualWeight.")]
    public bool useEntryManualWeight;

    public bool TryRollLevel(PoolEvalContext context, System.Random random, out LevelData level)
    {
        return TryRollLevel(context, random, null, out level);
    }

    public bool TryRollLevel(PoolEvalContext context, System.Random random, LevelCollageSourceDatabase fallbackSourceDatabase, out LevelData level)
    {
        level = null;
        var candidates = BuildCandidates(context, fallbackSourceDatabase);
        if (candidates.Count == 0)
            return false;

        int index = random != null ? random.Next(candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        level = candidates[index]?.level;
        return level != null;
    }

    public List<LevelCollageSourceEntry> BuildCandidates(PoolEvalContext context, LevelCollageSourceDatabase fallbackSourceDatabase = null)
    {
        var candidates = new List<LevelCollageSourceEntry>();
        var database = ResolveSourceDatabase(fallbackSourceDatabase);
        if (!enabled || database?.entries == null || database.entries.Count == 0)
            return candidates;

        var filters = BuildFilters(context);
        foreach (var entry in database.entries)
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

        return candidates;
    }

    public PoolAnalysisResult Analyze(PoolEvalContext context)
    {
        return Analyze(context, null);
    }

    public PoolAnalysisResult Analyze(PoolEvalContext context, LevelCollageSourceDatabase fallbackSourceDatabase)
    {
        var database = ResolveSourceDatabase(fallbackSourceDatabase);
        var result = new PoolAnalysisResult
        {
            poolId = tableId,
            displayName = GetResolvedDisplayName()
        };

        if (database?.entries == null)
        {
            PoolAnalysisMath.Finalize(result);
            return result;
        }

        var filters = BuildFilters(context);
        for (int i = 0; i < database.entries.Count; i++)
        {
            var entry = database.entries[i];
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

    public LevelCollageSourceDatabase ResolveSourceDatabase(LevelCollageSourceDatabase fallbackSourceDatabase = null)
    {
        return sourceDatabase != null ? sourceDatabase : fallbackSourceDatabase;
    }

    private FeatureFilters BuildFilters(PoolEvalContext context)
    {
        var row = ResolveRow(context.routeLayer);
        if (row?.filter != null)
        {
            return BuildFilters(row.filter, GetRowDisplayName(row));
        }

        if (fallbackFilter != null)
            return BuildFilters(fallbackFilter, "Fallback");

        return new FeatureFilters
        {
            width = new Vector2(1, 50),
            height = new Vector2(1, 50),
            area = new Vector2(1, 2500),
            wallRate = new Vector2(0f, 1f),
            effectiveBoxes = new Vector2(0, 50),
            sourceLabel = "Built-in fallback"
        };
    }

    private static FeatureFilters BuildFilters(LevelFeatureFilterSO filter, string sourceLabel)
    {
        return new FeatureFilters
        {
            width = Normalize(filter.widthRange, 1f),
            height = Normalize(filter.heightRange, 1f),
            area = Normalize(filter.areaRange, 1f),
            wallRate = Normalize01(filter.wallRateRange),
            effectiveBoxes = Normalize(filter.effectiveBoxRange, 0f),
            sourceLabel = sourceLabel
        };
    }

    private Row ResolveRow(int roundIndex)
    {
        if (rows == null || rows.Count == 0)
            return null;

        Row best = null;
        int bestRound = int.MinValue;
        roundIndex = Mathf.Max(0, roundIndex);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null || !row.enabled)
                continue;

            int rowRound = Mathf.Max(0, row.roundIndex);
            if (rowRound > roundIndex || rowRound < bestRound)
                continue;

            best = row;
            bestRound = rowRound;
        }

        return best;
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

        return $"OK ({filters.sourceLabel})";
    }

    private static string GetRowDisplayName(Row row)
    {
        if (row != null && !string.IsNullOrWhiteSpace(row.label))
            return row.label;

        return row != null ? $"Round {Mathf.Max(0, row.roundIndex)}" : "Fallback";
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
        public string sourceLabel;

        public override string ToString()
        {
            return $"{sourceLabel}: W {width.x:0.#}-{width.y:0.#}, H {height.x:0.#}-{height.y:0.#}, Area {area.x:0.#}-{area.y:0.#}, Wall {wallRate.x:0.##}-{wallRate.y:0.##}, Eff {effectiveBoxes.x:0.#}-{effectiveBoxes.y:0.#}";
        }
    }
}
