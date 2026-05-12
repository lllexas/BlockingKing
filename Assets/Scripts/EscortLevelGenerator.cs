using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EscortLevelBuildRequest
{
    public int Seed;
    public int ManhattanDistance;
    public float LogSlope;
    public int DifficultyOffset;
    public PoolEvalContext Context = PoolEvalContext.Default;
    public EscortLevelGenerationConstraints Constraints = EscortLevelGenerationConstraints.Default;
    public LevelCollageSourceDatabase SourceDatabase;
    public IReadOnlyList<LevelCollageSourceEntry> SourceEntries;
}

public readonly struct EscortLevelTemplateFeatures
{
    public readonly int Width;
    public readonly int Height;
    public readonly int Area;
    public readonly int WallCount;
    public readonly int BoxCount;
    public readonly int BoxOnTargetCount;
    public readonly int EffectiveBoxCount;
    public readonly float WallRate;

    public EscortLevelTemplateFeatures(
        int width,
        int height,
        int wallCount,
        int boxCount,
        int boxOnTargetCount)
    {
        Width = width;
        Height = height;
        Area = Mathf.Max(1, width * height);
        WallCount = wallCount;
        BoxCount = boxCount;
        BoxOnTargetCount = boxOnTargetCount;
        EffectiveBoxCount = Mathf.Max(0, boxCount - boxOnTargetCount);
        WallRate = wallCount / (float)Area;
    }
}

public sealed class EscortLevelGenerationConstraints
{
    public int PlayerTagID = 1;
    public int BoxTagID = 2;
    public int TargetTagID = 3;
    public int EnemyTagID = 4;
    public int CoreBoxTagID = 5;
    public int TargetCoreTagID = 7;
    public int TargetEnemyTagID = 8;
    public int WallTileID = 1;
    public int FloorTileID = 2;

    public int MinTemplateWidth = 1;
    public int MaxTemplateWidth = 18;
    public int MinTemplateHeight = 1;
    public int MaxTemplateHeight = 18;
    public int MinTemplateArea = 1;
    public int MaxTemplateArea = int.MaxValue;
    public float MinTemplateWallRate = 0f;
    public float MaxTemplateWallRate = 1f;
    public int MinTemplateEffectiveBoxes = 1;
    public int MaxTemplateEffectiveBoxes = 5;
    public int MaxFinalRewardBoxes = int.MaxValue;

    public int CityMargin = 2;
    public int FixedZoneSize = 5;
    public int FixedZoneInnerInset = 1;
    public Vector2Int StartZoneCenter = new(3, 3);
    public Vector2Int PlayerStartPosition = new(2, 2);

    public int CityPlacementAttemptsPerCity = 48;
    public int DefaultManhattanDistance = 60;
    public int CityCountBase = 2;
    public int CityCountDistanceStep = 8;
    public int MinCityCount = 2;
    public int MaxCityCount = 5;
    public float MinLogSlope = -3f;
    public float MaxLogSlope = 3f;
    public float MinSlope = 0.05f;
    public float MaxSlope = 20f;
    public float MinTargetAngleDegrees = 30f;
    public float MaxTargetAngleDegrees = 60f;
    public int CityPerpendicularJitterMin = -3;
    public int CityPerpendicularJitterMax = 3;
    public int CityAlongJitterMin = -2;
    public int CityAlongJitterMax = 2;
    public int RouteBoundsPadding = 0;
    public bool ConnectCityCorridors = true;

    public int EnemyCoreExclusionDistance = 5;
    public int EnemyMapEdgeMargin = 1;
    public int EnemyWallPocketNeighborThreshold = 2;
    public int EnemyDistanceDivisor = 4;
    public int MinEnemyCount = 1;
    public int MaxEnemyCount = 8;
    public float EnemyTargetRoomRate = 0.35f;
    public float EnemyTargetReplacementRate = 0.5f;
    public int MinEnemyTargetsPerSelectedRoom = 1;

    public static EscortLevelGenerationConstraints Default => new();

