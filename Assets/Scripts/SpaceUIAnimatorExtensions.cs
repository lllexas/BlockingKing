using SpaceTUI;
using UnityEngine;

public static class SpaceUIAnimatorExtensions
{
    public static void FadeInIfHidden(this SpaceUIAnimator animator)
    {
        if (animator == null || IsRaycastVisible(animator))
            return;

        animator.FadeIn();
    }

    public static void FadeInIfHiddenPreserveRotation(this SpaceUIAnimator animator)
    {
        if (animator == null || IsRaycastVisible(animator))
            return;

        animator.FadeIn(null, true);
    }

    public static void FadeOutIfVisible(this SpaceUIAnimator animator)
    {
        if (animator == null || !IsRaycastVisible(animator))
            return;

        animator.FadeOut();
    }

    private static bool IsRaycastVisible(SpaceUIAnimator animator)
    {
        return animator.TryGetComponent<CanvasGroup>(out var canvasGroup) && canvasGroup.blocksRaycasts;
    }
}
