using SpaceTUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class HandPilePanelAnimatorBase : SpaceUIAnimator
{
    [Header("Pile UI")]
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image pileImage;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color emptyColor = new(1f, 1f, 1f, 0.35f);

    private int _lastCount = -1;
    private bool _shown;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => HidePile();
    }

    private void OnShowPanel(object data)
    {
        if (data is not HandPileUIRequest request)
            return;

        Refresh(request.Count);
        ShowPile();
    }

    protected override void CloseAction()
    {
        HidePile();
    }

    private void ShowPile()
    {
        if (_shown)
            return;

        _shown = true;
        if (_canvasGroup != null && _canvasGroup.alpha <= 0.001f)
            _canvasGroup.blocksRaycasts = false;

        FadeIn();
    }

    private void HidePile()
    {
        if (!_shown)
            return;

        _shown = false;
        FadeOut();
    }

    private void Refresh(int count)
    {
        if (countText != null)
            countText.text = Mathf.Max(0, count).ToString();

        if (pileImage != null)
            pileImage.color = count > 0 ? normalColor : emptyColor;

        if (_lastCount >= 0 && count != _lastCount)
            PlayScaleAnimation();

        _lastCount = count;
    }
}
