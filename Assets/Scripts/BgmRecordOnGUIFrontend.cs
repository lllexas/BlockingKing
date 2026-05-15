using UnityEngine;

public sealed class BgmRecordOnGUIFrontend : MonoBehaviour
{
    private const string SettingsIconResourcePath = "SimpleUI/UI Elements/White/1x/settings";

    [SerializeField] private bool visible = true;
    [SerializeField] private int buttonSize = 40;
    [SerializeField] private int margin = 10;
    [SerializeField] private int gap = 6;

    private Texture2D _settingsIcon;
    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;

    private void OnGUI()
    {
        if (!visible)
            return;

        if (GameFlowController.Instance != null && GameFlowController.Instance.IsMainMenuVisible)
            return;

        if (GameFlowController.Instance != null && GameFlowController.Instance.Mode == GameFlowMode.LevelEdit)
            return;

        if (RunSettingsPanelAnimator.Instance != null && RunSettingsPanelAnimator.Instance.IsSettingsVisible)
            return;

        EnsureStyles();

        int settingsX = Screen.width - margin - buttonSize;
        bool reserveLinearChapterButton = GameFlowController.Instance != null &&
                                          GameFlowController.Instance.Mode == GameFlowMode.LinearCampaign;
        int recordX = settingsX - gap - buttonSize;
        if (reserveLinearChapterButton)
            recordX -= gap + buttonSize;

        int y = margin;
        bool hasFormalRecordButton = BgmRecordAnimator.Instance != null;

        var settingsRect = new Rect(settingsX, y, buttonSize, buttonSize);
        if (GUI.Button(settingsRect, GUIContent.none, _buttonStyle))
        {
            if (RunSettingsPanelAnimator.Instance != null)
                RunSettingsPanelAnimator.Instance.Toggle();
            else
                RunSettingsOnGUIFrontend.Instance?.Toggle();
        }

        DrawSettingsIcon(settingsRect);

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
        EnsureIcons();

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

    private void EnsureIcons()
    {
        if (_settingsIcon != null)
            return;

        _settingsIcon = Resources.Load<Texture2D>(SettingsIconResourcePath);
    }

    private void DrawSettingsIcon(Rect buttonRect)
    {
        if (_settingsIcon == null)
        {
            GUI.Label(buttonRect, "≡", _buttonStyle);
            return;
        }

        float iconSize = Mathf.Min(buttonRect.width, buttonRect.height) - 14f;
        var iconRect = new Rect(
            buttonRect.x + (buttonRect.width - iconSize) * 0.5f,
            buttonRect.y + (buttonRect.height - iconSize) * 0.5f,
            iconSize,
            iconSize);
        GUI.DrawTexture(iconRect, _settingsIcon, ScaleMode.ScaleToFit, true);
    }
}
