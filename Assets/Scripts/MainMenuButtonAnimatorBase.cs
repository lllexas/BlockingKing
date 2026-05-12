using UnityEngine;
using UnityEngine.EventSystems;

public abstract class MainMenuButtonAnimatorBase : MainMenuAnimatorBase
{
    [Header("Button Motion")]
    [SerializeField] private float hoverScale = 1.06f;
    [SerializeField] private float idleBreathScale = 0.015f;
    [SerializeField] private float clickCooldown = 0.25f;

    private float _nextClickAllowedAt;

    protected override void Awake()
    {
        base.Awake();
        鼠标滑入 += OnHoverEnter;
        鼠标滑出 += OnHoverExit;
        鼠标点击 += OnClick;
    }

    protected override void ShowMenuPart()
    {
        _breathScaleAmplitude = idleBreathScale;
        this.FadeInIfHidden();
        StartBreathing();
    }

    protected override void HideMenuPart()
    {
        StopBreathing();
        ResetScale();
        this.FadeOutIfVisible();
    }

    private void OnHoverEnter(PointerEventData eventData)
    {
        SetTargetScale(_initialScale * hoverScale);
        PlayScaleAnimation();
    }

    private void OnHoverExit(PointerEventData eventData)
    {
        SetTargetScale(_initialScale);
        PlayScaleAnimation();
    }

    private void OnClick(PointerEventData eventData)
    {
        if (!IsVisible || Time.unscaledTime < _nextClickAllowedAt)
            return;

        _nextClickAllowedAt = Time.unscaledTime + clickCooldown;
        Invoke();
    }

    protected abstract void Invoke();
}
