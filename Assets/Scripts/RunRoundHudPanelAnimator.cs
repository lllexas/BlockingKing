using NekoGraph;
using SpaceTUI;
using TMPro;
using UnityEngine;

public sealed class RunRoundHudPanelAnimator : SpaceUIAnimator
{
    [Header("HUD UI")]
    [SerializeField] private TextMeshProUGUI roundText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI statusText;

    protected override string UIID => RunRoundUIIds.Hud;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => this.FadeOutIfVisible();
    }

    private void OnShowPanel(object data)
    {
        if (data is not RunRoundHudUIRequest request)
            return;

        Refresh(request);
        this.FadeInIfHidden();
    }

    private void Refresh(RunRoundHudUIRequest request)
    {
        var controller = request.Controller;
        if (controller != null)
            SetText(roundText, $"Round {controller.RoundIndex}/{controller.RoundCount} · {controller.EncounterCycleIndex}/{controller.EncounterCyclesPerRound}");

        var status = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        SetText(hpText, status != null ? $"HP {status.CurrentHp}/{status.MaxHp}" : "HP -");

        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        SetText(goldText, inventory != null ? $"金币 {inventory.Gold}" : "金币 0");
        SetText(statusText, request.StatusMessage);
    }

    protected override void CloseAction()
    {
        this.FadeOutIfVisible();
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }
}
