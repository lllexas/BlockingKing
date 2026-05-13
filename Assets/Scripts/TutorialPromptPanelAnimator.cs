using SpaceTUI;
using TMPro;
using UnityEngine;

public sealed class TutorialPromptPanelAnimator : SpaceUIAnimator
{
    [Header("Prompt UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;

    protected override string UIID => TutorialUIIds.Prompt;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += OnHidePanel;
    }

    private void OnShowPanel(object data)
    {
        if (data is not TutorialPromptUIRequest request)
            return;

        SetText(titleText, request.Title);
        SetText(bodyText, request.Message);
        this.FadeInIfHidden();
    }

    private void OnHidePanel(object data)
    {
        if (data is string panelId)
        {
            if (panelId != UIID)
                return;
        }
        else if (data is not TutorialPromptUIRequest)
        {
            return;
        }

        this.FadeOutIfVisible();
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
