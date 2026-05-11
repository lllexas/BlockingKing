using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "EscortGenerationConfig", menuName = "BlockingKing/Run/Escort Generation Config")]
public sealed class EscortGenerationConfigSO : TableBaseSO
{
    [System.Serializable]
    public sealed class Row
    {
        public bool enabled = true;

        [Min(1), TableColumnWidth(78), LabelText("Round")]
        [Tooltip("This row becomes active from this round index until a later enabled row overrides it.")]
        public int roundIndex = 1;

        [TableColumnWidth(120)]
        public string label;

        [MinMaxSlider(16, 200, true), LabelText("Distance")]
        public Vector2 manhattanDistanceRange = new(48, 96);

        [MinMaxSlider(1f, 89f, true), LabelText("Angle")]
        public Vector2 targetAngleRange = new(30f, 60f);

        [MinMaxSlider(0, 20, true), LabelText("Cities")]
        public Vector2 cityCountRange = new(2, 5);

        [MinMaxSlider(0, 50, true), LabelText("Enemies")]
        public Vector2 enemyCountRange = new(1, 8);

        [MinValue(-1), TableColumnWidth(92), LabelText("Reward Cap")]
        [Tooltip("-1 = unlimited final reward boxes. 0 or higher clamps the final reward box count.")]
        public int maxFinalRewardBoxes = -1;
    }

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
    [MinValue(0)] public int cityCountBase = 2;
    [MinValue(1)] public int cityCountDistanceStep = 8;
    public Vector2 logSlopeClamp = new(-3f, 3f);
    public Vector2 slopeClamp = new(0.05f, 20f);
    public Vector2Int cityPerpendicularJitter = new(-3, 3);
    public Vector2Int cityAlongJitter = new(-2, 2);
    [MinValue(0)] public int routeBoundsPadding = 0;
    [Tooltip("After cities are placed, carve one-tile-wide Manhattan corridors between adjacent city anchors.")]
    public bool connectCityCorridors = true;

    [Title("Enemy Layout")]
    [MinValue(0)] public int enemyCoreExclusionDistance = 5;
    [MinValue(0)] public int enemyMapEdgeMargin = 1;
    [MinValue(0)] public int enemyWallPocketNeighborThreshold = 2;
    [MinValue(1)] public int enemyDistanceDivisor = 4;

    [Title("Enemy Target Replacement")]
    [Range(0f, 1f)] public float enemyTargetRoomRate = 1f;
    [Range(0f, 1f)] public float enemyTargetReplacementRate = 0.5f;
    [MinValue(0)] public int minEnemyTargetsPerSelectedRoom = 1;

