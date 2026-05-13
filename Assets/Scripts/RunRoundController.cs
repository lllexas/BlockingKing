using System;
using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

public enum RunRoundState
{
    Inactive,
    RoundOffer,
    InCombat,
    CombatSettlement,
    PostCombatOffer,
    Shop,
    Event,
    EventStage,
    Defeat,
    RunComplete
}

public enum RunMainStageMode
{
    None,
    Classic,
    Escort
}

public sealed class RunRoundOffer
{
    public int RoundIndex;
    public int RoundCount;
    public int EncounterCycleIndex;
    public int EncounterCyclesPerRound;
    public RunDifficultySnapshot Difficulty;
    public LevelData ClassicLevel;
    public LevelData EscortLevel;
    public bool EscortLevelIsRuntime;
    public RewardSO SkipReward;
    public string SkipRewardLabel;
    public int AlternateBonusGold;
    public bool ClassicWouldAlternate;
    public bool EscortWouldAlternate;
}

public sealed class RunPostCombatOffer
{
    public ShopSO Shop;
    public bool ShopIsRuntime;
    public BasePackData EventPack;
    public string EventTitle;
}

public sealed class RunCombatSettlement
{
    public int RoundIndex;
    public int RoundCount;
    public int EncounterCycleIndex;
    public int EncounterCyclesPerRound;
    public RunMainStageMode Mode;
    public string LevelName;
    public int GoldBefore;
    public int GoldAfter;
    public int GoldDelta;
    public int SuccessfulBoxCount;
    public readonly List<RunCombatSettlementRewardLine> RewardLines = new();
    public int Hp;
    public int MaxHp;
    public string Message;
}

public sealed class RunCombatSettlementRewardLine
{
    public string Label;
    public int Gold;
    public bool IsHeader;
}

[DefaultExecutionOrder(105)]
public sealed class RunRoundController : MonoBehaviour
{
    private const string RuntimeShopTitle = "商店";
    private const string RuntimeShopDescription = "";
    private const int FallbackEventGold = 8;
    private const string FallbackEventDescription = "不期而遇：获得少量金币。";

    public RunRoundState State { get; private set; } = RunRoundState.Inactive;
    public RunRoundOffer CurrentOffer { get; private set; }
    public RunPostCombatOffer CurrentPostCombatOffer { get; private set; }
    public RunCombatSettlement CurrentCombatSettlement { get; private set; }
    public RunMainStageMode LastCompletedMode { get; private set; } = RunMainStageMode.None;
    public RunMainStageMode ActiveMode { get; private set; } = RunMainStageMode.None;
    public int RoundIndex { get; private set; }
    public int RoundCount => _config != null ? Mathf.Max(1, _config.totalRounds) : 1;
    public int EncounterCycleIndex { get; private set; }
    public int EncounterCyclesPerRound => _config != null ? Mathf.Max(1, _config.encounterCyclesPerRound) : 1;
    public string StatusMessage { get; private set; }
    public string EventMessage { get; private set; }

    private RunRoundConfigSO _config;
    private RunConfigSO _runConfig;
    private System.Random _random;
    private LevelData _activeCombatLevel;
    private bool _activeCombatLevelIsRuntime;
    private ShopSO _runtimePostCombatShop;
    private int _combatStartGold;

    public void StartRun(RunConfigSO runConfig, RunRoundConfigSO config)
    {
        _runConfig = runConfig;
        _config = config;
        int seed = config != null ? config.seed : Environment.TickCount;
        _random = seed == 0 ? new System.Random() : new System.Random(seed);
        RoundIndex = 1;
        EncounterCycleIndex = 1;
        LastCompletedMode = RunMainStageMode.None;
        ActiveMode = RunMainStageMode.None;
        StatusMessage = null;
        EventMessage = null;
        CurrentCombatSettlement = null;
        ClearCurrentPostCombatOffer();
        ClearCurrentRoundOffer();
        BuildNextOffer();
        BroadcastCurrentState();
    }

    public void ChooseClassic()
    {
        if (State != RunRoundState.RoundOffer || CurrentOffer?.ClassicLevel == null)
            return;

        StartCombat(RunMainStageMode.Classic, CurrentOffer.ClassicLevel);
    }