    public bool Passes(EscortLevelTemplateFeatures features)
    {
        return features.Width >= MinTemplateWidth &&
               features.Width <= MaxTemplateWidth &&
               features.Height >= MinTemplateHeight &&
               features.Height <= MaxTemplateHeight &&
               features.Area >= MinTemplateArea &&
               features.Area <= MaxTemplateArea &&
               features.WallRate >= MinTemplateWallRate &&
               features.WallRate <= MaxTemplateWallRate &&
               features.EffectiveBoxCount >= MinTemplateEffectiveBoxes &&
               features.EffectiveBoxCount <= MaxTemplateEffectiveBoxes;
    }
}

public static class EscortLevelGenerator
{
    private sealed class CityStamp
    {
        public LevelData Template;
        public RectInt Bounds;
        public Vector2Int Center => new(Bounds.x + Bounds.width / 2, Bounds.y + Bounds.height / 2);
    }

    private sealed class FixedEscortZone
    {
        public RectInt Bounds;
        public Vector2Int Center;
    }

    private sealed class EscortLayout
    {
        public FixedEscortZone StartZone;
        public FixedEscortZone TargetZone;
        public RectInt RouteCenterBounds;
        public List<CityStamp> Cities = new();
    }

    public static LevelData CreateFromRandomClassicMap(EscortLevelBuildRequest request)
    {
        var constraints = request?.Constraints ?? EscortLevelGenerationConstraints.Default;
        var usableTemplates = FilterUsableTemplates(request?.SourceDatabase, request?.SourceEntries, constraints);
        if (usableTemplates.Count == 0)
        {
            Debug.LogWarning("[EscortLevelGenerator] No collage source level found for current constraints. Assign and bake a LevelCollageSourceDatabase.");
            return null;
        }

        var random = new System.Random(request != null ? request.Seed : Environment.TickCount);

        int distance = Mathf.Max(1, request?.ManhattanDistance ?? constraints.DefaultManhattanDistance);
        int cityCount = ResolveCityCount(distance, constraints);
        var layout = BuildEscortLayout(
            usableTemplates,
            random,
            cityCount,
            distance,
            request?.LogSlope ?? 0f,
            constraints);
        if (layout.Cities.Count == 0)
            return null;

        LevelData level = BuildCompositeLevel(layout, constraints, random);
        TrimFinalRewardBoxes(level, random, constraints.MaxFinalRewardBoxes, constraints);
        AddEnemies(level, random, layout.StartZone.Center, ResolveEnemyCount(request, constraints), constraints);

        Debug.Log($"[EscortLevelGenerator] Built {level.levelName}, cities={layout.Cities.Count}, rewardBoxes={CountTags(level.tags, constraints.BoxTagID)}, candidates={usableTemplates.Count}, distance={distance}, anchorDistance={Manhattan(layout.StartZone.Center, layout.TargetZone.Center)}, logSlope={request?.LogSlope ?? 0f:0.###}");
        return level;
    }

