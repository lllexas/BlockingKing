using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EscortLevelBuildRequest
{
    public int Seed;
    public int ManhattanDistance;
    public float LogSlope;
    public int DifficultyOffset;
}

public static class EscortLevelGenerator
{
    private const string ResourcePath = "Levels";
    private const int PlayerTagID = 1;
    private const int BoxTagID = 2;
    private const int TargetTagID = 3;
    private const int EnemyTagID = 4;
    private const int CoreBoxTagID = 5;
    private const int TargetCoreTagID = 7;
    private const int WallTileID = 1;
    private const int FloorTileID = 2;
    private const int CityMargin = 2;

    private sealed class CityStamp
    {
        public LevelData Template;
        public RectInt Bounds;
        public Vector2Int Center => new(Bounds.x + Bounds.width / 2, Bounds.y + Bounds.height / 2);
    }

    public static LevelData CreateFromRandomClassicMap(EscortLevelBuildRequest request)
    {
        var templates = Resources.LoadAll<LevelData>(ResourcePath);
        if (templates == null || templates.Length == 0)
        {
            Debug.LogWarning($"[EscortLevelGenerator] No LevelData found under Resources/{ResourcePath}.");
            return null;
        }

        var random = new System.Random(request != null ? request.Seed : Environment.TickCount);
        var usableTemplates = FilterUsableTemplates(templates);
        if (usableTemplates.Count == 0)
        {
            Debug.LogWarning("[EscortLevelGenerator] No usable classic template found.");
            return null;
        }

        int distance = Mathf.Max(8, request?.ManhattanDistance ?? 12);
        int cityCount = ResolveCityCount(distance);
        var stamps = BuildCityStamps(usableTemplates, random, cityCount, request?.LogSlope ?? 0f);
        if (stamps.Count == 0)
            return null;

        LevelData level = BuildCompositeLevel(stamps);
        CarveCorridors(level, stamps, random);
        PromoteEscortTags(level, random, stamps);
        AddEnemies(level, random, stamps[0].Center, ResolveEnemyCount(request));

        Debug.Log($"[EscortLevelGenerator] Built {level.levelName}, cities={stamps.Count}, distance={distance}, logSlope={request?.LogSlope ?? 0f:0.###}");
        return level;
    }

    private static List<LevelData> FilterUsableTemplates(IReadOnlyList<LevelData> templates)
    {
        var result = new List<LevelData>();
        foreach (var template in templates)
        {
            if (template == null || template.width <= 0 || template.height <= 0)
                continue;

            if (template.tiles == null || template.tiles.Length != template.width * template.height)
                continue;

            if (FindTags(template.tags, BoxTagID).Count == 0)
                continue;

            if (template.width > 18 || template.height > 18)
                continue;

            result.Add(template);
        }

        return result;
    }

    private static int ResolveCityCount(int manhattanDistance)
    {
        return Mathf.Clamp(2 + manhattanDistance / 8, 2, 5);
    }

    private static List<CityStamp> BuildCityStamps(
        IReadOnlyList<LevelData> templates,
        System.Random random,
        int cityCount,
        float logSlope)
    {
        bool verticalMajor = logSlope > 0.35f;
        bool horizontalMajor = logSlope < -0.35f;

        var stamps = new List<CityStamp>();
        int cursorX = CityMargin;
        int cursorY = CityMargin;

        for (int i = 0; i < cityCount; i++)
        {
            LevelData template = templates[random.Next(templates.Count)];
            int width = template.width;
            int height = template.height;

            int mainOffset = i == 0 ? 0 : CityMargin + 2 + random.Next(0, 4);
            int sideOffset = random.Next(-2, 3);

            if (verticalMajor)
            {
                cursorY += i == 0 ? 0 : mainOffset + height;
                cursorX = CityMargin + Mathf.Max(0, sideOffset + i % 2 * 3);
            }
            else if (horizontalMajor)
            {
                cursorX += i == 0 ? 0 : mainOffset + width;
                cursorY = CityMargin + Mathf.Max(0, sideOffset + i % 2 * 3);
            }
            else
            {
                cursorX += i == 0 ? 0 : CityMargin + random.Next(3, 7);
                cursorY += i == 0 ? 0 : CityMargin + random.Next(2, 5);
            }

            stamps.Add(new CityStamp
            {
                Template = template,
                Bounds = new RectInt(cursorX, cursorY, width, height)
            });
        }

        return stamps;
    }

