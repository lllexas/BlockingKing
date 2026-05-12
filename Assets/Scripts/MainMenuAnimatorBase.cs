using SpaceTUI;
using UnityEngine;

public abstract class MainMenuAnimatorBase : SpaceUIAnimator
{
    protected GameFlowController Controller { get; private set; }
    protected RunConfigSO RunConfig { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => HideMenuPart();
    }

    private void OnShowPanel(object data)
    {
        if (data is not MainMenuUIRequest request)
            return;

        Controller = request.Controller;
        RunConfig = request.RunConfig;
        Refresh(request);
        if (request.Instant)
            ShowMenuPartInstant();
        else
            ShowMenuPart();
    }

    protected override void CloseAction()
    {
        HideMenuPart();
    }

    protected virtual void Refresh(MainMenuUIRequest request)
    {
    }

    protected virtual void ShowMenuPart()
    {
        this.FadeInIfHiddenPreserveRotation();
    }

    protected virtual void ShowMenuPartInstant()
    {
        if (_canvasGroup != null && !_canvasGroup.blocksRaycasts)
            Show();
    }

    protected virtual void HideMenuPart()
    {
        StopBreathing();
        ResetScale();
        this.FadeOutIfVisible();
    }
}
