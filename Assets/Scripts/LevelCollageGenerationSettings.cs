using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelCollageGenerationSettings", menuName = "BlockingKing/Level Collage Generation Settings")]
public class LevelCollageGenerationSettings : ScriptableObject
{
    [Title("Collage Source")]
    [AssetsOnly]
    [Tooltip("Only levels in this database can appear in Escort collage generation.")]
    public LevelCollageSourceDatabase sourceDatabase;

    [Title("Route Distance")]
    [MinMaxSlider(16, 200, true), LabelText("Escort Manhattan Distance")]
    public Vector2 escortManhattanDistanceRange = new(48, 96);

    [MinValue(1), LabelText("Layer Distance Unit")]
    public int routeLayerDistanceUnit = 24;

    [MinValue(1), LabelText("Lane Distance Unit")]
    public int routeLaneDistanceUnit = 12;

    [Title("Tile And Tag IDs")]
    [MinValue(1)] public int playerTagID = 1;
    [MinValue(1)] public int boxTagID = 2;
    [MinValue(1)] public int targetTagID = 3;
    [MinValue(1)] public int enemyTagID = 4;
    [MinValue(1)] public int coreBoxTagID = 5;
    [MinValue(1)] public int targetCoreTagID = 7;
    [MinValue(1)] public int targetEnemyTagID = 8;
    [MinValue(0)] public int wallTileID = 1;
    [MinValue(0)] public int floorTileID = 2;

    [Title("Fixed Escort Zones")]
    [MinValue(3)] public int fixedZoneSize = 5;
    [MinValue(0)] public int fixedZoneInnerInset = 1;
    public Vector2Int startZoneCenter = new(3, 3);
    public Vector2Int playerStartPosition = new(2, 2);

    [Title("City Layout")]
    [MinValue(0)] public int cityMargin = 2;
    [MinValue(1)] public int cityPlacementAttemptsPerCity = 48;
    [MinValue(1)] public int defaultManhattanDistance = 60;
    [MinValue(0)] public int cityCountBase = 2;
    [MinValue(1)] public int cityCountDistanceStep = 8;
    [MinMaxSlider(0, 20, true), LabelText("City Count")]
    public Vector2 cityCountRange = new(2, 5);
    public Vector2 logSlopeClamp = new(-3f, 3f);
    public Vector2 slopeClamp = new(0.05f, 20f);
    [MinMaxSlider(1f, 89f, true), LabelText("Target Angle")]
    public Vector2 targetAngleRange = new(30f, 60f);
    public Vector2Int cityPerpendicularJitter = new(-3, 3);
    public Vector2Int cityAlongJitter = new(-2, 2);
    [MinValue(0)] public int routeBoundsPadding = 0;

    [Title("Enemy Layout")]
    [MinValue(0)] public int enemyCoreExclusionDistance = 5;
    [MinValue(0)] public int enemyMapEdgeMargin = 1;
    [MinValue(0)] public int enemyWallPocketNeighborThreshold = 2;
    [MinValue(1)] public int enemyDistanceDivisor = 4;
    [MinMaxSlider(0, 50, true), LabelText("Enemy Count")]
    public Vector2 enemyCountRange = new(1, 8);

    [Title("Enemy Target Replacement")]
    [Range(0f, 1f)] public float enemyTargetRoomRate = 1f;
    [Range(0f, 1f)] public float enemyTargetReplacementRate = 0.5f;
    [MinValue(0)] public int minEnemyTargetsPerSelectedRoom = 1;

    [Title("Template Filters")]
    [MinMaxSlider(1, 50, true), LabelText("Width")]
    public Vector2 templateWidthRange = new(1, 18);

    [MinMaxSlider(1, 50, true), LabelText("Height")]
    public Vector2 templateHeightRange = new(1, 18);

    [MinMaxSlider(1, 2500, true), LabelText("Area")]
    public Vector2 templateAreaRange = new(1, 324);

    [MinMaxSlider(0f, 1f, true), LabelText("Wall Rate")]
    public Vector2 templateWallRateRange = new(0f, 1f);

    [MinMaxSlider(0, 50, true), LabelText("Effective Boxes")]
    public Vector2 templateEffectiveBoxRange = new(1, 5);

    [Title("Final Collage")]
    [MinValue(-1), LabelText("Max Final Reward Boxes (-1 = unlimited)")]
    public int maxFinalRewardBoxes = -1;

