using System;
using System.Collections.Generic;

[Serializable]
public class UserModel
{
    public int schemaVersion = 1;
    public LinearCampaignProgressModel linearCampaign = new LinearCampaignProgressModel();

    public static UserModel CreateDefault()
    {
        var model = new UserModel();
        model.EnsureInitialized();
        return model;
    }

    public void EnsureInitialized()
    {
        if (schemaVersion <= 0)
            schemaVersion = 1;

        linearCampaign ??= new LinearCampaignProgressModel();
        linearCampaign.EnsureInitialized();
    }
}

[Serializable]
public sealed class LinearCampaignProgressModel
{
    public string campaignId;
    public int chapterIndex;
    public int levelIndex;
    public string currentChapterId;
    public string currentLevelId;
    public bool campaignCompleted;
    public List<LinearLevelClearRecord> clearedLevels = new List<LinearLevelClearRecord>();

    public void EnsureInitialized()
    {
        clearedLevels ??= new List<LinearLevelClearRecord>();
        chapterIndex = Math.Max(0, chapterIndex);
        levelIndex = Math.Max(0, levelIndex);
    }

    public void ResetForCampaign(string targetCampaignId)
    {
        campaignId = targetCampaignId ?? string.Empty;
        chapterIndex = 0;
        levelIndex = 0;
        currentChapterId = string.Empty;
        currentLevelId = string.Empty;
        campaignCompleted = false;
        clearedLevels.Clear();
    }

    public void SetCursor(
        string targetCampaignId,
        int targetChapterIndex,
        int targetLevelIndex,
        string targetChapterId,
        string targetLevelId)
    {
        campaignId = targetCampaignId ?? string.Empty;
        chapterIndex = Math.Max(0, targetChapterIndex);
        levelIndex = Math.Max(0, targetLevelIndex);
        currentChapterId = targetChapterId ?? string.Empty;
        currentLevelId = targetLevelId ?? string.Empty;
        campaignCompleted = false;
    }

    public bool IsLevelCleared(string targetChapterId, string targetLevelId)
    {
        if (string.IsNullOrWhiteSpace(targetChapterId) || string.IsNullOrWhiteSpace(targetLevelId))
            return false;

        EnsureInitialized();
        return clearedLevels.Exists(record =>
            record != null &&
            string.Equals(record.chapterId, targetChapterId, StringComparison.Ordinal) &&
            string.Equals(record.levelId, targetLevelId, StringComparison.Ordinal));
    }

    public void MarkLevelCleared(string targetChapterId, string targetLevelId)
    {
        if (string.IsNullOrWhiteSpace(targetChapterId) || string.IsNullOrWhiteSpace(targetLevelId))
            return;

        EnsureInitialized();
        var record = clearedLevels.Find(item =>
            item != null &&
            string.Equals(item.chapterId, targetChapterId, StringComparison.Ordinal) &&
            string.Equals(item.levelId, targetLevelId, StringComparison.Ordinal));

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (record == null)
        {
            clearedLevels.Add(new LinearLevelClearRecord
            {
                chapterId = targetChapterId,
                levelId = targetLevelId,
                clearCount = 1,
                firstClearedUnixSeconds = now,
                lastClearedUnixSeconds = now
            });
            return;
        }

        record.clearCount = Math.Max(0, record.clearCount) + 1;
        if (record.firstClearedUnixSeconds <= 0)
            record.firstClearedUnixSeconds = now;

        record.lastClearedUnixSeconds = now;
    }
}

[Serializable]
public sealed class LinearLevelClearRecord
{
    public string chapterId;
    public string levelId;
    public int clearCount;
    public long firstClearedUnixSeconds;
    public long lastClearedUnixSeconds;
}