    private static LevelData BuildCompositeLevel(IReadOnlyList<CityStamp> stamps)
    {
        RectInt extents = stamps[0].Bounds;
        for (int i = 1; i < stamps.Count; i++)
            extents = Union(extents, stamps[i].Bounds);

        LevelData level = UnityEngine.ScriptableObject.CreateInstance<LevelData>();
        level.name = $"Escort_Collage_{stamps[0].Template.name}";
        level.levelName = $"Escort Collage {stamps[0].Template.levelName}";
        level.width = extents.xMax + CityMargin;
        level.height = extents.yMax + CityMargin;
        level.tiles = new int[level.width * level.height];
        level.tags = new List<LevelTagEntry>();

        for (int i = 0; i < level.tiles.Length; i++)
            level.tiles[i] = WallTileID;

        foreach (var stamp in stamps)
            StampTemplate(level, stamp);

        CancelBoxOnTargetTags(level);
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

    private static void StampTemplate(LevelData target, CityStamp stamp)
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

            target.tags.Add(new LevelTagEntry
            {
                tagID = tag.tagID,
                x = stamp.Bounds.x + tag.x,
                y = stamp.Bounds.y + tag.y
            });
        }
    }

    private static void CancelBoxOnTargetTags(LevelData level)
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
            if (tag.tagID == BoxTagID)
            {
                boxCells.Add(pos);
                if (targetCells.Contains(pos))
                    boxOnTargetCells.Add(pos);
            }
            else if (tag.tagID == TargetTagID)
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
            (tag.tagID == BoxTagID || tag.tagID == TargetTagID));
    }

    private static void CarveCorridors(LevelData level, IReadOnlyList<CityStamp> stamps, System.Random random)
    {
        for (int i = 0; i < stamps.Count - 1; i++)
        {
            Vector2Int from = FindNearestWalkableTo(level, stamps[i], stamps[i + 1].Center);
            Vector2Int to = FindNearestWalkableTo(level, stamps[i + 1], from);
            CarveManhattanCorridor(level, from, to, random.Next(0, 2) == 0);
        }
    }

    private static Vector2Int FindNearestWalkableTo(LevelData level, CityStamp stamp, Vector2Int target)
    {
        Vector2Int best = stamp.Center;
        int bestDistance = int.MaxValue;
        for (int y = stamp.Bounds.yMin; y < stamp.Bounds.yMax; y++)
        {
            for (int x = stamp.Bounds.xMin; x < stamp.Bounds.xMax; x++)
            {
                var pos = new Vector2Int(x, y);
                if (level.GetTile(x, y) == WallTileID)
                    continue;

                int distance = Manhattan(pos, target);
                if (distance >= bestDistance)
                    continue;

                best = pos;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static void CarveManhattanCorridor(LevelData level, Vector2Int from, Vector2Int to, bool horizontalFirst)
    {
        if (horizontalFirst)
        {
            CarveHorizontal(level, from.x, to.x, from.y);
            CarveVertical(level, from.y, to.y, to.x);
        }
        else
        {
            CarveVertical(level, from.y, to.y, from.x);
            CarveHorizontal(level, from.x, to.x, to.y);
        }
    }

    private static void CarveHorizontal(LevelData level, int x0, int x1, int y)
    {
        int min = Mathf.Min(x0, x1);
        int max = Mathf.Max(x0, x1);
        for (int x = min; x <= max; x++)
            CarveFloor(level, x, y);
    }

    private static void CarveVertical(LevelData level, int y0, int y1, int x)
    {
        int min = Mathf.Min(y0, y1);
        int max = Mathf.Max(y0, y1);
        for (int y = min; y <= max; y++)
            CarveFloor(level, x, y);
    }

    private static void CarveFloor(LevelData level, int x, int y)
    {
        level.SetTile(x, y, FloorTileID);
        level.ClearTagsAt(x, y);
    }

    private static void PromoteEscortTags(LevelData level, System.Random random, IReadOnlyList<CityStamp> stamps)
    {
        RemoveTags(level, PlayerTagID);
        RemoveTags(level, TargetCoreTagID);

        Vector2Int corePosition = PromoteCoreBox(level, stamps[0]);
        EnsurePlayerNearCore(level, random, corePosition);
        EnsureCoreTargetInFinalCity(level, random, corePosition, stamps[^1]);
    }

    private static void RemoveTags(LevelData level, int tagID)
    {
        level.tags?.RemoveAll(tag => tag != null && tag.tagID == tagID);
    }

    private static Vector2Int PromoteCoreBox(LevelData level, CityStamp startCity)
    {
        var boxes = FindTags(level.tags, BoxTagID);
        var targets = new HashSet<Vector2Int>();
        foreach (var target in FindTags(level.tags, TargetTagID))
            targets.Add(new Vector2Int(target.x, target.y));

        LevelTagEntry selected = null;
        foreach (var box in boxes)
        {
            if (!startCity.Bounds.Contains(new Vector2Int(box.x, box.y)))
                continue;

            if (!targets.Contains(new Vector2Int(box.x, box.y)))
            {
                selected = box;
                break;
            }
        }

        selected ??= boxes.Count > 0 ? boxes[0] : null;
        if (selected == null)
        {
            Vector2Int fallback = FindNearestWalkableTo(level, startCity, startCity.Center);
            level.tags.Add(new LevelTagEntry { tagID = CoreBoxTagID, x = fallback.x, y = fallback.y });
            return fallback;
        }

        selected.tagID = CoreBoxTagID;
        return new Vector2Int(selected.x, selected.y);
    }

    private static void EnsurePlayerNearCore(LevelData level, System.Random random, Vector2Int corePosition)
    {
        var adjacent = new List<Vector2Int>
        {
            corePosition + Vector2Int.left,
            corePosition + Vector2Int.right,
            corePosition + Vector2Int.up,
            corePosition + Vector2Int.down
        };

        Shuffle(adjacent, random);
        foreach (var adjacentPos in adjacent)
        {
            if (!IsWalkableEmpty(level, adjacentPos))
                continue;

            level.tags.Add(new LevelTagEntry { tagID = PlayerTagID, x = adjacentPos.x, y = adjacentPos.y });
            return;
        }

        if (TryFindEmptyFloor(level, random, corePosition, out var pos))
            level.tags.Add(new LevelTagEntry { tagID = PlayerTagID, x = pos.x, y = pos.y });
    }

    private static void EnsureCoreTargetInFinalCity(LevelData level, System.Random random, Vector2Int corePosition, CityStamp finalCity)
    {
        if (TryFindFarthestFloorInBounds(level, finalCity.Bounds, corePosition, out var pos))
            level.tags.Add(new LevelTagEntry { tagID = TargetCoreTagID, x = pos.x, y = pos.y });
        else if (TryFindEmptyFloor(level, random, corePosition, out pos))
            level.tags.Add(new LevelTagEntry { tagID = TargetCoreTagID, x = pos.x, y = pos.y });
    }

    private static bool TryFindFarthestFloorInBounds(LevelData level, RectInt bounds, Vector2Int origin, out Vector2Int pos)
    {
        pos = Vector2Int.zero;
        int bestDistance = -1;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                var candidate = new Vector2Int(x, y);
                if (!IsWalkableEmpty(level, candidate))
                    continue;

                int distance = Manhattan(candidate, origin);
                if (distance <= bestDistance)
                    continue;

                bestDistance = distance;
                pos = candidate;
            }
        }

        return bestDistance >= 0;
    }

    private static void AddEnemies(LevelData level, System.Random random, Vector2Int corePosition, int count)
    {
        var candidates = new List<Vector2Int>();
        for (int y = 0; y < level.height; y++)
        {
            for (int x = 0; x < level.width; x++)
            {
                var pos = new Vector2Int(x, y);
                if (!IsWalkableEmpty(level, pos))
                    continue;

                if (!IsNearMapEdge(level, pos) && !IsNearWallPocket(level, pos))
                    continue;

                if (Manhattan(pos, corePosition) < 5)
                    continue;

                candidates.Add(pos);
            }
        }

        Shuffle(candidates, random);
        int actualCount = Mathf.Min(count, candidates.Count);
        for (int i = 0; i < actualCount; i++)
            level.tags.Add(new LevelTagEntry { tagID = EnemyTagID, x = candidates[i].x, y = candidates[i].y });
    }

    private static bool IsNearMapEdge(LevelData level, Vector2Int pos)
    {
        return pos.x <= 1 || pos.y <= 1 || pos.x >= level.width - 2 || pos.y >= level.height - 2;
    }

    private static bool IsNearWallPocket(LevelData level, Vector2Int pos)
    {
        int wallNeighbors = 0;
        if (level.GetTile(pos.x + 1, pos.y) == WallTileID) wallNeighbors++;
        if (level.GetTile(pos.x - 1, pos.y) == WallTileID) wallNeighbors++;
        if (level.GetTile(pos.x, pos.y + 1) == WallTileID) wallNeighbors++;
        if (level.GetTile(pos.x, pos.y - 1) == WallTileID) wallNeighbors++;
        return wallNeighbors >= 2;
    }

    private static int ResolveEnemyCount(EscortLevelBuildRequest request)
    {
        int distance = Mathf.Max(8, request?.ManhattanDistance ?? 12);
        int difficultyOffset = request?.DifficultyOffset ?? 0;
        return Mathf.Clamp(distance / 4 + difficultyOffset, 1, 8);
    }

    private static bool TryFindEmptyFloor(LevelData level, System.Random random, Vector2Int avoid, out Vector2Int pos)
    {
        var candidates = new List<Vector2Int>();
        for (int y = 0; y < level.height; y++)
        {
            for (int x = 0; x < level.width; x++)
            {
                var candidate = new Vector2Int(x, y);
                if (candidate == avoid || !IsWalkableEmpty(level, candidate))
                    continue;

                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            pos = Vector2Int.zero;
            return false;
        }

        pos = candidates[random.Next(candidates.Count)];
        return true;
    }

    private static bool TryFindFarthestFloor(LevelData level, Vector2Int origin, out Vector2Int pos)
    {
        pos = Vector2Int.zero;
        int bestDistance = -1;
        for (int y = 0; y < level.height; y++)
        {
            for (int x = 0; x < level.width; x++)
            {
                var candidate = new Vector2Int(x, y);
                if (!IsWalkableEmpty(level, candidate))
                    continue;

                int distance = Manhattan(candidate, origin);
                if (distance <= bestDistance)
                    continue;

                bestDistance = distance;
                pos = candidate;
            }
        }

        return bestDistance >= 0;
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

    private static bool IsWalkableEmpty(LevelData level, Vector2Int pos)
    {
        int tile = level.GetTile(pos.x, pos.y);
        if (tile == WallTileID || tile < 0)
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

    private static void Shuffle<T>(IList<T> values, System.Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
