using System.Collections.Generic;
using UnityEngine;

public readonly struct LevelFeatureMetrics
{
    public readonly int Width;
    public readonly int Height;
    public readonly int Area;
    public readonly int WallCount;
    public readonly float WallRate;
    public readonly int BoxCount;
    public readonly float BoxRate;
    public readonly int TargetCount;
    public readonly int BoxOnTargetCount;
    public readonly int EffectiveBoxCount;
    public readonly float EffectiveBoxRate;

    public LevelFeatureMetrics(
        int width,
        int height,
        int area,
        int wallCount,
        int boxCount,
        int targetCount,
        int boxOnTargetCount)
    {
        Width = width;
        Height = height;
        Area = Mathf.Max(1, area);
        WallCount = Mathf.Max(0, wallCount);
        BoxCount = Mathf.Max(0, boxCount);
        TargetCount = Mathf.Max(0, targetCount);
        BoxOnTargetCount = Mathf.Max(0, boxOnTargetCount);
        EffectiveBoxCount = Mathf.Max(0, BoxCount - BoxOnTargetCount);
        WallRate = WallCount / (float)Area;
        BoxRate = BoxCount / (float)Area;
        EffectiveBoxRate = EffectiveBoxCount / (float)Area;
    }
}

public static class LevelFeatureMetricsUtility
{
    public const int DefaultWallTileID = 1;
    public const int DefaultBoxTagID = 2;
    public const int DefaultTargetTagID = 3;

    public static LevelFeatureMetrics Analyze(
        LevelData level,
        int wallTileID = DefaultWallTileID,
        int boxTagID = DefaultBoxTagID,
        int targetTagID = DefaultTargetTagID)
    {
        if (level == null)
            return default;

        int width = Mathf.Max(0, level.width);
        int height = Mathf.Max(0, level.height);
        int area = Mathf.Max(1, width * height);
        int wallCount = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (level.GetTile(x, y) == wallTileID)
                    wallCount++;
            }
        }

        var boxCells = new HashSet<Vector2Int>();
        var targetCells = new HashSet<Vector2Int>();
        if (level.tags != null)
        {
            foreach (var tag in level.tags)
            {
                if (tag == null)
                    continue;

                var pos = new Vector2Int(tag.x, tag.y);
                if (tag.tagID == boxTagID)
                    boxCells.Add(pos);
                else if (tag.tagID == targetTagID)
                    targetCells.Add(pos);
            }
        }

        int boxOnTargetCount = 0;
        foreach (var boxCell in boxCells)
        {
            if (targetCells.Contains(boxCell))
                boxOnTargetCount++;
        }

        return new LevelFeatureMetrics(
            width,
            height,
            area,
            wallCount,
            boxCells.Count,
            targetCells.Count,
            boxOnTargetCount);
    }
}
