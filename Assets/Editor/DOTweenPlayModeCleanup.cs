using DG.Tweening;
using UnityEditor;

[InitializeOnLoad]
public static class DOTweenPlayModeCleanup
{
    static DOTweenPlayModeCleanup()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode)
            return;

        DOTween.KillAll(false);
        DOTween.Clear(true);
    }
}
