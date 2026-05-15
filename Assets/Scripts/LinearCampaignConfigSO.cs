using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class LinearCampaignChapterEntry
{
    [TableColumnWidth(120)]
    public string chapterId;

    [TableColumnWidth(240)]
    public LinearChapterSO chapterConfig;
}

[CreateAssetMenu(fileName = "LinearCampaignConfig", menuName = "BlockingKing/Linear/Campaign Config")]
public class LinearCampaignConfigSO : ScriptableObject
{
    [Header("Identity")]
    public string campaignId;
    public string displayName;

    [Header("Chapters")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<LinearCampaignChapterEntry> chapters = new List<LinearCampaignChapterEntry>();

    public string ResolvedCampaignId => !string.IsNullOrWhiteSpace(campaignId) ? campaignId : name;

    public int ChapterCount => chapters != null ? chapters.Count : 0;

    public bool TryGetChapter(int index, out LinearChapterSO chapter)
    {
        chapter = null;
        if (chapters == null || index < 0 || index >= chapters.Count)
            return false;

        chapter = chapters[index]?.chapterConfig;
        return chapter != null;
    }

    public string ResolveChapterId(int index, LinearChapterSO chapter)
    {
        if (chapters != null && index >= 0 && index < chapters.Count)
        {
            string entryId = chapters[index]?.chapterId;
            if (!string.IsNullOrWhiteSpace(entryId))
                return entryId;
        }

        return chapter != null ? chapter.ResolveChapterId(index) : $"chapter_{index + 1}";
    }

    public bool TryGetLevel(int chapterIndex, int levelIndex, out LinearChapterSO chapter, out LinearLevelEntry entry)
    {
        entry = null;
        return TryGetChapter(chapterIndex, out chapter) && chapter.TryGetLevel(levelIndex, out entry);
    }
}
