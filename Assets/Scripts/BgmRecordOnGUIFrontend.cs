using UnityEngine;

public sealed class BgmRecordOnGUIFrontend : MonoBehaviour
{
    [SerializeField] private bool visible = true;
    [SerializeField] private int buttonSize = 34;
    [SerializeField] private int margin = 10;
    [SerializeField] private int gap = 6;

    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;

    private void OnGUI()
    {
        if (!visible)
            return;

        if (GameFlowController.Instance != null && GameFlowController.Instance.IsMainMenuVisible)
            return;

        if (RunSettingsPanelAnimator.Instance != null && RunSettingsPanelAnimator.Instance.IsSettingsVisible)
            return;

        EnsureStyles();

        int settingsX = Screen.width - margin - buttonSize;
        int recordX = settingsX - gap - buttonSize;
        int y = margin;
        bool hasFormalRecordButton = BgmRecordAnimator.Instance != null;

        if (GUI.Button(new Rect(settingsX, y, buttonSize, buttonSize), "⚙", _buttonStyle))
        {
            if (RunSettingsPanelAnimator.Instance != null)
                RunSettingsPanelAnimator.Instance.Toggle();
            else
                RunSettingsOnGUIFrontend.Instance?.Toggle();
        }

        if (hasFormalRecordButton)
            return;

        if (GUI.Button(new Rect(recordX, y, buttonSize, buttonSize), "◉", _buttonStyle))
            BgmRecordPlayer.Instance?.NextTrack();

        DrawInfo(recordX, y + buttonSize + gap);
    }

    private void DrawInfo(int rightAnchorX, int y)
    {
        var player = BgmRecordPlayer.Instance;
        var track = player != null ? player.CurrentTrack : null;
        string title = track != null ? track.ResolvedTitle : "No BGM";
        string bpm = track != null ? $"{track.ResolvedBpm:0.#} BPM" : "-- BPM";

        const int width = 220;
        var rect = new Rect(Mathf.Max(margin, rightAnchorX + buttonSize - width), y, width, 46);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 8, rect.y + 5, rect.width - 16, 20), title, _labelStyle);
        GUI.Label(new Rect(rect.x + 8, rect.y + 24, rect.width - 16, 18), bpm, _labelStyle);
    }

    private void EnsureStyles()
    {
        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 20,
            padding = new RectOffset(0, 0, 0, 2)
        };

        _labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            normal = { textColor = Color.white }
        };
    }
}