    public void ChooseEscort()
    {
        if (State != RunRoundState.RoundOffer || CurrentOffer?.EscortLevel == null)
            return;

        StartCombat(RunMainStageMode.Escort, CurrentOffer.EscortLevel);
    }

    public void SkipRound()
    {
        if (State != RunRoundState.RoundOffer)
            return;

        if (CurrentOffer?.SkipReward != null)
            ApplyReward(CurrentOffer.SkipReward, "round skip");

        StatusMessage = CurrentOffer != null && !string.IsNullOrWhiteSpace(CurrentOffer.SkipRewardLabel)
            ? $"放弃本轮，获得 {CurrentOffer.SkipRewardLabel}。"
            : "放弃本轮。";
        ClearCurrentRoundOffer();
        AdvanceEncounterCycle();
        BroadcastCurrentState();
    }

    public void OnCombatSettled(LevelPlayResult result)
    {
        if (State != RunRoundState.InCombat)
            return;

        if (result != LevelPlayResult.Success)
        {
            DefeatRun("战斗失败。");
            BroadcastCurrentState();
            return;
        }

        var status = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        if (status != null && status.IsDead)
        {
            DefeatRun("玩家生命值归零。");
            BroadcastCurrentState();
            return;
        }

        bool alternated = LastCompletedMode != RunMainStageMode.None && ActiveMode != LastCompletedMode;
        if (alternated && CurrentOffer != null && CurrentOffer.AlternateBonusGold > 0)
            AddGold(CurrentOffer.AlternateBonusGold, "alternate classic escort bonus");

        var completedMode = ActiveMode;
        var completedLevel = _activeCombatLevel;
        var completedPlayer = LevelPlayer.ActiveInstance;
        LastCompletedMode = ActiveMode;
        ActiveMode = RunMainStageMode.None;
        StatusMessage = alternated
            ? $"交替完成，获得 {CurrentOffer.AlternateBonusGold} 金币。"
            : "战斗完成。";
        CurrentCombatSettlement = BuildCombatSettlement(completedMode, completedLevel, completedPlayer, StatusMessage);
        if (alternated && CurrentOffer != null && CurrentOffer.AlternateBonusGold > 0)
        {
            CurrentCombatSettlement.RewardLines.Add(new RunCombatSettlementRewardLine
            {
                Label = "交替完成奖励",
                Gold = CurrentOffer.AlternateBonusGold
            });
        }

        BuildPostCombatOffer();
        State = RunRoundState.CombatSettlement;
        BroadcastCurrentState();
    }

    public void ConfirmCombatSettlement()
    {
        if (State != RunRoundState.CombatSettlement)
            return;

        CurrentCombatSettlement = null;
        State = RunRoundState.PostCombatOffer;
        BroadcastCurrentState();
    }

    private void DefeatRun(string reason)
    {
        StatusMessage = reason;
        State = RunRoundState.Defeat;
        ClearCurrentRoundOffer();
        ClearCurrentPostCombatOffer();
        ClearActiveCombatLevel();
        ActiveMode = RunMainStageMode.None;
    }

    public void ChooseShop()
    {
        if (State != RunRoundState.PostCombatOffer)
            return;

        if (CurrentPostCombatOffer?.Shop == null)
        {
            StatusMessage = "没有配置商店，跳过。";
            AdvanceEncounterCycle();
            BroadcastCurrentState();
            return;
        }

        State = RunRoundState.Shop;
        StatusMessage = "进入商店。";
        RunRoundUIStateRegistry.Apply(this);
        ShowBgmRecord();
        if (RunShopPanelAnimator.Instance != null)
        {
            RunShopPanelAnimator.Instance.OpenDirect(CurrentPostCombatOffer.Shop, OnShopClosed);
            return;
        }

        var shopFrontend = FindObjectOfType<RunShopOnGUIFrontend>();
        if (shopFrontend == null)
            shopFrontend = gameObject.AddComponent<RunShopOnGUIFrontend>();

        shopFrontend.OpenDirect(CurrentPostCombatOffer.Shop, OnShopClosed);
    }

    public void OnShopClosed()
    {
        if (State != RunRoundState.Shop)
            return;

        StatusMessage = "离开商店。";
        ClearCurrentPostCombatOffer();
        AdvanceEncounterCycle();
        BroadcastCurrentState();
    }

