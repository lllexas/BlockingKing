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
    private RunRoundHudUIRequest _lastRequest;
    private bool _visible;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => HideHud();
    }

    private void OnShowPanel(object data)
    {
        if (data is not RunRoundHudUIRequest request)
            return;

        _lastRequest = request;
        _visible = true;
        Refresh(request);
        this.FadeInIfHidden();
    }

    protected override void Update()
    {
        base.Update();
        if (_visible && _lastRequest != null)
            Refresh(_lastRequest);
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
        _visible = false;
        this.FadeOutIfVisible();
    }

    private void HideHud()
    {
        _visible = false;
        this.FadeOutIfVisible();
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }
}