    [Title("Dashboard")]
    [ShowInInspector, ReadOnly, LabelText("Summary")]
    private string Summary => BuildSummary();

    [ShowInInspector, ReadOnly, ProgressBar(0, 1), LabelText("Candidate Hit Rate")]
    private float CandidateRatio => sourceDatabase == null || sourceDatabase.entries == null || sourceDatabase.entries.Count == 0
        ? 0f
        : CountMatchingEntries() / (float)Mathf.Max(1, CountEnabledEntries());

    [ShowInInspector, ReadOnly, LabelText("Enabled Sources")]
    private int EnabledSourceCount => CountEnabledEntries();

    [ShowInInspector, ReadOnly, LabelText("Matching Candidates")]
    private int MatchingCandidateCount => CountMatchingEntries();

    [ShowInInspector, ReadOnly, LabelText("Status")]
    private string Status => BuildStatus();

    [Title("Candidate Sample")]
    [TableList(AlwaysExpanded = false, DrawScrollView = true, MinScrollViewHeight = 120)]
    [ShowInInspector, ReadOnly]
    private List<CandidatePreviewRow> CandidatePreview => BuildCandidatePreview(12);

    public EscortLevelGenerationConstraints ToConstraints()
    {
        var widthRange = Normalize(templateWidthRange, 1f);
        var heightRange = Normalize(templateHeightRange, 1f);
        var areaRange = Normalize(templateAreaRange, 1f);
        var effectiveBoxRange = Normalize(templateEffectiveBoxRange, 0f);
        var wallRateRange = Normalize01(templateWallRateRange);
        var cityCount = Normalize(cityCountRange, 0f);
        var enemyCount = Normalize(enemyCountRange, 0f);
        var logSlopeRange = NormalizeUnbounded(logSlopeClamp);
        var slopeRange = Normalize(slopeClamp, 0.001f);
        var targetAngleRangeNormalized = Normalize(targetAngleRange, 1f);

        return new EscortLevelGenerationConstraints
        {
            MinTemplateWidth = Mathf.RoundToInt(widthRange.x),
            MaxTemplateWidth = Mathf.RoundToInt(widthRange.y),
            MinTemplateHeight = Mathf.RoundToInt(heightRange.x),
            MaxTemplateHeight = Mathf.RoundToInt(heightRange.y),
            MinTemplateArea = Mathf.RoundToInt(areaRange.x),
            MaxTemplateArea = Mathf.RoundToInt(areaRange.y),
            MinTemplateWallRate = wallRateRange.x,
            MaxTemplateWallRate = wallRateRange.y,
            MinTemplateEffectiveBoxes = Mathf.RoundToInt(effectiveBoxRange.x),
            MaxTemplateEffectiveBoxes = Mathf.RoundToInt(effectiveBoxRange.y),
            MaxFinalRewardBoxes = maxFinalRewardBoxes < 0 ? int.MaxValue : maxFinalRewardBoxes,

            PlayerTagID = Mathf.Max(1, playerTagID),
            BoxTagID = Mathf.Max(1, boxTagID),
            TargetTagID = Mathf.Max(1, targetTagID),
            EnemyTagID = Mathf.Max(1, enemyTagID),
            CoreBoxTagID = Mathf.Max(1, coreBoxTagID),
            TargetCoreTagID = Mathf.Max(1, targetCoreTagID),
            TargetEnemyTagID = Mathf.Max(1, targetEnemyTagID),
            WallTileID = Mathf.Max(0, wallTileID),
            FloorTileID = Mathf.Max(0, floorTileID),

            CityMargin = Mathf.Max(0, cityMargin),
            FixedZoneSize = Mathf.Max(3, fixedZoneSize),
            FixedZoneInnerInset = Mathf.Max(0, fixedZoneInnerInset),
            StartZoneCenter = startZoneCenter,
            PlayerStartPosition = playerStartPosition,

            CityPlacementAttemptsPerCity = Mathf.Max(1, cityPlacementAttemptsPerCity),
            DefaultManhattanDistance = Mathf.Max(1, defaultManhattanDistance),
            CityCountBase = Mathf.Max(0, cityCountBase),
            CityCountDistanceStep = Mathf.Max(1, cityCountDistanceStep),
            MinCityCount = Mathf.RoundToInt(cityCount.x),
            MaxCityCount = Mathf.RoundToInt(cityCount.y),
            MinLogSlope = logSlopeRange.x,
            MaxLogSlope = logSlopeRange.y,
            MinSlope = slopeRange.x,
            MaxSlope = slopeRange.y,
            MinTargetAngleDegrees = targetAngleRangeNormalized.x,
            MaxTargetAngleDegrees = targetAngleRangeNormalized.y,
            CityPerpendicularJitterMin = Mathf.Min(cityPerpendicularJitter.x, cityPerpendicularJitter.y),
            CityPerpendicularJitterMax = Mathf.Max(cityPerpendicularJitter.x, cityPerpendicularJitter.y),
            CityAlongJitterMin = Mathf.Min(cityAlongJitter.x, cityAlongJitter.y),
            CityAlongJitterMax = Mathf.Max(cityAlongJitter.x, cityAlongJitter.y),
            RouteBoundsPadding = Mathf.Max(0, routeBoundsPadding),

            EnemyCoreExclusionDistance = Mathf.Max(0, enemyCoreExclusionDistance),
            EnemyMapEdgeMargin = Mathf.Max(0, enemyMapEdgeMargin),
            EnemyWallPocketNeighborThreshold = Mathf.Max(0, enemyWallPocketNeighborThreshold),
            EnemyDistanceDivisor = Mathf.Max(1, enemyDistanceDivisor),
            MinEnemyCount = Mathf.RoundToInt(enemyCount.x),
            MaxEnemyCount = Mathf.RoundToInt(enemyCount.y),
            EnemyTargetRoomRate = Mathf.Clamp01(enemyTargetRoomRate),
            EnemyTargetReplacementRate = Mathf.Clamp01(enemyTargetReplacementRate),
            MinEnemyTargetsPerSelectedRoom = Mathf.Max(0, minEnemyTargetsPerSelectedRoom)
        };
    }

