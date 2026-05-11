using NekoGraph;
using UnityEngine;

[DefaultExecutionOrder(120)]
public sealed class RunRoundOnGUIFrontend : MonoBehaviour
{
    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _disabledButtonStyle;
    private GUIStyle _goldStyle;

    private void OnGUI()
    {
        var flow = GameFlowController.Instance;
        if (flow == null || flow.Mode != GameFlowMode.RoundFlow || flow.IsInLevel)
            return;

        var controller = FindObjectOfType<RunRoundController>();
        if (controller == null)
            return;

        EnsureStyles();
        int oldDepth = GUI.depth;
        GUI.depth = -20;

        float width = Mathf.Clamp(Screen.width * 0.72f, 860f, 1280f);
        float height = Mathf.Clamp(Screen.height * 0.68f, 560f, 820f);
        var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUILayout.BeginArea(rect, _panelStyle);
        DrawHeader(controller);
        GUILayout.Space(18f);

        switch (controller.State)
        {
            case RunRoundState.RoundOffer:
                DrawRoundOffer(controller);
                break;
            case RunRoundState.PostCombatOffer:
                DrawPostCombatOffer(controller);
                break;
            case RunRoundState.Event:
                DrawEvent(controller);
                break;
            case RunRoundState.Shop:
                GUILayout.Label("商店已打开。", _bodyStyle);
                break;
            case RunRoundState.Defeat:
                DrawDefeat(controller);
                break;
            case RunRoundState.RunComplete:
                DrawRunComplete(controller);
                break;
            default:
                GUILayout.Label(controller.StatusMessage ?? controller.State.ToString(), _bodyStyle);
                break;
        }

        GUILayout.EndArea();
        GUI.depth = oldDepth;
    }

    private void DrawHeader(RunRoundController controller)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Round {controller.RoundIndex}/{controller.RoundCount} · {controller.EncounterCycleIndex}/{controller.EncounterCyclesPerRound}", _titleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"HP {GetHpText()}", _goldStyle, GUILayout.Width(180f));
        GUILayout.Label($"金币 {GetGold()}", _goldStyle, GUILayout.Width(180f));
        GUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(controller.StatusMessage))
        {
            GUILayout.Space(8f);
            GUILayout.Label(controller.StatusMessage, _bodyStyle);
        }
    }

    private void DrawRoundOffer(RunRoundController controller)
    {
        var offer = controller.CurrentOffer;
        if (offer == null)
        {
            GUILayout.Label("No round offer.", _bodyStyle);
            return;
        }

        GUILayout.Label($"强度 {offer.Difficulty.OverallDifficulty:0.##} / 进度 {offer.Difficulty.Progress:P0}", _bodyStyle);
        GUILayout.Space(16f);
        GUILayout.BeginHorizontal();

        DrawMainOfferButton(
            "Classic",
            offer.ClassicLevel != null ? offer.ClassicLevel.name : "未生成",
            offer.ClassicWouldAlternate ? $"+{offer.AlternateBonusGold} 交替奖励" : "常规奖励",
            offer.ClassicLevel != null,
            controller.ChooseClassic);

        DrawMainOfferButton(
            "Escort",
            offer.EscortLevel != null ? offer.EscortLevel.name : "未生成",
            offer.EscortWouldAlternate ? $"+{offer.AlternateBonusGold} 交替奖励" : "常规奖励",
            offer.EscortLevel != null,
            controller.ChooseEscort);

        DrawMainOfferButton(
            "放弃本轮",
            !string.IsNullOrWhiteSpace(offer.SkipRewardLabel) ? offer.SkipRewardLabel : "无奖励",
            "跳过本轮主战斗",
            true,
            controller.SkipRound);

        GUILayout.EndHorizontal();
    }

    private void DrawPostCombatOffer(RunRoundController controller)
    {
        GUILayout.Label("战后选择", _titleStyle);
        GUILayout.Space(14f);
        GUILayout.BeginHorizontal();

        DrawMainOfferButton("商店", "进入商店", "购买卡牌或道具", true, controller.ChooseShop);
        DrawMainOfferButton("不期而遇", "即时事件", "获得事件奖励", true, controller.ChooseEvent);

        GUILayout.EndHorizontal();
    }

    private void DrawEvent(RunRoundController controller)
    {
        GUILayout.Label("不期而遇", _titleStyle);
        GUILayout.Space(12f);
        GUILayout.Label(string.IsNullOrWhiteSpace(controller.EventMessage) ? "事件完成。" : controller.EventMessage, _bodyStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("继续", _buttonStyle, GUILayout.Height(58f)))
            controller.ConfirmEvent();
    }

    private void DrawRunComplete(RunRoundController controller)
    {
        GUILayout.Label("Run Complete", _titleStyle);
        GUILayout.Space(12f);
        GUILayout.Label(controller.StatusMessage ?? "本轮流程已结束。", _bodyStyle);
    }

    private void DrawDefeat(RunRoundController controller)
    {
        GUILayout.Label("Run Failed", _titleStyle);
        GUILayout.Space(12f);
        GUILayout.Label(controller.StatusMessage ?? "玩家失败。", _bodyStyle);
    }

    private void DrawMainOfferButton(string title, string subtitle, string detail, bool enabled, System.Action onClick)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.MinHeight(220f));
        GUILayout.Label(title, _titleStyle);
        GUILayout.Label(subtitle, _bodyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(detail, _bodyStyle);
        GUI.enabled = enabled;
        if (GUILayout.Button(enabled ? "选择" : "不可用", enabled ? _buttonStyle : _disabledButtonStyle, GUILayout.Height(58f)))
            onClick?.Invoke();
        GUI.enabled = true;
        GUILayout.EndVertical();
    }

    private static int GetGold()
    {
        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        return inventory != null ? inventory.Gold : 0;
    }

    private static string GetHpText()
    {
        var status = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        return status != null ? $"{status.CurrentHp}/{status.MaxHp}" : "-";
    }

    private void EnsureStyles()
    {
        _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(28, 28, 24, 24)
        };

        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };

        _bodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };

        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        _disabledButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        _goldStyle ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }
}
