using System;
using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

public enum RunRoundState
{
    Inactive,
    RoundOffer,
    InCombat,
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
    public RunMainStageMode LastCompletedMode { get; private set; } = RunMainStageMode.None;
    public RunMainStageMode ActiveMode { get; private set; } = RunMainStageMode.None;
    public int RoundIndex { get; private set; }
    public int RoundCount => _config != null ? Mathf.Max(1, _config.totalRounds) : 1;
    public string StatusMessage { get; private set; }
    public string EventMessage { get; private set; }

    private RunRoundConfigSO _config;
    private RunConfigSO _runConfig;
    private System.Random _random;
    private LevelData _activeCombatLevel;
    private bool _activeCombatLevelIsRuntime;
    private ShopSO _runtimePostCombatShop;

    public void StartRun(RunConfigSO runConfig, RunRoundConfigSO config)
    {
        _runConfig = runConfig;
        _config = config;
        int seed = config != null ? config.seed : Environment.TickCount;
        _random = seed == 0 ? new System.Random() : new System.Random(seed);
        RoundIndex = 0;
        LastCompletedMode = RunMainStageMode.None;
        ActiveMode = RunMainStageMode.None;
        StatusMessage = null;
        EventMessage = null;
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
        AdvanceRound();
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

        LastCompletedMode = ActiveMode;
        ActiveMode = RunMainStageMode.None;
        StatusMessage = alternated
            ? $"交替完成，获得 {CurrentOffer.AlternateBonusGold} 金币。"
            : "战斗完成。";
        BuildPostCombatOffer();
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
            AdvanceRound();
            BroadcastCurrentState();
            return;
        }

        State = RunRoundState.Shop;
        StatusMessage = "进入商店。";
        HideRoundPanels();
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
        AdvanceRound();
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
                HideRoundPanels();
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
        AdvanceRound();
        BroadcastCurrentState();
    }

    public void OnEventStageCompleted()
    {
        if (State != RunRoundState.EventStage)
            return;

        StatusMessage = "不期而遇完成。";
        ClearCurrentPostCombatOffer();
        AdvanceRound();
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
        ClearUnselectedRoundOffer(mode);
        State = RunRoundState.InCombat;
        HideRoundPanels();
        GameFlowController.Instance?.EnterLevel();

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

    private void AdvanceRound()
    {
        RoundIndex++;
        if (RoundIndex >= RoundCount)
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
            progress = RoundCount > 1 ? Mathf.Clamp01(RoundIndex / (float)(RoundCount - 1)) : 0f,
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
        var legacySettings = _config.legacyEscortGenerationSettings;
        var sourceDatabase = ResolveEscortSourceDatabase();
        if (sourceDatabase == null)
            return null;

        var constraints = legacySettings != null
            ? legacySettings.ToConstraints(context)
            : EscortLevelGenerationConstraints.Default;

        int distance = legacySettings != null
            ? legacySettings.ClampEscortManhattanDistance(legacySettings.defaultManhattanDistance, context)
            : constraints.DefaultManhattanDistance;

        return EscortLevelGenerator.CreateFromRandomClassicMap(new EscortLevelBuildRequest
        {
            Seed = context.seed,
            ManhattanDistance = distance,
            LogSlope = 0f,
            Context = context,
            Constraints = constraints,
            SourceDatabase = sourceDatabase
        });
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
        HideRoundPanels();

        switch (State)
        {
            case RunRoundState.RoundOffer:
                ShowHud();
                PostSystem.Instance?.Send("期望显示面板", new RunRoundClassicChoiceUIRequest
                {
                    Controller = this,
                    Title = "Classic",
                    Body = CurrentOffer?.ClassicLevel != null ? CurrentOffer.ClassicLevel.name : "未生成",
                    Footer = CurrentOffer != null && CurrentOffer.ClassicWouldAlternate
                        ? $"+{CurrentOffer.AlternateBonusGold} 交替奖励"
                        : "常规奖励",
                    Interactable = CurrentOffer?.ClassicLevel != null
                });
                PostSystem.Instance?.Send("期望显示面板", new RunRoundEscortChoiceUIRequest
                {
                    Controller = this,
                    Title = "Escort",
                    Body = CurrentOffer?.EscortLevel != null ? CurrentOffer.EscortLevel.name : "未生成",
                    Footer = CurrentOffer != null && CurrentOffer.EscortWouldAlternate
                        ? $"+{CurrentOffer.AlternateBonusGold} 交替奖励"
                        : "常规奖励",
                    Interactable = CurrentOffer?.EscortLevel != null
                });
                PostSystem.Instance?.Send("期望显示面板", new RunRoundSkipChoiceUIRequest
                {
                    Controller = this,
                    Title = "放弃本轮",
                    Body = CurrentOffer != null && !string.IsNullOrWhiteSpace(CurrentOffer.SkipRewardLabel)
                        ? CurrentOffer.SkipRewardLabel
                        : "无奖励",
                    Footer = "跳过主战斗",
                    Interactable = true
                });
                break;

            case RunRoundState.PostCombatOffer:
                ShowHud();
                PostSystem.Instance?.Send("期望显示面板", new RunRoundShopChoiceUIRequest
                {
                    Controller = this,
                    Title = "商店",
                    Body = CurrentPostCombatOffer?.Shop != null ? CurrentPostCombatOffer.Shop.title : "未配置商店",
                    Footer = "购买卡牌或道具",
                    Interactable = CurrentPostCombatOffer?.Shop != null
                });
                PostSystem.Instance?.Send("期望显示面板", new RunRoundEventChoiceUIRequest
                {
                    Controller = this,
                    Title = "不期而遇",
                    Body = !string.IsNullOrWhiteSpace(CurrentPostCombatOffer?.EventTitle)
                        ? CurrentPostCombatOffer.EventTitle
                        : "即时事件",
                    Footer = CurrentPostCombatOffer?.EventPack != null ? "运行事件" : "获得事件奖励",
                    Interactable = true
                });
                break;

            case RunRoundState.Event:
                ShowHud();
                break;

            case RunRoundState.Defeat:
                PostSystem.Instance?.Send("期望显示面板", new RunResultUIRequest
                {
                    Controller = this,
                    Victory = false,
                    Message = StatusMessage
                });
                break;

            case RunRoundState.RunComplete:
                PostSystem.Instance?.Send("期望显示面板", new RunResultUIRequest
                {
                    Controller = this,
                    Victory = true,
                    Message = StatusMessage ?? "本轮流程已结束。"
                });
                break;
        }
    }

    private void ShowHud()
    {
        PostSystem.Instance?.Send("期望显示面板", new RunRoundHudUIRequest
        {
            Controller = this,
            StatusMessage = StatusMessage
        });
    }

    private static void HideRoundPanels()
    {
        PostSystem.Instance?.Send("期望隐藏所有面板", null);
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
