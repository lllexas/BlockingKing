using System.Collections.Generic;
using UnityEngine;

public enum TutorialStepAdvanceMode
{
    Manual,
    TickCount,
    PlayCard,
    ReachCell,
    AllBoxesOnTargets,
    AnyCoreBoxOnTarget
}

public enum TutorialAct
{
    Push,
    BreakWall,
    ClassicPhaseOne,
    ClassicPhaseTwo,
    RookBishop,
    Escort
}

[System.Serializable]
public sealed class TutorialStep
{
    public string stepId;
    public TutorialAct act;
    public string title = "教程";
    [TextArea(2, 4)] public string message;
    public TutorialStepAdvanceMode advanceMode = TutorialStepAdvanceMode.Manual;
    public string requiredCardId;
    public Vector2Int targetCell;
    [Min(1)] public int requiredTicks = 1;
    public bool lockCards;
    public bool startEnemySpawning;
    public bool stopEnemySpawning;
    public bool hidePrompt;
    public bool completeTutorial;
}

[DefaultExecutionOrder(120)]
public sealed class TutorialStageDirector : MonoBehaviour
{
    [Header("Levels")]
    [SerializeField] private TileMappingConfig tileConfig;
    [SerializeField] private LevelData pushLevel;
    [SerializeField] private LevelData breakWallLevel;
    [SerializeField] private LevelData classicLevel;
    [SerializeField] private LevelData rookBishopLevel;
    [SerializeField] private LevelData escortLevel;

    [Header("Flow")]
    [SerializeField] private bool autoStart;
    [SerializeField] private bool returnToMainMenuOnComplete;
    [SerializeField] private List<TutorialStep> steps = new()
    {
        new TutorialStep
        {
            stepId = "act1_push",
            act = TutorialAct.Push,
            title = "第一幕",
            message = "推过去",
            lockCards = true
        },
        new TutorialStep
        {
            stepId = "act2_break_wall",
            act = TutorialAct.BreakWall,
            title = "第二幕",
            message = "这是个死局，在其他的推箱子游戏中，我会建议你重新开始。但在《推王之王》中，你不需要被这些繁文缛节约束。把墙撞开！！！",
            lockCards = false
        },
        new TutorialStep
        {
            stepId = "act3_classic_phase_one",
            act = TutorialAct.ClassicPhaseOne,
            title = "第三幕",
            message = "这是经典关卡，我暂时收走了你的卡牌，请尝试着把所有箱子推到目标点。如果你发现关卡没法再进行，就点击结算按钮吧。量力而行，不要内耗。",
            lockCards = true
        },
        new TutorialStep
        {
            stepId = "act4_classic_phase_two",
            act = TutorialAct.ClassicPhaseTwo,
            title = "第四幕",
            message = "现在，敌人会周期性生成在目标点。卡牌系统已回归，请消灭所有敌人，并把所有箱子推到目标点。",
            lockCards = false,
            startEnemySpawning = true
        },
        new TutorialStep
        {
            stepId = "act5_rook_bishop",
            act = TutorialAct.RookBishop,
            title = "第五幕",
            message = "不知道你发现没有，战车卡可以一次把箱子推好远。主教卡也是。你可以试试看。",
            lockCards = false
        },
        new TutorialStep
        {
            stepId = "act6_escort",
            act = TutorialAct.Escort,
            title = "第六幕",
            message = "这是押送关卡，你需要把蓝色的核心箱推到蓝色的核心目标点。请善用战车与主教卡，不然敌人会把你淹没。",
            lockCards = false,
            startEnemySpawning = true,
            completeTutorial = true
        }
    };

    private int _stepIndex = -1;
    private int _ticksInStep;
    private TutorialAct? _currentAct;
    private LevelData _currentLevel;
    private HandZone _observedHandZone;
    private string _overrideTitle;
    private string _overrideMessage;
    private bool _overrideHidden;

    private void OnEnable()
    {
        TickSystem.OnTick += HandleTick;
        RegisterHandZone();

        if (autoStart)
            StartTutorial();
        else
            RefreshPrompt();
    }

    private void OnDisable()
    {
        TickSystem.OnTick -= HandleTick;
        UnregisterHandZone();
        HidePrompt();
    }

    private void Update()
    {
        if (GameFlowController.Instance == null || GameFlowController.Instance.Mode != GameFlowMode.Tutorial)
            return;

        RegisterHandZone();
        EvaluateCurrentStep();
    }