    public void ChooseEvent()
    {
        if (State != RunRoundState.PostCombatOffer)
            return;

        if (CurrentPostCombatOffer?.EventPack != null)
        {
            var stageFacade = GraphHub.Instance?.GetFacade<RunStageFacade>();
            if (stageFacade == null)
            {
                stageFacade = new RunStageFacade();
                GraphHub.Instance?.RegisterFacade(stageFacade);
            }

            if (stageFacade != null && stageFacade.TryRunStagePack(CurrentPostCombatOffer.EventPack, unloadPrevious: true))
            {
                State = RunRoundState.EventStage;
                StatusMessage = string.IsNullOrWhiteSpace(CurrentPostCombatOffer.EventTitle)
                    ? "不期而遇。"
                    : CurrentPostCombatOffer.EventTitle;
                RunRoundUIStateRegistry.Apply(this);
                ShowBgmRecord();
                GameFlowController.Instance?.OnRoundEventStageStarted();
                return;
            }
        }

        State = RunRoundState.Event;
        int gold = Mathf.Max(0, FallbackEventGold);
        AddGold(gold, "round event fallback");
        EventMessage = string.IsNullOrWhiteSpace(FallbackEventDescription)
            ? $"不期而遇，获得 {gold} 金币。"
            : $"{FallbackEventDescription} 获得 {gold} 金币。";
        StatusMessage = EventMessage;
        ClearCurrentPostCombatOffer();
        ConfirmEvent();
    }

    public void ConfirmEvent()
    {
        if (State != RunRoundState.Event)
            return;

        EventMessage = null;
        AdvanceEncounterCycle();
        BroadcastCurrentState();
    }

    public void OnEventStageCompleted()
    {
        if (State != RunRoundState.EventStage)
            return;

        StatusMessage = "不期而遇完成。";
        ClearCurrentPostCombatOffer();
        AdvanceEncounterCycle();
        BroadcastCurrentState();
    }

    private void StartCombat(RunMainStageMode mode, LevelData level)
    {
        var player = FindObjectOfType<LevelPlayer>();
        if (player == null)
        {
            StatusMessage = "LevelPlayer not found.";
            return;
        }

        ActiveMode = mode;
        _activeCombatLevel = level;
        _activeCombatLevelIsRuntime = mode == RunMainStageMode.Escort && CurrentOffer != null && CurrentOffer.EscortLevelIsRuntime;
        _combatStartGold = GetGold();
        SetCameraFlowPaused(false);
        ClearUnselectedRoundOffer(mode);
        State = RunRoundState.InCombat;
        RunRoundUIStateRegistry.Apply(this);
        GameFlowController.Instance?.EnterLevel();
        ShowBgmRecord();

        player.PlayLevel(new LevelPlayRequest
        {
            Level = level,
            Mode = mode == RunMainStageMode.Escort ? LevelPlayMode.Escort : LevelPlayMode.Classic,
            Difficulty = CurrentOffer?.Difficulty ?? RunDifficultySnapshot.Default,
            RewardSettings = GameFlowController.Instance != null ? GameFlowController.Instance.RewardSettings : null,
            RouteLayer = RoundIndex,
            RouteLayerCount = RoundCount
        });
    }

    private void AdvanceEncounterCycle()
    {
        EncounterCycleIndex++;
        if (EncounterCycleIndex > EncounterCyclesPerRound)
        {
            EncounterCycleIndex = 1;
            RoundIndex++;
        }

        if (RoundIndex > RoundCount)
        {
            State = RunRoundState.RunComplete;
            ClearCurrentRoundOffer();
            ClearCurrentPostCombatOffer();
            ClearActiveCombatLevel();
            ActiveMode = RunMainStageMode.None;
            return;
        }

        ClearActiveCombatLevel();
        BuildNextOffer();
    }

