using System;
using System.Collections.Generic;
using UnityEngine;

public enum LinearLevelSubMode
{
    Classic,
    StepLimit,
    Escort
}

[Serializable]
public class LinearLevelEntry
{
    public string levelId;
    public string displayName;
    public LevelData levelData;
    public LinearLevelSubMode subMode = LinearLevelSubMode.Classic;
    [Min(1)] public int playerInitialHp = 30;
    public StartingDeckSO orderedStartingDeck;
    [Min(1)] public int stepLimit = 30;
}

[CreateAssetMenu(fileName = "LinearChapter", menuName = "BlockingKing/Linear/Chapter")]
public class LinearChapterSO : ScriptableObject
{
    public string chapterId;
    public string displayName;
    [TextArea(2, 5)] public string description;
    public List<LinearLevelEntry> levels = new List<LinearLevelEntry>();

    public int LevelCount => levels != null ? levels.Count : 0;

    public string ResolveChapterId(int chapterIndex)
    {
        return !string.IsNullOrWhiteSpace(chapterId) ? chapterId : $"chapter_{chapterIndex + 1}";
    }

    public bool TryGetLevel(int index, out LinearLevelEntry entry)
    {
        entry = null;
        if (levels == null || index < 0 || index >= levels.Count)
            return false;

        entry = levels[index];
        return entry != null && entry.levelData != null;
    }

    public string ResolveLevelId(LinearLevelEntry entry, int levelIndex)
    {
        if (entry != null && !string.IsNullOrWhiteSpace(entry.levelId))
            return entry.levelId;

        if (entry != null && entry.levelData != null)
            return entry.levelData.name;

        return $"level_{levelIndex + 1}";
    }
}
