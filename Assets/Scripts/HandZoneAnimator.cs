using SpaceTUI;

public sealed class HandZoneAnimator : SpaceUIAnimator
{
    protected override string UIID => string.Empty;

    protected override void Awake()
    {
        base.Awake();
        Hide();
    }

    protected override void CloseAction()
    {
        HideHandZone();
    }

    public void ShowHandZone()
    {
        this.FadeInIfHidden();
    }

    public void HideHandZone()
    {
        StopBreathing();
        ResetScale();
        this.FadeOutIfVisible();
    }
}
