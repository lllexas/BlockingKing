using SpaceTUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RunResultPanelAnimator : SpaceUIAnimator
{
    [Header("Run Result UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;

    [Header("Buttons")]
    [SerializeField] private Button returnMainMenuButton;

    protected override string UIID => RunRoundUIIds.Result;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => this.FadeOutIfVisible();
        BindButtons();
    }

    protected override void OnDestroy()
    {
        UnbindButtons();
        base.OnDestroy();
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

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        this.FadeOutIfVisible();
        GameFlowController.Instance?.ReturnToMainMenuRound();
    }

    private void BindButtons()
    {
        if (returnMainMenuButton != null)
            returnMainMenuButton.onClick.AddListener(ReturnToMainMenu);
    }

    private void UnbindButtons()
    {
        if (returnMainMenuButton != null)
            returnMainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }
}
