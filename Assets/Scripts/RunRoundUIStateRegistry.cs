using System.Collections.Generic;
using NekoGraph;

public static class RunRoundUIStateRegistry
{
    private static readonly string[] AllPanelIds =
    {
        RunRoundUIIds.Backdrop,
        RunRoundUIIds.Hud,
        RunRoundUIIds.ClassicChoice,
        RunRoundUIIds.EscortChoice,
        RunRoundUIIds.SkipChoice,
        RunRoundUIIds.ShopChoice,
        RunRoundUIIds.EventChoice,
        RunRoundUIIds.CombatSettlement,
        RunRoundUIIds.Result,
        RunRoundUIIds.DeckPanel
    };

    private static readonly Dictionary<RunRoundState, string[]> StatePanels = new()
    {
        { RunRoundState.Inactive, new string[] { } },
        { RunRoundState.RoundOffer, new[] { RunRoundUIIds.Backdrop, RunRoundUIIds.Hud, RunRoundUIIds.ClassicChoice, RunRoundUIIds.EscortChoice, RunRoundUIIds.SkipChoice } },
        { RunRoundState.InCombat, new string[] { } },
        { RunRoundState.CombatSettlement, new[] { RunRoundUIIds.Backdrop, RunRoundUIIds.Hud, RunRoundUIIds.CombatSettlement } },
        { RunRoundState.PostCombatOffer, new[] { RunRoundUIIds.Backdrop, RunRoundUIIds.Hud, RunRoundUIIds.ShopChoice, RunRoundUIIds.EventChoice } },
        { RunRoundState.Shop, new[] { RunRoundUIIds.Backdrop, RunRoundUIIds.Hud } },
        { RunRoundState.Event, new[] { RunRoundUIIds.Backdrop, RunRoundUIIds.Hud } },
        { RunRoundState.EventStage, new[] { RunRoundUIIds.Backdrop } },
        { RunRoundState.Defeat, new[] { RunRoundUIIds.Backdrop, RunRoundUIIds.Result } },
        { RunRoundState.RunComplete, new[] { RunRoundUIIds.Backdrop, RunRoundUIIds.Result } }
    };

    public static void Apply(RunRoundController controller)
    {
        if (controller == null)
            return;

        string[] visiblePanels = GetVisiblePanels(controller.State);
        HidePanelsOutside(visiblePanels);

        foreach (string panelId in visiblePanels)
            ShowPanel(controller, panelId);
    }

    public static void HideAll()
    {
        foreach (string panelId in AllPanelIds)
            HidePanel(panelId);
    }

    private static string[] GetVisiblePanels(RunRoundState state)
    {
        return StatePanels.TryGetValue(state, out var panels)
            ? panels
            : StatePanels[RunRoundState.Inactive];
    }

    private static void HidePanelsOutside(string[] visiblePanels)
    {
        foreach (string panelId in AllPanelIds)
        {
            if (!ContainsPanel(visiblePanels, panelId))
                HidePanel(panelId);
        }
    }

    private static bool ContainsPanel(IReadOnlyList<string> panels, string panelId)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            if (panels[i] == panelId)
                return true;
        }

        return false;
    }

    private static void ShowPanel(RunRoundController controller, string panelId)
    {
        object request = BuildShowRequest(controller, panelId);
        if (request != null)
            PostSystem.Instance?.Send("期望显示面板", request);
    }

    private static void HidePanel(string panelId)
    {
        PostSystem.Instance?.Send("期望隐藏面板", panelId);
    }

    private static object BuildShowRequest(RunRoundController controller, string panelId)
    {
        switch (panelId)
        {
            case RunRoundUIIds.Backdrop:
                return new RunRoundBackdropUIRequest();

            case RunRoundUIIds.Hud:
                return new RunRoundHudUIRequest
                {
                    Controller = controller,
                    StatusMessage = controller.StatusMessage
                };

            case RunRoundUIIds.ClassicChoice:
                return BuildClassicChoiceRequest(controller);

            case RunRoundUIIds.EscortChoice:
                return BuildEscortChoiceRequest(controller);

            case RunRoundUIIds.SkipChoice:
                return BuildSkipChoiceRequest(controller);

            case RunRoundUIIds.ShopChoice:
                return BuildShopChoiceRequest(controller);

            case RunRoundUIIds.EventChoice:
                return BuildEventChoiceRequest(controller);

            case RunRoundUIIds.CombatSettlement:
                return new RunCombatSettlementUIRequest
                {
                    Controller = controller,
                    Settlement = controller.CurrentCombatSettlement
                };

            case RunRoundUIIds.Result:
                return BuildResultRequest(controller);
        }

        return null;
    }

    private static RunRoundClassicChoiceUIRequest BuildClassicChoiceRequest(RunRoundController controller)
    {
        var offer = controller.CurrentOffer;
        return new RunRoundClassicChoiceUIRequest
        {
            Controller = controller,
            Title = "Classic",
            Body = offer?.ClassicLevel != null ? offer.ClassicLevel.name : "未生成",
            Footer = offer != null && offer.ClassicWouldAlternate
                ? $"+{offer.AlternateBonusGold} 交替奖励"
                : "常规奖励",
            Interactable = offer?.ClassicLevel != null
        };
    }

    private static RunRoundEscortChoiceUIRequest BuildEscortChoiceRequest(RunRoundController controller)
    {
        var offer = controller.CurrentOffer;
        return new RunRoundEscortChoiceUIRequest
        {
            Controller = controller,
            Title = "Escort",
            Body = offer?.EscortLevel != null ? offer.EscortLevel.name : "未生成",
            Footer = offer != null && offer.EscortWouldAlternate
                ? $"+{offer.AlternateBonusGold} 交替奖励"
                : "常规奖励",
            Interactable = offer?.EscortLevel != null
        };
    }

    private static RunRoundSkipChoiceUIRequest BuildSkipChoiceRequest(RunRoundController controller)
    {
        var offer = controller.CurrentOffer;
        return new RunRoundSkipChoiceUIRequest
        {
            Controller = controller,
            Title = "放弃本轮",
            Body = offer != null && !string.IsNullOrWhiteSpace(offer.SkipRewardLabel)
                ? offer.SkipRewardLabel
                : "无奖励",
            Footer = "跳过主战斗",
            Interactable = true
        };
    }

    private static RunRoundShopChoiceUIRequest BuildShopChoiceRequest(RunRoundController controller)
    {
        var offer = controller.CurrentPostCombatOffer;
        return new RunRoundShopChoiceUIRequest
        {
            Controller = controller,
            Title = "商店",
            Body = offer?.Shop != null ? offer.Shop.title : "未配置商店",
            Footer = "购买卡牌或道具",
            Interactable = offer?.Shop != null
        };
    }

    private static RunRoundEventChoiceUIRequest BuildEventChoiceRequest(RunRoundController controller)
    {
        var offer = controller.CurrentPostCombatOffer;
        return new RunRoundEventChoiceUIRequest
        {
            Controller = controller,
            Title = "不期而遇",
            Body = !string.IsNullOrWhiteSpace(offer?.EventTitle)
                ? offer.EventTitle
                : "即时事件",
            Footer = offer?.EventPack != null ? "运行事件" : "获得事件奖励",
            Interactable = true
        };
    }

    private static RunResultUIRequest BuildResultRequest(RunRoundController controller)
    {
        return new RunResultUIRequest
        {
            Controller = controller,
            Victory = controller.State == RunRoundState.RunComplete,
            Message = controller.State == RunRoundState.RunComplete
                ? controller.StatusMessage ?? "本轮流程已结束。"
                : controller.StatusMessage
        };
    }
}
