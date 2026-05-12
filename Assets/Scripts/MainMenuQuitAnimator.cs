using UnityEngine;

public sealed class MainMenuQuitAnimator : MainMenuButtonAnimatorBase
{
    protected override string UIID => MainMenuUIIds.Quit;

    protected override void Invoke()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
