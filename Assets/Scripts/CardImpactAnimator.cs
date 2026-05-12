using SpaceTUI;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public sealed class CardImpactAnimator : SpaceUIAnimator
{
    protected override string UIID => string.Empty;

    [Header("Card Impact")]
    [SerializeField] private RectTransform impactTarget;
    [SerializeField] private float drawBeatCount = 0.65f;
    [SerializeField] private float hoverBeatCount = 0.75f;
    [SerializeField] private float pressBeatCount = 0.35f;
    [SerializeField] private float aimBreathBeatCount = 1f;
    [SerializeField] private float releaseBeatCount = 0.55f;

    [Header("Scale")]
    [SerializeField] private float drawScale = 1.08f;
    [SerializeField] private float hoverScale = 1.035f;
    [SerializeField] private float pressScale = 0.94f;
    [SerializeField] private float aimBreathScale = 1.025f;
    [SerializeField] private float releaseScale = 0.88f;

    [Header("Rotation")]
    [SerializeField] private float hoverWobbleYaw = 8f;
    [SerializeField] private float hoverDoorOscillations = 1.35f;
    [SerializeField] private float hoverDoorDamping = 3.2f;
    [SerializeField] private float pressYaw = -3f;
    [SerializeField] private float releaseYaw = 12f;

    private Vector3 _impactBaseScale = Vector3.one;
    private Vector3 _impactBaseEuler;
    private Tween _impactScaleTween;
    private Tween _wobbleTween;
    private Tween _aimTween;
    private CardView _cardView;
    private bool _subscribed;

    protected override void Awake()
    {
        base.Awake();
        ForceRaycastPassThrough();

        if (impactTarget != null)
        {
            _impactBaseScale = impactTarget.localScale;
            _impactBaseEuler = impactTarget.localEulerAngles;
        }
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    protected override void OnDestroy()
    {
        Unsubscribe();
        _impactScaleTween?.Kill();
        _wobbleTween?.Kill();
        _aimTween?.Kill();
        base.OnDestroy();
    }

    protected override void CloseAction()
    {
    }

    protected override void Update()
    {
    }

    private void Subscribe()
    {
        if (_cardView == null)
            _cardView = GetComponentInParent<CardView>();

        if (_cardView == null || _subscribed)
            return;

        _cardView.DrawImpactRequested += OnDrawImpactRequested;
        _cardView.HoverImpactRequested += OnHoverImpactRequested;
        _cardView.HoverImpactStopped += OnHoverImpactStopped;
        _cardView.PressImpactRequested += OnPressImpactRequested;
        _cardView.AimImpactRequested += OnAimImpactRequested;
        _cardView.AimImpactStopped += OnAimImpactStopped;
        _cardView.ReleaseImpactRequested += OnReleaseImpactRequested;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_cardView == null || !_subscribed)
            return;

        _cardView.DrawImpactRequested -= OnDrawImpactRequested;
        _cardView.HoverImpactRequested -= OnHoverImpactRequested;
        _cardView.HoverImpactStopped -= OnHoverImpactStopped;
        _cardView.PressImpactRequested -= OnPressImpactRequested;
        _cardView.AimImpactRequested -= OnAimImpactRequested;
        _cardView.AimImpactStopped -= OnAimImpactStopped;
        _cardView.ReleaseImpactRequested -= OnReleaseImpactRequested;
        _subscribed = false;
    }

    private void ForceRaycastPassThrough()
    {
        if (TryGetComponent<CanvasGroup>(out var canvasGroup))
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    private void OnDrawImpactRequested(CardView view) => PlayDrawImpact();
    private void OnHoverImpactRequested(CardView view) => PlayHoverImpact();
    private void OnHoverImpactStopped(CardView view) => StopAimImpact();
    private void OnPressImpactRequested(CardView view) => PlayPressImpact();
    private void OnAimImpactRequested(CardView view) => PlayAimImpact();
    private void OnAimImpactStopped(CardView view) => StopAimImpact();
    private void OnReleaseImpactRequested(CardView view) => PlayReleaseImpact();

    public void PlayDrawImpact()
    {
        PulseScale(drawScale, drawBeatCount);
    }

    public void PlayHoverImpact()
    {
        PulseScale(hoverScale, hoverBeatCount);
        PlayYawWobble();
    }

    public void PlayPressImpact()
    {
        if (impactTarget == null)
            return;

        StopAimImpact();
        float duration = BeatSeconds(pressBeatCount);
        _wobbleTween?.Kill();
        _impactScaleTween?.Kill();
        _impactScaleTween = DOTween.Sequence()
            .Join(impactTarget.DOScale(_impactBaseScale * Mathf.Max(0.01f, pressScale), duration * 0.45f).SetEase(Ease.OutCubic))
            .Join(impactTarget.DOLocalRotate(_impactBaseEuler + new Vector3(0f, pressYaw, 0f), duration * 0.45f).SetEase(Ease.OutCubic))
            .Append(impactTarget.DOScale(_impactBaseScale, duration * 0.55f).SetEase(Ease.OutBack))
            .Join(impactTarget.DOLocalRotate(_impactBaseEuler, duration * 0.55f).SetEase(Ease.OutCubic));
    }

    public void PlayReleaseImpact()
    {
        if (impactTarget == null)
            return;

        StopAimImpact();
        float duration = BeatSeconds(releaseBeatCount);
        _wobbleTween?.Kill();
        _impactScaleTween?.Kill();
        _impactScaleTween = DOTween.Sequence()
            .Join(impactTarget.DOScale(_impactBaseScale * Mathf.Max(0.01f, releaseScale), duration * 0.55f).SetEase(Ease.InCubic))
            .Join(impactTarget.DOLocalRotate(_impactBaseEuler + new Vector3(0f, releaseYaw, 0f), duration * 0.55f).SetEase(Ease.InCubic))
            .Append(impactTarget.DOScale(_impactBaseScale, duration * 0.45f).SetEase(Ease.OutCubic))
            .Join(impactTarget.DOLocalRotate(_impactBaseEuler, duration * 0.45f).SetEase(Ease.OutCubic));
    }

    public void PlayAimImpact()
    {
        if (impactTarget == null)
            return;

        _aimTween?.Kill();
        _aimTween = impactTarget
            .DOScale(_impactBaseScale * Mathf.Max(0.01f, aimBreathScale), BeatSeconds(aimBreathBeatCount))
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    public void StopAimImpact()
    {
        _aimTween?.Kill();
        if (impactTarget != null)
        {
            float duration = BeatSeconds(0.35f);
            impactTarget.DOScale(_impactBaseScale, duration).SetEase(Ease.OutCubic);
            impactTarget.DOLocalRotate(_impactBaseEuler, duration).SetEase(Ease.OutCubic);
        }
    }

    private void PlayYawWobble()
    {
        if (impactTarget == null || Mathf.Approximately(hoverWobbleYaw, 0f))
            return;

        float duration = BeatSeconds(hoverBeatCount);
        _wobbleTween?.Kill();
        _wobbleTween = DOVirtual.Float(0f, 1f, duration, ApplyDoorWobble)
            .SetEase(Ease.Linear)
            .OnComplete(() => impactTarget.localEulerAngles = _impactBaseEuler);
    }

    private void ApplyDoorWobble(float normalizedTime)
    {
        if (impactTarget == null)
            return;

        float damping = Mathf.Max(0f, hoverDoorDamping);
        float oscillations = Mathf.Max(0.01f, hoverDoorOscillations);
        float envelope = Mathf.Exp(-damping * normalizedTime);
        float angle = hoverWobbleYaw * envelope * Mathf.Cos(Mathf.PI * 2f * oscillations * normalizedTime);
        impactTarget.localEulerAngles = _impactBaseEuler + new Vector3(0f, angle, 0f);
    }

    private void PulseScale(float scale, float beatCount)
    {
        if (impactTarget == null)
            return;

        float duration = BeatSeconds(beatCount);
        _impactScaleTween?.Kill();
        _impactScaleTween = DOTween.Sequence()
            .Append(impactTarget.DOScale(_impactBaseScale * Mathf.Max(0.01f, scale), duration * 0.45f).SetEase(Ease.OutCubic))
            .Append(impactTarget.DOScale(_impactBaseScale, duration * 0.55f).SetEase(Ease.OutCubic));
    }

    private static float BeatSeconds(float beatCount)
    {
        float beatDuration = BeatTiming.GetBeatDuration();
        return Mathf.Max(0.01f, beatDuration * Mathf.Max(0.01f, beatCount));
    }
}