    public int ClampEscortManhattanDistance(int rawDistance)
    {
        var range = Normalize(escortManhattanDistanceRange, 1f);
        return Mathf.Clamp(rawDistance, Mathf.RoundToInt(range.x), Mathf.RoundToInt(range.y));
    }

    [Button(ButtonSizes.Large), HorizontalGroup("Presets")]
    private void MicrobanCalm()
    {
        templateWidthRange = new Vector2(5, 18);
        templateHeightRange = new Vector2(3, 18);
        templateAreaRange = new Vector2(40, 180);
        templateWallRateRange = new Vector2(0.30f, 0.60f);
        templateEffectiveBoxRange = new Vector2(1, 5);
        escortManhattanDistanceRange = new Vector2(48, 96);
        cityCountRange = new Vector2(2, 5);
        enemyCountRange = new Vector2(1, 8);
        targetAngleRange = new Vector2(30f, 60f);
        enemyTargetRoomRate = 1f;
        enemyTargetReplacementRate = 0.35f;
        maxFinalRewardBoxes = -1;
    }

    [Button(ButtonSizes.Large), HorizontalGroup("Presets")]
    private void MediumRooms()
    {
        templateWidthRange = new Vector2(6, 22);
        templateHeightRange = new Vector2(5, 22);
        templateAreaRange = new Vector2(60, 260);
        templateWallRateRange = new Vector2(0.25f, 0.65f);
        templateEffectiveBoxRange = new Vector2(3, 8);
        escortManhattanDistanceRange = new Vector2(64, 120);
        cityCountRange = new Vector2(4, 8);
        enemyCountRange = new Vector2(3, 12);
        targetAngleRange = new Vector2(30f, 60f);
        enemyTargetRoomRate = 1f;
        enemyTargetReplacementRate = 0.5f;
        maxFinalRewardBoxes = 18;
    }

    [Button(ButtonSizes.Large), HorizontalGroup("Presets")]
    private void BossChaos()
    {
        templateWidthRange = new Vector2(8, 32);
        templateHeightRange = new Vector2(8, 32);
        templateAreaRange = new Vector2(140, 650);
        templateWallRateRange = new Vector2(0.18f, 0.58f);
        templateEffectiveBoxRange = new Vector2(8, 24);
        escortManhattanDistanceRange = new Vector2(88, 160);
        cityCountRange = new Vector2(6, 12);
        enemyCountRange = new Vector2(8, 20);
        targetAngleRange = new Vector2(30f, 60f);
        enemyTargetRoomRate = 1f;
        enemyTargetReplacementRate = 0.7f;
        maxFinalRewardBoxes = 36;
    }