    public void StartTutorial()
    {
        _stepIndex = -1;
        _currentAct = null;
        _currentLevel = null;
        ClearPromptOverride();
        AdvanceStep();
    }

    public void AdvanceStep()
    {
        _stepIndex++;
        _ticksInStep = 0;
        ClearPromptOverride();

        if (_stepIndex >= steps.Count)
        {
            CompleteTutorial(LevelPlayResult.Success, "tutorial steps completed");
            return;
        }

        ApplyStepState(steps[_stepIndex]);
    }

    private void ApplyStepState(TutorialStep step)
    {
        if (step == null)
            return;

        if (!EnsureActLoaded(step.act))
            return;

        HandZone.SetCardsLocked(step != null && step.lockCards);
        ApplySpawnState(step);
        RefreshPrompt();
    }

    public void ShowPrompt(string message, string title = null)
    {
        _overrideHidden = false;
        _overrideTitle = title;
        _overrideMessage = message;
        RefreshPrompt();
    }

    public void HidePrompt()
    {
        _overrideHidden = true;
        HidePromptPanel();
    }

    public void ClearPromptOverride()
    {
        _overrideHidden = false;
        _overrideTitle = null;
        _overrideMessage = null;
        RefreshPrompt();
    }

    public void SetPromptVisible(bool visible)
    {
        if (visible)
            RefreshPrompt();
        else
            HidePrompt();
    }

    private void HandleTick()
    {
        if (!IsTutorialActive())
            return;

        _ticksInStep++;
        EvaluateCurrentStep();
    }

    private void HandleCardPlayed(CardSO card)
    {
        if (!IsTutorialActive() || CurrentStep()?.advanceMode != TutorialStepAdvanceMode.PlayCard)
            return;

        string requiredCardId = CurrentStep()?.requiredCardId;
        if (!string.IsNullOrWhiteSpace(requiredCardId) && card != null && card.cardId != requiredCardId)
            return;

        AdvanceOrCompleteCurrentStep();
    }

    private void EvaluateCurrentStep()
    {
        if (!IsTutorialActive())
            return;

        var step = CurrentStep();
        if (step == null)
            return;

        switch (step.advanceMode)
        {
            case TutorialStepAdvanceMode.TickCount:
                if (_ticksInStep >= Mathf.Max(1, step.requiredTicks))
                    AdvanceOrCompleteCurrentStep();
                break;

            case TutorialStepAdvanceMode.ReachCell:
                if (TryGetPlayerCell(out var playerCell) && playerCell == step.targetCell)
                    AdvanceOrCompleteCurrentStep();
                break;

            case TutorialStepAdvanceMode.AllBoxesOnTargets:
                if (LevelPlayer.ActiveInstance != null && LevelPlayer.ActiveInstance.AreAllBoxesOnTargets())
                    AdvanceOrCompleteCurrentStep();
                break;

            case TutorialStepAdvanceMode.AnyCoreBoxOnTarget:
                if (LevelPlayer.ActiveInstance != null && LevelPlayer.ActiveInstance.IsAnyCoreBoxOnTarget())
                    AdvanceOrCompleteCurrentStep();
                break;
        }
    }

    private void AdvanceOrCompleteCurrentStep()
    {
        if (CurrentStep()?.completeTutorial == true)
            CompleteTutorial(LevelPlayResult.Success, CurrentStep()?.stepId);
        else
            AdvanceStep();
    }

    private void CompleteTutorial(LevelPlayResult result, string reason)
    {
        HidePrompt();
        HandZone.SetCardsLocked(true);
        SpawnSystem.Instance?.StopSpawning();
        LevelPlayer.ActiveInstance?.SettleTutorial(result, string.IsNullOrWhiteSpace(reason) ? "tutorial completed" : reason);

        if (returnToMainMenuOnComplete)
            GameFlowController.Instance?.ReturnToMainMenuRound();
    }

    private TutorialStep CurrentStep()
    {
        return _stepIndex >= 0 && _stepIndex < steps.Count ? steps[_stepIndex] : null;
    }

    private bool ShouldShowPrompt()
    {
        if (_overrideHidden)
            return false;

        var step = CurrentStep();
        if (step == null || step.hidePrompt)
            return false;

        return !string.IsNullOrWhiteSpace(GetPromptMessage());
    }