    private void BuildNextOffer()
    {
        if (_config == null)
        {
            State = RunRoundState.Inactive;
            CurrentOffer = null;
            StatusMessage = "Round flow config missing.";
            return;
        }

        var flow = GameFlowController.Instance;
        var difficulty = flow != null
            ? flow.BuildDifficultySnapshot(RoundIndex, RoundCount)
            : RunDifficultySnapshot.Default;

        var context = new PoolEvalContext
        {
            routeLayer = RoundIndex,
            routeLayerCount = RoundCount,
            progress = RoundCount > 1 ? Mathf.Clamp01((RoundIndex - 1) / (float)(RoundCount - 1)) : 0f,
            difficulty = Mathf.Max(0f, difficulty.OverallDifficulty),
            seed = _random != null ? _random.Next() : UnityEngine.Random.Range(int.MinValue, int.MaxValue)
        };

        LevelData classic = null;
        var classicTable = _config.classicFeatureSelectionTable != null
            ? _config.classicFeatureSelectionTable
            : _runConfig?.routeSettings?.classicLevelSelectionTable;
        classicTable?.TryRollLevel(context, _random, _config.levelSourceDatabase, out classic);

        LevelData escort = BuildEscortOffer(context);
        var skipReward = RollSkipReward();

        CurrentOffer = new RunRoundOffer
        {
            RoundIndex = RoundIndex,
            RoundCount = RoundCount,
            EncounterCycleIndex = EncounterCycleIndex,
            EncounterCyclesPerRound = EncounterCyclesPerRound,
            Difficulty = difficulty,
            ClassicLevel = classic,
            EscortLevel = escort,
            EscortLevelIsRuntime = escort != null,
            SkipReward = skipReward,
            SkipRewardLabel = BuildRewardLabel(skipReward),
            AlternateBonusGold = Mathf.Max(0, _config.classicEscortAlternationGold),
            ClassicWouldAlternate = LastCompletedMode == RunMainStageMode.Escort,
            EscortWouldAlternate = LastCompletedMode == RunMainStageMode.Classic
        };

        State = RunRoundState.RoundOffer;
    }

    private LevelData BuildEscortOffer(PoolEvalContext context)
    {
        var sourceDatabase = ResolveEscortSourceDatabase();
        if (sourceDatabase == null)
            return null;

        var resolved = ResolveEscortGeneration(context);
        var sourceEntries = _config.escortFeatureSelectionTable != null
            ? _config.escortFeatureSelectionTable.BuildCandidates(context, _config.levelSourceDatabase)
            : null;

        return EscortLevelGenerator.CreateFromRandomClassicMap(new EscortLevelBuildRequest
        {
            Seed = context.seed,
            ManhattanDistance = resolved.ManhattanDistance,
            LogSlope = resolved.LogSlope,
            Context = context,
            Constraints = resolved.Constraints,
            SourceDatabase = sourceDatabase,
            SourceEntries = sourceEntries
        });
    }

    private EscortGenerationResolvedConfig ResolveEscortGeneration(PoolEvalContext context)
    {
        if (_config.escortGenerationConfig != null)
            return _config.escortGenerationConfig.Resolve(context, _random);

        var legacySettings = _config.legacyEscortGenerationSettings;
        var constraints = legacySettings != null
            ? legacySettings.ToConstraints(context)
            : EscortLevelGenerationConstraints.Default;

        int distance = legacySettings != null
            ? legacySettings.ClampEscortManhattanDistance(legacySettings.defaultManhattanDistance, context)
            : constraints.DefaultManhattanDistance;

        return new EscortGenerationResolvedConfig(constraints, distance, 0f);
    }

    private LevelCollageSourceDatabase ResolveEscortSourceDatabase()
    {
        if (_config.escortFeatureSelectionTable != null && _config.escortFeatureSelectionTable.sourceDatabase != null)
            return _config.escortFeatureSelectionTable.sourceDatabase;

        if (_config.levelSourceDatabase != null)
            return _config.levelSourceDatabase;

        return _config.legacyEscortGenerationSettings != null
            ? _config.legacyEscortGenerationSettings.sourceDatabase
            : null;
    }

    private void BuildPostCombatOffer()
    {
        ClearCurrentPostCombatOffer();
        BasePackData eventPack = null;
        string eventTitle = null;
        _config?.eventStagePool?.TryRollStagePack(_random, out eventPack, out eventTitle);

        var runtimeShop = BuildRuntimeShop();
        CurrentPostCombatOffer = new RunPostCombatOffer
        {
            Shop = runtimeShop,
            ShopIsRuntime = runtimeShop != null,
            EventPack = eventPack,
            EventTitle = eventTitle
        };
    }

