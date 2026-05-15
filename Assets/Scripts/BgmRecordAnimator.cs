using SpaceTUI;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class BgmRecordAnimator : SpaceUIAnimator
{
    protected override string UIID => BgmRecordUIIds.RecordButton;

    public static BgmRecordAnimator Instance { get; private set; }

    [Header("Record Motion")]
    [SerializeField] private float hoverScale = 1.08f;
    [SerializeField] private float idleBreathScale = 0.02f;
    [SerializeField] private float clickCooldown = 0.25f;

    [Header("Track Text")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bpmText;

    private float _nextClickAllowedAt;
    private bool _hiddenByFlow;

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        期望显示面板 += _ => ShowRecord();
        期望隐藏面板 += HideRecordIfExplicitlyRequested;
        鼠标滑入 += OnHoverEnter;
        鼠标滑出 += OnHoverExit;
        鼠标点击 += OnClick;
    }

    protected override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    private void Start()
    {
        ShowRecord();
    }

    private void LateUpdate()
    {
        RefreshTrackText();
    }

    protected override void Update()
    {
        base.Update();

        if (ShouldHideForFlow())
        {
            if (!_hiddenByFlow)
            {
                _hiddenByFlow = true;
                HideRecord();
            }

            return;
        }

        if (_hiddenByFlow)
        {
            _hiddenByFlow = false;
            ShowRecord();
        }
    }

    public void ShowRecord()
    {
        if (ShouldHideForFlow())
        {
            HideRecord();
            return;
        }

        _breathScaleAmplitude = idleBreathScale;
        FadeInRecord();
        StartBreathing();
        RefreshTrackText();
    }

    public void HideRecord()
    {
        StopBreathing();
        ResetScale();
        this.FadeOutIfVisible();
    }

    private void HideRecordIfExplicitlyRequested(object data)
    {
        if (data == null)
            return;

        HideRecord();
    }

    protected override void CloseAction()
    {
        HideRecord();
    }

    private void OnHoverEnter(PointerEventData eventData)
    {
        if (ShouldHideForFlow())
            return;

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
        if (!IsVisible || Time.unscaledTime < _nextClickAllowedAt || ShouldHideForFlow())
            return;

        _nextClickAllowedAt = Time.unscaledTime + clickCooldown;
        BgmRecordPlayer.Instance?.NextTrack();
        SetTargetScale(_initialScale * hoverScale);
        PlayScaleAnimation();
        RefreshTrackText();
    }

    private void RefreshTrackText()
    {
        var player = BgmRecordPlayer.Instance;
        var track = player != null ? player.CurrentTrack : null;

        if (titleText != null)
            titleText.text = track != null ? track.ResolvedTitle : "No BGM";

        if (bpmText != null)
            bpmText.text = track != null ? $"{track.ResolvedBpm:0.#} BPM" : "-- BPM";
    }

    private void FadeInRecord()
    {
        if (_canvasGroup == null)
        {
            this.FadeInIfHiddenPreserveRotation();
            return;
        }

        if (_canvasGroup.blocksRaycasts && _canvasGroup.alpha >= 0.999f)
            return;

        _stateTween?.Kill();
        _moveTween?.Kill();
        _canvasGroup.blocksRaycasts = false;
        FadeIn(null, true);
    }

    private static bool ShouldHideForFlow()
    {
        return (GameFlowController.Instance != null &&
                (GameFlowController.Instance.IsMainMenuVisible ||
                 GameFlowController.Instance.Mode == GameFlowMode.LevelEdit))
            || (RunSettingsPanelAnimator.Instance != null && RunSettingsPanelAnimator.Instance.IsSettingsVisible);
    }
}
