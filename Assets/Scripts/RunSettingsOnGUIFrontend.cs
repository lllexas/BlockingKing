using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class RunSettingsOnGUIFrontend : MonoBehaviour
{
    public static RunSettingsOnGUIFrontend Instance { get; private set; }

    [SerializeField] private bool visible;
    [SerializeField] private int panelWidth = 260;
    [SerializeField] private int panelHeight = 210;
    [SerializeField] private int margin = 10;

    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _buttonStyle;

    public bool Visible => visible;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Toggle()
    {
        visible = !visible;
    }

    public void Show()
    {
        visible = true;
    }

    public void Hide()
    {
        visible = false;
    }

    private void OnGUI()
    {
        if (RunSettingsPanelAnimator.Instance != null)
            return;

        if (!visible)
            return;

        EnsureStyles();

        var rect = new Rect(
            Screen.width - panelWidth - margin,
            margin + 42,
            panelWidth,
            panelHeight);

        GUI.Box(rect, GUIContent.none, _panelStyle);
        GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20));

        GUILayout.Label("设置", _titleStyle);
        GUILayout.Space(8);

        DrawVolume("总音量", AudioBus.Ensure().MasterVolume, AudioBus.Ensure().SetMasterVolume);
        DrawVolume("音效", AudioBus.Ensure().SfxVolume, AudioBus.Ensure().SetSfxVolume);
        DrawVolume("BGM", AudioBus.Ensure().MusicVolume, AudioBus.Ensure().SetMusicVolume);

        GUILayout.Space(12);
        if (GUILayout.Button("重新开始", _buttonStyle, GUILayout.Height(30)))
            RestartGame();

        if (GUILayout.Button("退出程序", _buttonStyle, GUILayout.Height(30)))
            QuitGame();

        GUILayout.EndArea();
    }

    private void DrawVolume(string label, float value, System.Action<float> setter)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _labelStyle, GUILayout.Width(54));
        float next = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.Width(130));
        GUILayout.Label($"{Mathf.RoundToInt(next * 100f)}%", _labelStyle, GUILayout.Width(42));
        GUILayout.EndHorizontal();

        if (!Mathf.Approximately(next, value))
            setter?.Invoke(next);
    }

    private static void RestartGame()
    {
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid())
            SceneManager.LoadScene(activeScene.buildIndex);
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void EnsureStyles()
    {
        _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 10, 10)
        };

        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        _labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            normal = { textColor = Color.white }
        };

        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13
        };
    }
}