    private ShopSO BuildRuntimeShop()
    {
        if (_config == null)
            return null;

        DestroyRuntimeShop();

        var shop = ScriptableObject.CreateInstance<ShopSO>();
        shop.hideFlags = HideFlags.HideAndDontSave;
        shop.shopId = $"runtime_round_shop_{RoundIndex}";
        shop.title = RuntimeShopTitle;
        shop.description = RuntimeShopDescription;
        shop.itemPool = _config.shopItemPool;
        shop.items = new List<ShopSO.ShopItem>();

        _runtimePostCombatShop = shop;
        return shop;
    }

    private void ClearCurrentRoundOffer()
    {
        if (CurrentOffer != null &&
            CurrentOffer.EscortLevelIsRuntime &&
            CurrentOffer.EscortLevel != null &&
            CurrentOffer.EscortLevel != _activeCombatLevel)
        {
            Destroy(CurrentOffer.EscortLevel);
        }

        CurrentOffer = null;
    }

    private void ClearUnselectedRoundOffer(RunMainStageMode selectedMode)
    {
        if (CurrentOffer == null)
            return;

        if (selectedMode != RunMainStageMode.Escort &&
            CurrentOffer.EscortLevelIsRuntime &&
            CurrentOffer.EscortLevel != null)
        {
            Destroy(CurrentOffer.EscortLevel);
            CurrentOffer.EscortLevel = null;
            CurrentOffer.EscortLevelIsRuntime = false;
        }
    }

    private void ClearActiveCombatLevel()
    {
        if (_activeCombatLevelIsRuntime && _activeCombatLevel != null)
            Destroy(_activeCombatLevel);

        _activeCombatLevel = null;
        _activeCombatLevelIsRuntime = false;
    }

    private void ClearCurrentPostCombatOffer()
    {
        if (CurrentPostCombatOffer != null && CurrentPostCombatOffer.ShopIsRuntime)
            DestroyRuntimeShop();

        CurrentPostCombatOffer = null;
    }

    private void DestroyRuntimeShop()
    {
        if (_runtimePostCombatShop != null)
        {
            Destroy(_runtimePostCombatShop);
            _runtimePostCombatShop = null;
        }
    }

    private void BroadcastCurrentState()
    {
        RunRoundUIStateRegistry.Apply(this);
        ShowBgmRecord();

        switch (State)
        {
            case RunRoundState.RoundOffer:
                SetCameraFlowPaused(true);
                break;

            case RunRoundState.CombatSettlement:
                SetCameraFlowPaused(true);
                break;

            case RunRoundState.PostCombatOffer:
                SetCameraFlowPaused(true);
                break;

            case RunRoundState.Event:
                SetCameraFlowPaused(true);
                break;

            case RunRoundState.Defeat:
                SetCameraFlowPaused(true);
                break;

            case RunRoundState.RunComplete:
                SetCameraFlowPaused(true);
                break;
        }
    }

    private static void ShowBgmRecord()
    {
        PostSystem.Instance?.Send("期望显示面板", new BgmRecordUIRequest(BgmRecordUIIds.RecordButton));
    }

    private static void SetCameraFlowPaused(bool paused)
    {
        Camera.main?.GetComponent<CameraController>()?.SetFlowPaused(paused);
    }

    private static void AddGold(int amount, string reason)
    {
        if (amount <= 0)
            return;

        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        inventory?.AddGold(amount);
        Debug.Log($"[RunRoundController] Gold awarded: amount={amount}, reason={reason}");
    }

    private RunCombatSettlement BuildCombatSettlement(RunMainStageMode completedMode, LevelData completedLevel, LevelPlayer completedPlayer, string message)
    {
        int goldAfter = GetGold();
        var status = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        var summary = completedPlayer != null ? completedPlayer.LastSettlementSummary : null;

        var settlement = new RunCombatSettlement
        {
            RoundIndex = RoundIndex,
            RoundCount = RoundCount,
            EncounterCycleIndex = EncounterCycleIndex,
            EncounterCyclesPerRound = EncounterCyclesPerRound,
            Mode = completedMode,
            LevelName = completedLevel != null ? completedLevel.name : null,
            GoldBefore = _combatStartGold,
            GoldAfter = goldAfter,
            GoldDelta = goldAfter - _combatStartGold,
            SuccessfulBoxCount = summary != null ? summary.SuccessfulBoxCount : 0,
            Hp = status != null ? status.CurrentHp : 0,
            MaxHp = status != null ? status.MaxHp : 0,
            Message = message
        };

        if (summary != null)
        {
            for (int i = 0; i < summary.RewardLines.Count; i++)
            {
                var line = summary.RewardLines[i];
                if (line == null)
                    continue;

                settlement.RewardLines.Add(new RunCombatSettlementRewardLine
                {
                    Label = line.Label,
                    Gold = line.Gold,
                    IsHeader = line.IsHeader
                });
            }
        }

        return settlement;
    }

