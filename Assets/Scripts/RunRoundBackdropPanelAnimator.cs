using SpaceTUI;
using UnityEngine;
using UnityEngine.UI;

public sealed class RunRoundBackdropPanelAnimator : SpaceUIAnimator
{
    protected override string UIID => RunRoundUIIds.Backdrop;

    [Header("Backdrop")]
    [SerializeField] private Color backdropColor = new(0f, 0f, 0f, 0.72f);

    protected override void Awake()
    {
        base.Awake();
        ConfigureImage();
        期望显示面板 += _ => this.FadeInIfHidden();
        期望隐藏面板 += _ => this.FadeOutIfVisible();
    }

    private void Reset()
    {
        ConfigureImage();
    }

    private void ConfigureImage()
    {
        var image = GetComponent<Image>();
        if (image == null)
            return;

        image.color = backdropColor;
        image.raycastTarget = false;
    }

    protected override void CloseAction()
    {
        this.FadeOutIfVisible();
    }
}
