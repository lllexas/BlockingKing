using UnityEngine;

public sealed class LinearModeDirector : MonoBehaviour
{
    private LinearCampaignConfigSO _config;
    private int _chapterIndex;
    private int _levelIndex;
    private bool _isRunning;

    public LinearCampaignConfigSO Config => _config;
    public bool IsRunning => _isRunning;
    public int CurrentChapterIndex => _chapterIndex;
    public int CurrentLevelIndex => _levelIndex;

    public void Configure(LinearCampaignConfigSO campaignConfig)
    {
        _config = campaignConfig;
    }

    public void StartCampaign()
    {
        if (_config == null)
        {
            Debug.LogError("[LinearModeDirector] Linear campaign config is missing.");
            return;
        }

        string campaignId = _config.ResolvedCampaignId;
        var progress = MainModel.User.linearCampaign;
        if (!string.Equals(progress.campaignId, campaignId, System.StringComparison.Ordinal))
            progress.ResetForCampaign(campaignId);

        _chapterIndex = Mathf.Clamp(progress.chapterIndex, 0, Mathf.Max(0, _config.ChapterCount - 1));
        _levelIndex = Mathf.Max(0, progress.levelIndex);
        _isRunning = false;
    }

    public void StopCampaign()
    {
        _isRunning = false;
    }

    public bool TryJumpToLevel(int chapterIndex, int levelIndex)
    {
        if (_config == null || !_config.TryGetLevel(chapterIndex, levelIndex, out _, out var entry))
            return false;

        if (!LinearLevelValidation.CanPlay(entry, out string reason))
        {
            Debug.LogWarning($"[LinearModeDirector] Linear level is not playable: chapter={chapterIndex}, level={levelIndex}, reason={reason}");
            return false;
        }

        _chapterIndex = Mathf.Max(0, chapterIndex);
        _levelIndex = Mathf.Max(0, levelIndex);
        _isRunning = true;
        PlayCurrentLevel();
        return true;
    }

    public void OnLevelSettled(LevelPlayResult result)
    {
        if (!_isRunning)
            return;

        if (result != LevelPlayResult.Success)
        {
            _isRunning = false;
            Debug.LogWarning($"[LinearModeDirector] Linear level failed at chapter={_chapterIndex}, level={_levelIndex}. Auto retry is disabled.");
            return;
        }

        if (!TryResolveCurrentLevel(out var chapter, out var entry, out string chapterId, out string levelId))
        {
            CompleteCampaign();
            return;
        }

        var progress = MainModel.User.linearCampaign;
        progress.MarkLevelCleared(chapterId, levelId);

        int nextChapterIndex = _chapterIndex;
        int nextLevelIndex = _levelIndex + 1;
        if (chapter == null || nextLevelIndex >= chapter.LevelCount)
        {
            nextChapterIndex++;
            nextLevelIndex = 0;
        }

        if (nextChapterIndex >= _config.ChapterCount)
        {
            CompleteCampaign();
            return;
        }

        _chapterIndex = nextChapterIndex;
        _levelIndex = nextLevelIndex;
        SaveCursor();
        PlayCurrentLevel();
    }