    private static List<LevelData> FilterUsableTemplates(
        LevelCollageSourceDatabase database,
        IReadOnlyList<LevelCollageSourceEntry> sourceEntries,
        EscortLevelGenerationConstraints constraints)
    {
        var result = new List<LevelData>();
        var entries = sourceEntries ?? database?.entries;
        if (entries == null)
            return result;

        foreach (var entry in entries)
        {
            if (entry == null || !entry.enabled)
                continue;

            LevelData template = entry.level;
            if (template == null || template.width <= 0 || template.height <= 0)
                continue;

            if (template.tiles == null || template.tiles.Length != template.width * template.height)
                continue;

            EscortLevelTemplateFeatures cachedFeatures = ToFeatures(entry);
            if (!constraints.Passes(cachedFeatures))
                continue;

            EscortLevelTemplateFeatures runtimeFeatures = AnalyzeTemplate(template, constraints);
            if (!constraints.Passes(runtimeFeatures))
                continue;

            int weight = Mathf.Max(1, entry.manualWeight);
            for (int i = 0; i < weight; i++)
                result.Add(template);
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

    private static EscortLevelTemplateFeatures AnalyzeTemplate(LevelData level, EscortLevelGenerationConstraints constraints)
    {
        var metrics = LevelFeatureMetricsUtility.Analyze(
            level,
            constraints.WallTileID,
            constraints.BoxTagID,
            constraints.TargetTagID);

        return new EscortLevelTemplateFeatures(
            metrics.Width,
            metrics.Height,
            metrics.WallCount,
            metrics.BoxCount,
            metrics.BoxOnTargetCount);
    }

    private static int ResolveCityCount(int manhattanDistance, EscortLevelGenerationConstraints constraints)
    {
        int step = Mathf.Max(1, constraints.CityCountDistanceStep);
        return Mathf.Clamp(
            constraints.CityCountBase + manhattanDistance / step,
            constraints.MinCityCount,
            Mathf.Max(constraints.MinCityCount, constraints.MaxCityCount));
    }

    private static EscortLayout BuildEscortLayout(
        IReadOnlyList<LevelData> templates,
        System.Random random,
        int cityCount,
        int manhattanDistance,
        float logSlope,
        EscortLevelGenerationConstraints constraints)
    {
        var layout = new EscortLayout
        {
            StartZone = CreateFixedZone(constraints.StartZoneCenter, constraints),
            TargetZone = CreateFixedZone(ResolveTargetZoneCenter(manhattanDistance, logSlope, constraints), constraints)
        };
        layout.RouteCenterBounds = BuildRouteCenterBounds(layout.StartZone, layout.TargetZone, constraints);

        for (int i = 0; i < cityCount; i++)
        {
            if (TryCreateCityStamp(templates, layout, random, i, cityCount, constraints, out var stamp))
                layout.Cities.Add(stamp);
        }

        return layout;
    }

    private static RectInt BuildRouteCenterBounds(
        FixedEscortZone startZone,
        FixedEscortZone targetZone,
        EscortLevelGenerationConstraints constraints)
    {
        int padding = Mathf.Max(0, constraints.RouteBoundsPadding);
        int xMin = Mathf.Min(startZone.Center.x, targetZone.Center.x) - padding;
        int yMin = Mathf.Min(startZone.Center.y, targetZone.Center.y) - padding;
        int xMax = Mathf.Max(startZone.Center.x, targetZone.Center.x) + padding + 1;
        int yMax = Mathf.Max(startZone.Center.y, targetZone.Center.y) + padding + 1;
        int clampedXMin = Mathf.Max(0, xMin);
        int clampedYMin = Mathf.Max(0, yMin);
        return new RectInt(clampedXMin, clampedYMin, xMax - clampedXMin, yMax - clampedYMin);
    }

    private static FixedEscortZone CreateFixedZone(Vector2Int center, EscortLevelGenerationConstraints constraints)
    {
        int size = Mathf.Max(3, constraints.FixedZoneSize);
        return new FixedEscortZone
        {
            Center = center,
            Bounds = new RectInt(
                center.x - size / 2,
                center.y - size / 2,
                size,
                size)
        };
    }

    private static Vector2Int ResolveTargetZoneCenter(
        int manhattanDistance,
        float logSlope,
        EscortLevelGenerationConstraints constraints)
    {
        int distance = Mathf.Max(1, manhattanDistance);
        float minLogSlope = Mathf.Min(constraints.MinLogSlope, constraints.MaxLogSlope);
        float maxLogSlope = Mathf.Max(constraints.MinLogSlope, constraints.MaxLogSlope);
        float minSlope = Mathf.Min(constraints.MinSlope, constraints.MaxSlope);
        float maxSlope = Mathf.Max(constraints.MinSlope, constraints.MaxSlope);
        float minAngle = Mathf.Clamp(Mathf.Min(constraints.MinTargetAngleDegrees, constraints.MaxTargetAngleDegrees), 0.1f, 89.9f);
        float maxAngle = Mathf.Clamp(Mathf.Max(constraints.MinTargetAngleDegrees, constraints.MaxTargetAngleDegrees), 0.1f, 89.9f);
        float angleMinSlope = Mathf.Tan(minAngle * Mathf.Deg2Rad);
        float angleMaxSlope = Mathf.Tan(maxAngle * Mathf.Deg2Rad);
        minSlope = Mathf.Max(minSlope, Mathf.Min(angleMinSlope, angleMaxSlope));
        maxSlope = Mathf.Min(maxSlope, Mathf.Max(angleMinSlope, angleMaxSlope));
        if (minSlope > maxSlope)
            (minSlope, maxSlope) = (maxSlope, minSlope);

        float slope = Mathf.Clamp(Mathf.Exp(Mathf.Clamp(logSlope, minLogSlope, maxLogSlope)), minSlope, maxSlope);
        int dx = Mathf.Clamp(Mathf.RoundToInt(distance / (1f + slope)), 1, distance - 1);
        int dy = Mathf.Max(0, distance - dx);

        int fixedZoneSize = Mathf.Max(3, constraints.FixedZoneSize);
        if (dx < fixedZoneSize && dy < fixedZoneSize)
        {
            int needed = fixedZoneSize - Mathf.Max(dx, dy);
            if (dx >= dy)
            {
                dx += needed;
                dy = Mathf.Max(0, dy - needed);
            }
            else
            {
                dy += needed;
                dx = Mathf.Max(0, dx - needed);
            }
        }

        return constraints.StartZoneCenter + new Vector2Int(dx, dy);
    }

    private static bool TryCreateCityStamp(
        IReadOnlyList<LevelData> templates,
        EscortLayout layout,
        System.Random random,
        int index,
        int cityCount,
        EscortLevelGenerationConstraints constraints,
        out CityStamp stamp)
    {
        for (int attempt = 0; attempt < Mathf.Max(1, constraints.CityPlacementAttemptsPerCity); attempt++)
        {
            LevelData template = templates[random.Next(templates.Count)];
            Vector2Int routePoint = ResolveCityRoutePoint(layout, index, cityCount, random, constraints);
            var origin = new Vector2Int(
                Mathf.Max(0, routePoint.x - template.width / 2),
                Mathf.Max(0, routePoint.y - template.height / 2));
            var bounds = new RectInt(origin.x, origin.y, template.width, template.height);

            if (!ContainsPoint(layout.RouteCenterBounds, CenterOf(bounds)))
                continue;

            if (OverlapsProtectedArea(bounds, layout, constraints))
                continue;

            stamp = new CityStamp { Template = template, Bounds = bounds };
            return true;
        }

        for (int i = 0; i < templates.Count; i++)
        {
            LevelData template = templates[random.Next(templates.Count)];
            Vector2Int routePoint = ResolveCityRoutePoint(layout, index, cityCount, random, constraints);
            if (TryFindFallbackCityBounds(template, layout, routePoint, constraints, out var bounds))
            {
                stamp = new CityStamp { Template = template, Bounds = bounds };
                return true;
            }
        }

        stamp = null;
        return false;
    }

    private static bool TryFindFallbackCityBounds(
        LevelData template,
        EscortLayout layout,
        Vector2Int routePoint,
        EscortLevelGenerationConstraints constraints,
        out RectInt bounds)
    {
        bounds = default;
        int bestDistance = int.MaxValue;
        int originXMin = Mathf.Max(0, layout.RouteCenterBounds.xMin - template.width + 1);
        int originYMin = Mathf.Max(0, layout.RouteCenterBounds.yMin - template.height + 1);
        int originXMax = Mathf.Max(originXMin, layout.RouteCenterBounds.xMax - 1);
        int originYMax = Mathf.Max(originYMin, layout.RouteCenterBounds.yMax - 1);

        for (int y = originYMin; y <= originYMax; y++)
        {
            for (int x = originXMin; x <= originXMax; x++)
            {
                var candidate = new RectInt(x, y, template.width, template.height);
                var center = CenterOf(candidate);
                if (!ContainsPoint(layout.RouteCenterBounds, center))
                    continue;

                if (OverlapsProtectedArea(candidate, layout, constraints))
                    continue;

                int distance = Manhattan(center, routePoint);
                if (distance >= bestDistance)
                    continue;

                bounds = candidate;
                bestDistance = distance;
            }
        }

        return bestDistance < int.MaxValue;
    }

    private static Vector2Int ResolveCityRoutePoint(
        EscortLayout layout,
        int index,
        int cityCount,
        System.Random random,
        EscortLevelGenerationConstraints constraints)
    {
        float t = (index + 1) / (float)(cityCount + 1);
        var point = new Vector2Int(
            Mathf.RoundToInt(Mathf.Lerp(layout.StartZone.Center.x, layout.TargetZone.Center.x, t)),
            Mathf.RoundToInt(Mathf.Lerp(layout.StartZone.Center.y, layout.TargetZone.Center.y, t)));

        Vector2Int direction = layout.TargetZone.Center - layout.StartZone.Center;
        Vector2Int perpendicular = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? Vector2Int.up
            : Vector2Int.right;
        if (random.Next(0, 2) == 0)
            perpendicular *= -1;

        Vector2Int along = direction.x >= direction.y ? Vector2Int.right : Vector2Int.up;
        return point +
               perpendicular * NextInclusive(random, constraints.CityPerpendicularJitterMin, constraints.CityPerpendicularJitterMax) +
               along * NextInclusive(random, constraints.CityAlongJitterMin, constraints.CityAlongJitterMax);
    }

    private static bool OverlapsProtectedArea(RectInt bounds, EscortLayout layout, EscortLevelGenerationConstraints constraints)
    {
        int cityMargin = Mathf.Max(0, constraints.CityMargin);
        if (bounds.Overlaps(Inflate(layout.StartZone.Bounds, cityMargin)) ||
            bounds.Overlaps(Inflate(layout.TargetZone.Bounds, cityMargin)))
            return true;

        RectInt paddedBounds = Inflate(bounds, cityMargin);
        for (int i = 0; i < layout.Cities.Count; i++)
        {
            if (paddedBounds.Overlaps(Inflate(layout.Cities[i].Bounds, cityMargin)))
                return true;
        }

        return false;
    }

    private static RectInt Inflate(RectInt rect, int amount)
    {
        return new RectInt(rect.x - amount, rect.y - amount, rect.width + amount * 2, rect.height + amount * 2);
    }

    private static Vector2Int CenterOf(RectInt rect)
    {
        return new Vector2Int(rect.x + rect.width / 2, rect.y + rect.height / 2);
    }

    private static bool ContainsPoint(RectInt container, Vector2Int point)
    {
        return point.x >= container.xMin &&
               point.y >= container.yMin &&
               point.x < container.xMax &&
               point.y < container.yMax;
    }

    private static LevelData BuildCompositeLevel(
        EscortLayout layout,
        EscortLevelGenerationConstraints constraints,
        System.Random random)
    {
        RectInt extents = Union(layout.StartZone.Bounds, layout.TargetZone.Bounds);
        for (int i = 0; i < layout.Cities.Count; i++)
            extents = Union(extents, layout.Cities[i].Bounds);

        LevelData level = UnityEngine.ScriptableObject.CreateInstance<LevelData>();
        level.name = $"Escort_Collage_{layout.Cities[0].Template.name}";
        level.levelName = $"Escort Collage {layout.Cities[0].Template.levelName}";
        int cityMargin = Mathf.Max(0, constraints.CityMargin);
        level.width = extents.xMax + cityMargin;
        level.height = extents.yMax + cityMargin;
        level.tiles = new int[level.width * level.height];
        level.tags = new List<LevelTagEntry>();

        for (int i = 0; i < level.tiles.Length; i++)
            level.tiles[i] = constraints.WallTileID;

        foreach (var stamp in layout.Cities)
            StampTemplate(level, stamp, constraints);

        CarveCityCorridors(level, layout, random, constraints);
        CancelBoxOnTargetTags(level, constraints);
        ReplaceRoomTargetsWithEnemyTargets(level, layout.Cities, random, constraints);
        WriteFixedEscortZone(level, layout.StartZone, constraints.CoreBoxTagID, constraints);
        WriteFixedEscortZone(level, layout.TargetZone, constraints.TargetCoreTagID, constraints);
        level.tags.Add(new LevelTagEntry { tagID = constraints.PlayerTagID, x = constraints.PlayerStartPosition.x, y = constraints.PlayerStartPosition.y });
        return level;
    }

    private static RectInt Union(RectInt a, RectInt b)
    {
        int xMin = Mathf.Min(a.xMin, b.xMin);
        int yMin = Mathf.Min(a.yMin, b.yMin);
        int xMax = Mathf.Max(a.xMax, b.xMax);
        int yMax = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    private static void StampTemplate(LevelData target, CityStamp stamp, EscortLevelGenerationConstraints constraints)
    {
        LevelData template = stamp.Template;
        for (int y = 0; y < template.height; y++)
        {
            for (int x = 0; x < template.width; x++)
                target.SetTile(stamp.Bounds.x + x, stamp.Bounds.y + y, template.GetTile(x, y));
        }

        if (template.tags == null)
            return;

        foreach (var tag in template.tags)
        {
            if (tag == null)
                continue;

            if (tag.tagID == constraints.PlayerTagID ||
                tag.tagID == constraints.CoreBoxTagID ||
                tag.tagID == constraints.TargetCoreTagID)
                continue;

            target.tags.Add(new LevelTagEntry
            {
                tagID = tag.tagID,
                x = stamp.Bounds.x + tag.x,
                y = stamp.Bounds.y + tag.y
            });
        }
    }

    private static void CarveCityCorridors(
        LevelData level,
        EscortLayout layout,
        System.Random random,
        EscortLevelGenerationConstraints constraints)
    {
        if (!constraints.ConnectCityCorridors)
            return;

        var anchors = new List<Vector2Int> { layout.StartZone.Center };
        var cities = new List<CityStamp>(layout.Cities);
        cities.Sort((a, b) =>
        {
            int da = Manhattan(a.Center, layout.StartZone.Center);
            int db = Manhattan(b.Center, layout.StartZone.Center);
            if (da != db)
                return da.CompareTo(db);

            return a.Center.x != b.Center.x
                ? a.Center.x.CompareTo(b.Center.x)
                : a.Center.y.CompareTo(b.Center.y);
        });

        for (int i = 0; i < cities.Count; i++)
            anchors.Add(cities[i].Center);
        anchors.Add(layout.TargetZone.Center);

        for (int i = 0; i < anchors.Count - 1; i++)
            CarveSingleCorridor(level, anchors[i], anchors[i + 1], random, constraints);
    }

    private static void CarveSingleCorridor(
        LevelData level,
        Vector2Int from,
        Vector2Int to,
        System.Random random,
        EscortLevelGenerationConstraints constraints)
    {
        if (from.x == to.x || from.y == to.y)
        {
            CarveStraightCorridor(level, from, to, constraints);
            return;
        }

        bool horizontalFirst = random == null || random.Next(0, 2) == 0;
        var corner = horizontalFirst
            ? new Vector2Int(to.x, from.y)
            : new Vector2Int(from.x, to.y);

        CarveStraightCorridor(level, from, corner, constraints);
        CarveStraightCorridor(level, corner, to, constraints);
    }

    private static void CarveStraightCorridor(
        LevelData level,
        Vector2Int from,
        Vector2Int to,
        EscortLevelGenerationConstraints constraints)
    {
        int dx = Math.Sign(to.x - from.x);
        int dy = Math.Sign(to.y - from.y);
        var pos = from;

        CarveCorridorCell(level, pos, constraints);
        while (pos != to)
        {
            pos += new Vector2Int(dx, dy);
            CarveCorridorCell(level, pos, constraints);
        }
    }

    private static void CarveCorridorCell(
        LevelData level,
        Vector2Int pos,
        EscortLevelGenerationConstraints constraints)
    {
        if (pos.x < 0 || pos.y < 0 || pos.x >= level.width || pos.y >= level.height)
            return;

        if (level.GetTile(pos.x, pos.y) == constraints.WallTileID)
            level.SetTile(pos.x, pos.y, constraints.FloorTileID);
    }

    private static void WriteFixedEscortZone(
        LevelData level,
        FixedEscortZone zone,
        int coreTagID,
        EscortLevelGenerationConstraints constraints)
    {
        int inset = Mathf.Clamp(constraints.FixedZoneInnerInset, 0, Mathf.Max(0, constraints.FixedZoneSize / 2));
        int innerXMin = zone.Bounds.xMin + inset;
        int innerYMin = zone.Bounds.yMin + inset;
        int innerXMax = zone.Bounds.xMax - inset - 1;
        int innerYMax = zone.Bounds.yMax - inset - 1;
        for (int y = zone.Bounds.yMin; y < zone.Bounds.yMax; y++)
        {
            for (int x = zone.Bounds.xMin; x < zone.Bounds.xMax; x++)
            {
                bool isInner = x >= innerXMin && x <= innerXMax && y >= innerYMin && y <= innerYMax;
                level.SetTile(x, y, isInner ? constraints.FloorTileID : constraints.WallTileID);
                level.ClearTagsAt(x, y);
            }
        }

        level.tags.Add(new LevelTagEntry { tagID = coreTagID, x = zone.Center.x, y = zone.Center.y });
    }

    private static void CancelBoxOnTargetTags(LevelData level, EscortLevelGenerationConstraints constraints)
    {
        if (level.tags == null || level.tags.Count == 0)
            return;

        var boxOnTargetCells = new HashSet<Vector2Int>();
        var boxCells = new HashSet<Vector2Int>();
        var targetCells = new HashSet<Vector2Int>();

        foreach (var tag in level.tags)
        {
            if (tag == null)
                continue;

            var pos = new Vector2Int(tag.x, tag.y);
            if (tag.tagID == constraints.BoxTagID)
            {
                boxCells.Add(pos);
                if (targetCells.Contains(pos))
                    boxOnTargetCells.Add(pos);
            }
            else if (tag.tagID == constraints.TargetTagID)
            {
                targetCells.Add(pos);
                if (boxCells.Contains(pos))
                    boxOnTargetCells.Add(pos);
            }
        }

        if (boxOnTargetCells.Count == 0)
            return;

        level.tags.RemoveAll(tag =>
            tag != null &&
            boxOnTargetCells.Contains(new Vector2Int(tag.x, tag.y)) &&
            (tag.tagID == constraints.BoxTagID || tag.tagID == constraints.TargetTagID));
    }

    private static void ReplaceRoomTargetsWithEnemyTargets(
        LevelData level,
        IReadOnlyList<CityStamp> cities,
        System.Random random,
        EscortLevelGenerationConstraints constraints)
    {
        if (level.tags == null || level.tags.Count == 0 || constraints.TargetEnemyTagID <= 0)
            return;

        float roomRate = Mathf.Clamp01(constraints.EnemyTargetRoomRate);
        float replacementRate = Mathf.Clamp01(constraints.EnemyTargetReplacementRate);
        if (roomRate <= 0f || replacementRate <= 0f)
            return;

        foreach (var city in cities)
        {
            if (random.NextDouble() > roomRate)
                continue;

            var targetTags = new List<LevelTagEntry>();
            foreach (var tag in level.tags)
            {
                if (tag == null || tag.tagID != constraints.TargetTagID)
                    continue;

                if (city.Bounds.Contains(new Vector2Int(tag.x, tag.y)))
                    targetTags.Add(tag);
            }

            if (targetTags.Count == 0)
                continue;

            Shuffle(targetTags, random);
            int replaceCount = Mathf.RoundToInt(targetTags.Count * replacementRate);
            replaceCount = Mathf.Clamp(
                replaceCount,
                Mathf.Min(targetTags.Count, Mathf.Max(0, constraints.MinEnemyTargetsPerSelectedRoom)),
                targetTags.Count);

            for (int i = 0; i < replaceCount; i++)
                targetTags[i].tagID = constraints.TargetEnemyTagID;
        }
    }

    private static void TrimFinalRewardBoxes(
        LevelData level,
        System.Random random,
        int maxRewardBoxes,
        EscortLevelGenerationConstraints constraints)
    {
        if (maxRewardBoxes == int.MaxValue || maxRewardBoxes < 0 || level.tags == null)
            return;

        var boxes = FindTags(level.tags, constraints.BoxTagID);
        int removeCount = boxes.Count - maxRewardBoxes;
        if (removeCount <= 0)
            return;

        Shuffle(boxes, random);
        var removeCells = new HashSet<Vector2Int>();
        for (int i = 0; i < removeCount && i < boxes.Count; i++)
            removeCells.Add(new Vector2Int(boxes[i].x, boxes[i].y));

        level.tags.RemoveAll(tag =>
            tag != null &&
            tag.tagID == constraints.BoxTagID &&
            removeCells.Contains(new Vector2Int(tag.x, tag.y)));
    }

    private static void AddEnemies(
        LevelData level,
        System.Random random,
        Vector2Int corePosition,
        int count,
        EscortLevelGenerationConstraints constraints)
    {
        var candidates = new List<Vector2Int>();
        for (int y = 0; y < level.height; y++)
        {
            for (int x = 0; x < level.width; x++)
            {
                var pos = new Vector2Int(x, y);
                if (!IsWalkableEmpty(level, pos, constraints))
                    continue;

                if (!IsNearMapEdge(level, pos, constraints) && !IsNearWallPocket(level, pos, constraints))
                    continue;

                if (Manhattan(pos, corePosition) < constraints.EnemyCoreExclusionDistance)
                    continue;

                candidates.Add(pos);
            }
        }

        Shuffle(candidates, random);
        int actualCount = Mathf.Min(count, candidates.Count);
        for (int i = 0; i < actualCount; i++)
            level.tags.Add(new LevelTagEntry { tagID = constraints.EnemyTagID, x = candidates[i].x, y = candidates[i].y });
    }

    private static bool IsNearMapEdge(LevelData level, Vector2Int pos, EscortLevelGenerationConstraints constraints)
    {
        int margin = Mathf.Max(0, constraints.EnemyMapEdgeMargin);
        return pos.x <= margin ||
               pos.y <= margin ||
               pos.x >= level.width - margin - 1 ||
               pos.y >= level.height - margin - 1;
    }

    private static bool IsNearWallPocket(LevelData level, Vector2Int pos, EscortLevelGenerationConstraints constraints)
    {
        int wallNeighbors = 0;
        if (level.GetTile(pos.x + 1, pos.y) == constraints.WallTileID) wallNeighbors++;
        if (level.GetTile(pos.x - 1, pos.y) == constraints.WallTileID) wallNeighbors++;
        if (level.GetTile(pos.x, pos.y + 1) == constraints.WallTileID) wallNeighbors++;
        if (level.GetTile(pos.x, pos.y - 1) == constraints.WallTileID) wallNeighbors++;
        return wallNeighbors >= constraints.EnemyWallPocketNeighborThreshold;
    }

    private static int ResolveEnemyCount(EscortLevelBuildRequest request, EscortLevelGenerationConstraints constraints)
    {
        int distance = Mathf.Max(1, request?.ManhattanDistance ?? constraints.DefaultManhattanDistance);
        int difficultyOffset = request?.DifficultyOffset ?? 0;
        int divisor = Mathf.Max(1, constraints.EnemyDistanceDivisor);
        return Mathf.Clamp(
            distance / divisor + difficultyOffset,
            constraints.MinEnemyCount,
            Mathf.Max(constraints.MinEnemyCount, constraints.MaxEnemyCount));
    }

    private static List<LevelTagEntry> FindTags(IReadOnlyList<LevelTagEntry> tags, int tagID)
    {
        var result = new List<LevelTagEntry>();
        if (tags == null)
            return result;

        foreach (var tag in tags)
        {
            if (tag != null && tag.tagID == tagID)
                result.Add(tag);
        }

        return result;
    }

    private static int CountTags(IReadOnlyList<LevelTagEntry> tags, int tagID)
    {
        if (tags == null)
            return 0;

        int count = 0;
        foreach (var tag in tags)
        {
            if (tag != null && tag.tagID == tagID)
                count++;
        }

        return count;
    }

    private static bool IsWalkableEmpty(LevelData level, Vector2Int pos, EscortLevelGenerationConstraints constraints)
    {
        int tile = level.GetTile(pos.x, pos.y);
        if (tile == constraints.WallTileID || tile < 0)
            return false;

        if (level.tags == null)
            return true;

        foreach (var tag in level.tags)
        {
            if (tag != null && tag.x == pos.x && tag.y == pos.y)
                return false;
        }

        return true;
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static int NextInclusive(System.Random random, int min, int max)
    {
        if (min > max)
            (min, max) = (max, min);

        return random.Next(min, max + 1);
    }

    private static void Shuffle<T>(IList<T> values, System.Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