    [Title("Round Driven Rows")]
    [InfoBox("Rows are resolved by current round index: the latest enabled row with Round <= current round wins.")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 180)]
    public List<Row> rows = new();

    [Title("Dashboard")]
    [Min(1), LabelText("Preview Round")]
    public int previewRoundIndex = 1;

    [ShowInInspector, ReadOnly, LabelText("Resolved Row")]
    private string PreviewResolvedRow => BuildPreview(previewRoundIndex);

    public EscortGenerationResolvedConfig Resolve(PoolEvalContext context, System.Random random)
    {
        return ResolveByRoundIndex(context.routeLayer, random);
    }

    public EscortGenerationResolvedConfig ResolveByRoundIndex(int roundIndex, System.Random random)
    {
        var row = ResolveRow(roundIndex);
        var distanceRange = Normalize(row?.manhattanDistanceRange ?? new Vector2(48, 96), 1f);
        int distance = RollInclusive(random, Mathf.RoundToInt(distanceRange.x), Mathf.RoundToInt(distanceRange.y));
        var angleRange = Normalize(row?.targetAngleRange ?? new Vector2(30f, 60f), 1f);

        return new EscortGenerationResolvedConfig(
            ToConstraints(row, distance),
            distance,
            RollLogSlope(random, angleRange));
    }

    private EscortLevelGenerationConstraints ToConstraints(Row row, int distance)
    {
        var cityCount = Normalize(row?.cityCountRange ?? new Vector2(2, 5), 0f);
        var enemyCount = Normalize(row?.enemyCountRange ?? new Vector2(1, 8), 0f);
        var logSlopeRange = NormalizeUnbounded(logSlopeClamp);
        var slopeRange = Normalize(slopeClamp, 0.001f);
        var targetAngle = Normalize(row?.targetAngleRange ?? new Vector2(30f, 60f), 1f);

        return new EscortLevelGenerationConstraints
        {
            MinTemplateWidth = 1,
            MaxTemplateWidth = int.MaxValue,
            MinTemplateHeight = 1,
            MaxTemplateHeight = int.MaxValue,
            MinTemplateArea = 1,
            MaxTemplateArea = int.MaxValue,
            MinTemplateWallRate = 0f,
            MaxTemplateWallRate = 1f,
            MinTemplateEffectiveBoxes = 0,
            MaxTemplateEffectiveBoxes = int.MaxValue,
            MaxFinalRewardBoxes = row != null && row.maxFinalRewardBoxes >= 0 ? row.maxFinalRewardBoxes : int.MaxValue,

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
            DefaultManhattanDistance = Mathf.Max(1, distance),
            CityCountBase = Mathf.Max(0, cityCountBase),
            CityCountDistanceStep = Mathf.Max(1, cityCountDistanceStep),
            MinCityCount = Mathf.RoundToInt(cityCount.x),
            MaxCityCount = Mathf.RoundToInt(cityCount.y),
            MinLogSlope = logSlopeRange.x,
            MaxLogSlope = logSlopeRange.y,
            MinSlope = slopeRange.x,
            MaxSlope = slopeRange.y,
            MinTargetAngleDegrees = targetAngle.x,
            MaxTargetAngleDegrees = targetAngle.y,
            CityPerpendicularJitterMin = Mathf.Min(cityPerpendicularJitter.x, cityPerpendicularJitter.y),
            CityPerpendicularJitterMax = Mathf.Max(cityPerpendicularJitter.x, cityPerpendicularJitter.y),
            CityAlongJitterMin = Mathf.Min(cityAlongJitter.x, cityAlongJitter.y),
            CityAlongJitterMax = Mathf.Max(cityAlongJitter.x, cityAlongJitter.y),
            RouteBoundsPadding = Mathf.Max(0, routeBoundsPadding),
            ConnectCityCorridors = connectCityCorridors,

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

    private Row ResolveRow(int roundIndex)
    {
        if (rows == null || rows.Count == 0)
            return null;

        Row best = null;
        int bestRound = int.MinValue;
        roundIndex = Mathf.Max(1, roundIndex);

        foreach (var row in rows)
        {
            if (row == null || !row.enabled)
                continue;

            int rowRound = Mathf.Max(1, row.roundIndex);
            if (rowRound > roundIndex || rowRound < bestRound)
                continue;

            best = row;
            bestRound = rowRound;
        }

        return best;
    }

    private string BuildPreview(int roundIndex)
    {
        var row = ResolveRow(roundIndex);
        if (row == null)
            return "Built-in fallback";

        string label = string.IsNullOrWhiteSpace(row.label) ? $"Round {Mathf.Max(1, row.roundIndex)}" : row.label;
        return $"{label}: Distance {row.manhattanDistanceRange.x:0}-{row.manhattanDistanceRange.y:0}, Angle {row.targetAngleRange.x:0}-{row.targetAngleRange.y:0}, Cities {row.cityCountRange.x:0}-{row.cityCountRange.y:0}, Enemies {row.enemyCountRange.x:0}-{row.enemyCountRange.y:0}";
    }

    private static int RollInclusive(System.Random random, int min, int max)
    {
        if (min > max)
            (min, max) = (max, min);

        return random != null ? random.Next(min, max + 1) : UnityEngine.Random.Range(min, max + 1);
    }

    private static float RollLogSlope(System.Random random, Vector2 angleRange)
    {
        float t = random != null ? (float)random.NextDouble() : UnityEngine.Random.value;
        float angle = Mathf.Lerp(angleRange.x, angleRange.y, t);
        float slope = Mathf.Tan(Mathf.Clamp(angle, 0.1f, 89.9f) * Mathf.Deg2Rad);
        return Mathf.Log(Mathf.Max(0.001f, slope));
    }

    private static Vector2 Normalize(Vector2 value, float min)
    {
        float x = Mathf.Max(min, value.x);
        float y = Mathf.Max(min, value.y);
        return x <= y ? new Vector2(x, y) : new Vector2(y, x);
    }

    private static Vector2 NormalizeUnbounded(Vector2 value)
    {
        return value.x <= value.y ? value : new Vector2(value.y, value.x);
    }
}

public readonly struct EscortGenerationResolvedConfig
{
    public readonly EscortLevelGenerationConstraints Constraints;
    public readonly int ManhattanDistance;
    public readonly float LogSlope;

    public EscortGenerationResolvedConfig(
        EscortLevelGenerationConstraints constraints,
        int manhattanDistance,
        float logSlope)
    {
        Constraints = constraints;
        ManhattanDistance = manhattanDistance;
        LogSlope = logSlope;
    }
}