    private void PlayCurrentLevel()
    {
        if (!TryResolveCurrentLevel(out var chapter, out var entry, out string chapterId, out string levelId))
        {
            Debug.LogError("[LinearModeDirector] Current linear level is invalid.");
            AbortCampaign();
            return;
        }

        if (!LinearLevelValidation.CanPlay(entry, out string validationReason))
        {
            Debug.LogWarning($"[LinearModeDirector] Linear level is not playable: {chapterId}/{levelId}, reason={validationReason}");
            AbortCampaign();
            return;
        }

        SaveCursor(chapterId, levelId);

        var player = LevelPlayer.ActiveInstance != null
            ? LevelPlayer.ActiveInstance
            : FindObjectOfType<LevelPlayer>();

        if (player == null)
        {
            Debug.LogError("[LinearModeDirector] LevelPlayer is missing.");
            AbortCampaign();
            return;
        }

        ApplyLevelPlayerHp(entry);
        ApplyLevelDeck(entry);
        GameFlowController.Instance?.EnterLevel();
        ShowBgmRecord();
        bool started = player.PlayLevel(new LevelPlayRequest
        {
            Level = entry.levelData,
            Mode = ResolvePlayMode(entry),
            StepLimit = Mathf.Max(1, entry.stepLimit),
            Difficulty = GameFlowController.Instance != null
                ? GameFlowController.Instance.BuildDifficultySnapshot(_chapterIndex + 1, Mathf.Max(1, _config.ChapterCount))
                : RunDifficultySnapshot.Default,
            RewardSettings = GameFlowController.Instance != null ? GameFlowController.Instance.RewardSettings : null,
            RouteLayer = _chapterIndex + 1,
            RouteLayerCount = Mathf.Max(1, _config.ChapterCount)
        });

        if (!started)
        {
            Debug.LogError($"[LinearModeDirector] Failed to play linear level {chapterId}/{levelId}.");
            AbortCampaign();
            return;
        }

        Debug.Log($"[LinearModeDirector] Playing {chapterId}/{levelId}");
    }

    private bool TryResolveCurrentLevel(
        out LinearChapterSO chapter,
        out LinearLevelEntry entry,
        out string chapterId,
        out string levelId)
    {
        chapter = null;
        entry = null;
        chapterId = string.Empty;
        levelId = string.Empty;

        if (_config == null || !_config.TryGetLevel(_chapterIndex, _levelIndex, out chapter, out entry))
            return false;

        chapterId = _config.ResolveChapterId(_chapterIndex, chapter);
        levelId = chapter.ResolveLevelId(entry, _levelIndex);
        return true;
    }

    private void SaveCursor()
    {
        if (!TryResolveCurrentLevel(out _, out _, out string chapterId, out string levelId))
            return;

        SaveCursor(chapterId, levelId);
    }

    private void SaveCursor(string chapterId, string levelId)
    {
        var progress = MainModel.User.linearCampaign;
        progress.SetCursor(_config.ResolvedCampaignId, _chapterIndex, _levelIndex, chapterId, levelId);
        MainModel.Save();
    }

    private void CompleteCampaign()
    {
        _isRunning = false;
        var progress = MainModel.User.linearCampaign;
        progress.campaignId = _config != null ? _config.ResolvedCampaignId : progress.campaignId;
        progress.campaignCompleted = true;
        MainModel.Save();
        GameFlowController.Instance?.ReturnToMainMenu();
        Debug.Log("[LinearModeDirector] Campaign completed.");
    }

    private void AbortCampaign()
    {
        _isRunning = false;
        GameFlowController.Instance?.ExitLevel();
    }

    private static LevelPlayMode ResolvePlayMode(LinearLevelEntry entry)
    {
        if (entry == null)
            return LevelPlayMode.Classic;

        return entry.subMode switch
        {
            LinearLevelSubMode.StepLimit => LevelPlayMode.StepLimit,
            LinearLevelSubMode.Escort => LevelPlayMode.Escort,
            _ => LevelPlayMode.Classic
        };
    }

    private static void ApplyLevelPlayerHp(LinearLevelEntry entry)
    {
        if (entry == null)
            return;

        var status = NekoGraph.GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        if (status == null)
        {
            status = new RunPlayerStatusFacade();
            NekoGraph.GraphHub.Instance?.RegisterFacade(status);
        }

        int hp = Mathf.Max(1, entry.playerInitialHp);
        status?.Reset(hp, hp);
    }

    private static void ApplyLevelDeck(LinearLevelEntry entry)
    {
        if (entry == null || entry.orderedStartingDeck == null)
            return;

        var handZone = HandZone.ActiveInstance != null
            ? HandZone.ActiveInstance
            : FindObjectOfType<HandZone>();

        handZone?.RebuildFromOrderedDeck(entry.orderedStartingDeck, true, false);
    }

    private static void ShowBgmRecord()
    {
        PostSystem.Instance?.Send("期望显示面板", new BgmRecordUIRequest(BgmRecordUIIds.RecordButton));
    }
}