    [Button(ButtonSizes.Medium), HorizontalGroup("Tools")]
    private void ResetSafeDefault()
    {
        templateWidthRange = new Vector2(1, 18);
        templateHeightRange = new Vector2(1, 18);
        templateAreaRange = new Vector2(1, 324);
        templateWallRateRange = new Vector2(0f, 1f);
        templateEffectiveBoxRange = new Vector2(1, 5);
        escortManhattanDistanceRange = new Vector2(48, 96);
        routeLayerDistanceUnit = 24;
        routeLaneDistanceUnit = 12;
        playerTagID = 1;
        boxTagID = 2;
        targetTagID = 3;
        enemyTagID = 4;
        coreBoxTagID = 5;
        targetCoreTagID = 7;
        targetEnemyTagID = 8;
        wallTileID = 1;
        floorTileID = 2;
        fixedZoneSize = 5;
        fixedZoneInnerInset = 1;
        startZoneCenter = new Vector2Int(3, 3);
        playerStartPosition = new Vector2Int(2, 2);
        cityMargin = 2;
        cityPlacementAttemptsPerCity = 48;
        defaultManhattanDistance = 60;
        cityCountBase = 2;
        cityCountDistanceStep = 8;
        cityCountRange = new Vector2(2, 5);
        logSlopeClamp = new Vector2(-3f, 3f);
        slopeClamp = new Vector2(0.05f, 20f);
        targetAngleRange = new Vector2(30f, 60f);
        cityPerpendicularJitter = new Vector2Int(-3, 3);
        cityAlongJitter = new Vector2Int(-2, 2);
        routeBoundsPadding = 0;
        enemyCoreExclusionDistance = 5;
        enemyMapEdgeMargin = 1;
        enemyWallPocketNeighborThreshold = 2;
        enemyDistanceDivisor = 4;
        enemyCountRange = new Vector2(1, 8);
        enemyTargetRoomRate = 1f;
        enemyTargetReplacementRate = 0.5f;
        minEnemyTargetsPerSelectedRoom = 1;
        maxFinalRewardBoxes = -1;
    }

    [Button(ButtonSizes.Medium), HorizontalGroup("Tools")]
    private void NormalizeRanges()
    {
        templateWidthRange = Normalize(templateWidthRange, 1f);
        templateHeightRange = Normalize(templateHeightRange, 1f);
        templateAreaRange = Normalize(templateAreaRange, 1f);
        templateEffectiveBoxRange = Normalize(templateEffectiveBoxRange, 0f);
        templateWallRateRange = Normalize01(templateWallRateRange);
        escortManhattanDistanceRange = Normalize(escortManhattanDistanceRange, 1f);
        routeLayerDistanceUnit = Mathf.Max(1, routeLayerDistanceUnit);
        routeLaneDistanceUnit = Mathf.Max(1, routeLaneDistanceUnit);
        fixedZoneSize = Mathf.Max(3, fixedZoneSize);
        fixedZoneInnerInset = Mathf.Max(0, fixedZoneInnerInset);
        cityMargin = Mathf.Max(0, cityMargin);
        cityPlacementAttemptsPerCity = Mathf.Max(1, cityPlacementAttemptsPerCity);
        defaultManhattanDistance = Mathf.Max(1, defaultManhattanDistance);
        cityCountBase = Mathf.Max(0, cityCountBase);
        cityCountDistanceStep = Mathf.Max(1, cityCountDistanceStep);
        cityCountRange = Normalize(cityCountRange, 0f);
        logSlopeClamp = NormalizeUnbounded(logSlopeClamp);
        slopeClamp = Normalize(slopeClamp, 0.001f);
        targetAngleRange = Normalize(targetAngleRange, 1f);
        cityPerpendicularJitter = NormalizeInt(cityPerpendicularJitter);
        cityAlongJitter = NormalizeInt(cityAlongJitter);
        routeBoundsPadding = Mathf.Max(0, routeBoundsPadding);
        enemyCoreExclusionDistance = Mathf.Max(0, enemyCoreExclusionDistance);
        enemyMapEdgeMargin = Mathf.Max(0, enemyMapEdgeMargin);
        enemyWallPocketNeighborThreshold = Mathf.Max(0, enemyWallPocketNeighborThreshold);
        enemyDistanceDivisor = Mathf.Max(1, enemyDistanceDivisor);
        enemyCountRange = Normalize(enemyCountRange, 0f);
        enemyTargetRoomRate = Mathf.Clamp01(enemyTargetRoomRate);
        enemyTargetReplacementRate = Mathf.Clamp01(enemyTargetReplacementRate);
        minEnemyTargetsPerSelectedRoom = Mathf.Max(0, minEnemyTargetsPerSelectedRoom);
    }