    private string GetPromptTitle()
    {
        if (!string.IsNullOrWhiteSpace(_overrideTitle))
            return _overrideTitle;

        string stepTitle = CurrentStep()?.title;
        return !string.IsNullOrWhiteSpace(stepTitle) ? stepTitle : "教程";
    }

    private string GetPromptMessage()
    {
        if (!string.IsNullOrWhiteSpace(_overrideMessage))
            return _overrideMessage;

        return CurrentStep()?.message ?? string.Empty;
    }

    private bool EnsureActLoaded(TutorialAct act)
    {
        LevelData level = ResolveActLevel(act);
        if (level == null)
        {
            Debug.LogError($"[TutorialStageDirector] Missing LevelData for tutorial act '{act}'.");
            HidePrompt();
            return false;
        }

        var player = LevelPlayer.ActiveInstance != null
            ? LevelPlayer.ActiveInstance
            : FindObjectOfType<LevelPlayer>();

        if (player == null)
        {
            Debug.LogError("[TutorialStageDirector] Cannot play tutorial act because LevelPlayer is missing.");
            HidePrompt();
            return false;
        }

        bool sameLevelIsRunning = _currentLevel == level &&
                                  player.CurrentLevel == level &&
                                  player.PlayMode == LevelPlayMode.Tutorial &&
                                  player.IsPlaying;

        _currentAct = act;
        if (sameLevelIsRunning)
            return true;

        var request = new LevelPlayRequest
        {
            Level = level,
            Config = tileConfig,
            Mode = LevelPlayMode.Tutorial,
            RewardSettings = GameFlowController.Instance != null ? GameFlowController.Instance.RewardSettings : null,
            Difficulty = RunDifficultySnapshot.Default
        };

        if (!player.PlayLevel(request))
        {
            Debug.LogError($"[TutorialStageDirector] Failed to play tutorial act '{act}' with level '{level.name}'.");
            HidePrompt();
            return false;
        }

        _currentLevel = level;
        return true;
    }

    private LevelData ResolveActLevel(TutorialAct act)
    {
        return act switch
        {
            TutorialAct.Push => pushLevel,
            TutorialAct.BreakWall => breakWallLevel,
            TutorialAct.ClassicPhaseOne => classicLevel,
            TutorialAct.ClassicPhaseTwo => classicLevel,
            TutorialAct.RookBishop => rookBishopLevel,
            TutorialAct.Escort => escortLevel,
            _ => null
        };
    }

    private static void ApplySpawnState(TutorialStep step)
    {
        if (step.stopEnemySpawning)
            SpawnSystem.Instance?.StopSpawning();

        if (step.startEnemySpawning)
        {
            SpawnSystem.Instance?.StartSpawning();
            EnemyAutoAISystem.Instance?.Tick();
        }
    }

    private bool IsTutorialActive()
    {
        return GameFlowController.Instance != null &&
               GameFlowController.Instance.Mode == GameFlowMode.Tutorial &&
               LevelPlayer.ActiveInstance != null &&
               LevelPlayer.ActiveInstance.PlayMode == LevelPlayMode.Tutorial &&
               _stepIndex >= 0 &&
               _stepIndex < steps.Count;
    }

    private static bool TryGetPlayerCell(out Vector2Int playerCell)
    {
        playerCell = default;
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            playerCell = entities.coreComponents[i].Position;
            return true;
        }

        return false;
    }

    private void RegisterHandZone()
    {
        var handZone = HandZone.ActiveInstance;
        if (handZone == _observedHandZone)
            return;

        UnregisterHandZone();
        _observedHandZone = handZone;
        if (_observedHandZone != null)
            _observedHandZone.CardPlayed += HandleCardPlayed;
    }

    private void UnregisterHandZone()
    {
        if (_observedHandZone != null)
            _observedHandZone.CardPlayed -= HandleCardPlayed;

        _observedHandZone = null;
    }

    private void RefreshPrompt()
    {
        if (!ShouldShowPrompt())
        {
            HidePromptPanel();
            return;
        }

        ShowPromptPanel(GetPromptTitle(), GetPromptMessage());
    }

    private static void ShowPromptPanel(string title, string message)
    {
        PostSystem.Instance?.Send("期望显示面板", new TutorialPromptUIRequest
        {
            Title = title,
            Message = message
        });
    }

    private static void HidePromptPanel()
    {
        PostSystem.Instance?.Send("期望隐藏面板", TutorialUIIds.Prompt);
    }
}
