using SpaceTUI;
using TMPro;
using UnityEngine;

public sealed class RunResultPanelAnimator : SpaceUIAnimator
{
    [Header("Run Result UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;

    protected override string UIID => RunRoundUIIds.Result;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => this.FadeOutIfVisible();
    }

    private void OnShowPanel(object data)
    {
        if (data is not RunResultUIRequest request)
            return;

        SetText(titleText, request.Victory ? "Run Complete" : "Run Failed");
        SetText(bodyText, request.Message);
        this.FadeInIfHidden();
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