    private string BuildSummary()
    {
        return $"Eff {templateEffectiveBoxRange.x}-{templateEffectiveBoxRange.y} | " +
               $"Size {templateWidthRange.x}-{templateWidthRange.y} x {templateHeightRange.x}-{templateHeightRange.y} | " +
               $"Area {templateAreaRange.x}-{templateAreaRange.y} | " +
               $"Wall {templateWallRateRange.x:P0}-{templateWallRateRange.y:P0} | " +
               $"Route {escortManhattanDistanceRange.x}-{escortManhattanDistanceRange.y} | " +
               $"Final Reward Cap {(maxFinalRewardBoxes < 0 ? "Unlimited" : maxFinalRewardBoxes.ToString())}";
    }

    private string BuildStatus()
    {
        if (sourceDatabase == null)
            return "No source database assigned.";

        int enabled = CountEnabledEntries();
        int matches = CountMatchingEntries();
        if (enabled == 0)
            return "No enabled source levels.";

        if (matches == 0)
            return "No candidates. Loosen filters or enable more source levels.";

        if (matches < 20)
            return $"Narrow candidate pool: {matches}/{enabled}.";

        return $"Healthy candidate pool: {matches}/{enabled}.";
    }

    private int CountEnabledEntries()
    {
        if (sourceDatabase?.entries == null)
            return 0;

        int count = 0;
        foreach (var entry in sourceDatabase.entries)
        {
            if (entry?.level != null && entry.enabled)
                count++;
        }

        return count;
    }

    private int CountMatchingEntries()
    {
        if (sourceDatabase?.entries == null)
            return 0;

        var constraints = ToConstraints();
        int count = 0;
        foreach (var entry in sourceDatabase.entries)
        {
            if (entry?.level == null || !entry.enabled)
                continue;

            if (constraints.Passes(ToFeatures(entry)))
                count++;
        }

        return count;
    }

    private List<CandidatePreviewRow> BuildCandidatePreview(int maxRows)
    {
        var result = new List<CandidatePreviewRow>();
        if (sourceDatabase?.entries == null)
            return result;

        var constraints = ToConstraints();
        foreach (var entry in sourceDatabase.entries)
        {
            if (entry?.level == null || !entry.enabled)
                continue;

            if (!constraints.Passes(ToFeatures(entry)))
                continue;

            result.Add(new CandidatePreviewRow(entry));
            if (result.Count >= maxRows)
                break;
        }

        return result;
    }

    private static EscortLevelTemplateFeatures ToFeatures(LevelCollageSourceEntry entry)
    {
        return new EscortLevelTemplateFeatures(
            entry.width,
            entry.height,
            entry.wallCount,
            entry.boxCount,
            entry.boxOnTargetCount);
    }

    private static Vector2 Normalize(Vector2 value, float min = 0f)
    {
        float x = Mathf.Max(min, value.x);
        float y = Mathf.Max(min, value.y);
        return x <= y ? new Vector2(x, y) : new Vector2(y, x);
    }

    private static Vector2 NormalizeUnbounded(Vector2 value)
    {
        return value.x <= value.y ? value : new Vector2(value.y, value.x);
    }

    private static Vector2Int NormalizeInt(Vector2Int value)
    {
        return value.x <= value.y ? value : new Vector2Int(value.y, value.x);
    }

    private static Vector2 Normalize01(Vector2 value)
    {
        float x = Mathf.Clamp01(value.x);
        float y = Mathf.Clamp01(value.y);
        return x <= y ? new Vector2(x, y) : new Vector2(y, x);
    }

    [Serializable]
    private sealed class CandidatePreviewRow
    {
        [ReadOnly] public string name;
        [ReadOnly] public string group;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public int effectiveBoxes;
        [ReadOnly] public float wallRate;

        public CandidatePreviewRow(LevelCollageSourceEntry entry)
        {
            name = entry.level != null ? entry.level.name : "Missing";
            group = entry.sourceGroup;
            width = entry.width;
            height = entry.height;
            effectiveBoxes = entry.effectiveBoxCount;
            wallRate = entry.wallRate;
        }
    }
}
