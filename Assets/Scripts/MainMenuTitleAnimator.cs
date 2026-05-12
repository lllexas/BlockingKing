using TMPro;
using UnityEngine;

public sealed class MainMenuTitleAnimator : MainMenuAnimatorBase
{
    protected override string UIID => MainMenuUIIds.Title;

    [Header("Title")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;

    protected override void Refresh(MainMenuUIRequest request)
    {
        if (titleText != null)
            titleText.text = "Blocking King";

        if (subtitleText != null)
        {
            string runName = request.RunConfig != null && !string.IsNullOrWhiteSpace(request.RunConfig.displayName)
                ? request.RunConfig.displayName
                : request.RunConfig != null ? request.RunConfig.name : "Round Flow";
            subtitleText.text = runName;
        }
    }
}