    private static int GetGold()
    {
        return GraphHub.Instance?.GetFacade<RunInventoryFacade>()?.Gold ?? 0;
    }

    private RewardSO RollSkipReward()
    {
        if (_config?.skipRewardPool == null)
            return null;

        if (!_config.skipRewardPool.TryRollReward(_random, out var reward) || reward == null)
            return null;

        return reward.rollFromPool && reward.TryResolveReward(_random, out var resolvedReward)
            ? resolvedReward
            : reward;
    }

    private static bool ApplyReward(RewardSO reward, string reason)
    {
        if (reward == null)
            return false;

        switch (reward.rewardKind)
        {
            case RewardSO.RewardKind.AddGold:
                AddGold(reward.goldAmount, reason);
                return true;

            case RewardSO.RewardKind.AddCardsToDeck:
                return AddCardsToDeck(reward);

            case RewardSO.RewardKind.AddItem:
                return AddItemReward(reward);

            case RewardSO.RewardKind.HealRunHp:
                return HealRunHp(reward);

            default:
                Debug.LogWarning($"[RunRoundController] Unsupported reward kind: {reward.rewardKind}");
                return false;
        }
    }

    private static bool AddCardsToDeck(RewardSO reward)
    {
        var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck == null)
        {
            deck = new CardDeckFacade();
            GraphHub.Instance?.RegisterFacade(deck);
        }

        if (deck == null || reward.cards == null)
            return false;

        bool grantedAny = false;
        foreach (var entry in reward.cards)
        {
            if (entry == null || entry.card == null)
                continue;

            grantedAny |= deck.AddCard(entry.card, Mathf.Max(1, entry.count));
        }

        return grantedAny;
    }

    private static string BuildRewardLabel(RewardSO reward)
    {
        if (reward == null)
            return null;

        if (!string.IsNullOrWhiteSpace(reward.rewardId))
            return reward.rewardId;

        switch (reward.rewardKind)
        {
            case RewardSO.RewardKind.AddGold:
                return $"+{Mathf.Max(0, reward.goldAmount)} 金币";

            case RewardSO.RewardKind.AddCardsToDeck:
                int count = 0;
                if (reward.cards != null)
                {
                    foreach (var entry in reward.cards)
                    {
                        if (entry != null && entry.card != null)
                            count += Mathf.Max(1, entry.count);
                    }
                }

                return count > 0 ? $"+{count} 张卡牌" : "卡牌奖励";

            case RewardSO.RewardKind.AddItem:
                string itemName = reward.item != null ? reward.item.ResolvedDisplayName : reward.inventoryItemId;
                return string.IsNullOrWhiteSpace(itemName) ? "道具奖励" : $"+{Mathf.Max(1, reward.itemCount)} {itemName}";

            case RewardSO.RewardKind.HealRunHp:
                return $"+{Mathf.Max(1, reward.healAmount)} 生命";

            default:
                return "奖励";
        }
    }

    private static bool AddItemReward(RewardSO reward)
    {
        string itemId = reward.item != null ? reward.item.ResolvedItemId : reward.inventoryItemId;
        string itemType = reward.item != null ? reward.item.itemType : reward.inventoryItemType;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        return inventory != null && inventory.AddItem(itemId, itemType, Mathf.Max(1, reward.itemCount));
    }

    private static bool HealRunHp(RewardSO reward)
    {
        var status = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        if (status == null)
        {
            status = new RunPlayerStatusFacade();
            GraphHub.Instance?.RegisterFacade(status);
        }

        return status != null && status.Heal(Mathf.Max(1, reward.healAmount));
    }
}
