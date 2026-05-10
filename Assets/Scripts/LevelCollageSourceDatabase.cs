using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelCollageSourceDatabase", menuName = "BlockingKing/Level Collage Source Database")]
public class LevelCollageSourceDatabase : ScriptableObject
{
    public List<LevelCollageSourceEntry> entries = new();

    public List<LevelCollageSourceEntry> GetEnabledEntries()
    {
        var result = new List<LevelCollageSourceEntry>();
        foreach (var entry in entries)
        {
            if (entry?.level != null && entry.enabled)
                result.Add(entry);
        }

        return result;
    }
}

[Serializable]
public sealed class LevelCollageSourceEntry
{
    public bool enabled = true;
    public LevelData level;
    public string sourcePath;
    public string sourceGroup;
    public int width;
    public int height;
    public int area;
    public int wallCount;
    public float wallRate;
    public int boxCount;
    public float boxRate;
    public int targetCount;
    public int boxOnTargetCount;
    public int effectiveBoxCount;
    public float effectiveBoxRate;
    public int manualWeight = 1;
}
