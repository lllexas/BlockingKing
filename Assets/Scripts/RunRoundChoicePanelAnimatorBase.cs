using SpaceTUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class RunRoundChoicePanelAnimatorBase<TRequest> : SpaceUIAnimator
    where TRequest : RunRoundChoiceUIRequest
{
    [Header("Choice UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private TextMeshProUGUI footerText;
    [SerializeField] private Button button;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => FadeOut();
    }

    private void OnShowPanel(object data)
    {
        if (data is not TRequest request)
            return;

        SetText(titleText, request.Title);
        SetText(bodyText, request.Body);
        SetText(footerText, request.Footer);
        BindButton(request);
        FadeIn();
    }

    protected override void CloseAction()
    {
        FadeOut();
    }

    protected abstract void InvokeChoice(RunRoundController controller);

    private void BindButton(TRequest request)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.interactable = request.Interactable && request.Controller != null;
        if (button.interactable)
            button.onClick.AddListener(() => InvokeChoice(request.Controller));
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }
}
