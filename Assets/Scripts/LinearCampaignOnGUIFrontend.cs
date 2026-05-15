using UnityEngine;

public sealed class LinearCampaignOnGUIFrontend : MonoBehaviour
{
    private const string ChapterIconResourcePath = "SimpleUI/UI Elements/White/2x/menu1";

    public static LinearCampaignOnGUIFrontend Instance { get; private set; }

    [SerializeField] private bool visible;
    [SerializeField] private int buttonSize = 40;
    [SerializeField] private int margin = 10;
    [SerializeField] private int gap = 16;
    [SerializeField] private int panelWidth = 1600;
    [SerializeField] private int panelHeight = 900;
    [SerializeField] private int levelColumns = 5;
    [SerializeField] private int levelButtonHeight = 140;

    private int _selectedChapterIndex;
    private Vector2 _levelScroll;
    private GUIStyle _buttonStyle;
    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _chapterButtonStyle;
    private GUIStyle _currentChapterButtonStyle;
    private GUIStyle _levelButtonStyle;
    private GUIStyle _currentLevelButtonStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _mutedLabelStyle;
    private Texture2D _chapterIcon;

    public bool Visible => visible;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (visible)
            HideBorrowedBackdrop();
    }

    public void Toggle()
    {
        if (visible)
            Hide();
        else
            Show();
    }

    public void Show()
    {
        visible = true;
        SyncSelectedChapterToCurrentLevel();
        ShowBorrowedBackdrop();
    }

    public void Hide()
    {
        visible = false;
        HideBorrowedBackdrop();
    }

    private void OnGUI()
    {
        var flow = GameFlowController.Instance;
        if (flow == null || flow.Mode != GameFlowMode.LinearCampaign || flow.IsMainMenuVisible)
            return;

        var director = FindObjectOfType<LinearModeDirector>();
        var config = director != null ? director.Config : flow.LinearCampaignConfig;
        if (config == null)
            return;

        EnsureStyles();
        DrawTopButton();

        if (!visible)
            return;

        DrawPanel(director, config);
    }

    private void DrawTopButton()
    {
        int settingsX = Screen.width - margin - buttonSize;
        int chapterX = settingsX - gap - buttonSize;
        var rect = new Rect(chapterX, margin, buttonSize, buttonSize);
        if (GUI.Button(rect, GUIContent.none, _buttonStyle))
            Toggle();

        DrawChapterIcon(rect);
    }

    private void DrawPanel(LinearModeDirector director, LinearCampaignConfigSO config)
    {
        int width = Mathf.Min(panelWidth, Screen.width - margin * 2);
        int height = Mathf.Min(panelHeight, Screen.height - margin * 2);
        var rect = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        GUI.Box(rect, GUIContent.none, _panelStyle);
        GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 22, rect.width - 48, rect.height - 44));

        GUILayout.BeginHorizontal();
        GUILayout.Label(ResolveCampaignTitle(config), _titleStyle);
        if (GUILayout.Button("X", _buttonStyle, GUILayout.Width(48), GUILayout.Height(42)))
        {
            Hide();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            return;
        }
        GUILayout.EndHorizontal();

        DrawChapterBar(director, config);
        GUILayout.Space(18);
        DrawLevelList(director, config);

        GUILayout.EndArea();
    }

    private void DrawChapterBar(LinearModeDirector director, LinearCampaignConfigSO config)
    {
        int chapterCount = config.ChapterCount;
        if (chapterCount <= 0)
        {
            GUILayout.Label("No chapters.", _mutedLabelStyle);
            return;
        }

        _selectedChapterIndex = Mathf.Clamp(_selectedChapterIndex, 0, chapterCount - 1);

        GUILayout.BeginHorizontal();
        for (int i = 0; i < chapterCount; i++)
        {
            if (!config.TryGetChapter(i, out var chapter))
                continue;

            bool isCurrent = director != null && director.CurrentChapterIndex == i;
            var style = i == _selectedChapterIndex || isCurrent ? _currentChapterButtonStyle : _chapterButtonStyle;
            string label = ResolveChapterTitle(chapter, i);
            if (GUILayout.Button(label, style, GUILayout.Height(48)))
            {
                _selectedChapterIndex = i;
                _levelScroll = Vector2.zero;
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawLevelList(LinearModeDirector director, LinearCampaignConfigSO config)
    {
        if (!config.TryGetChapter(_selectedChapterIndex, out var chapter))
        {
            GUILayout.Label("Chapter missing.", _mutedLabelStyle);
            return;
        }

        string chapterId = config.ResolveChapterId(_selectedChapterIndex, chapter);
        var progress = MainModel.User.linearCampaign;

        _levelScroll = GUILayout.BeginScrollView(_levelScroll);
        int levelCount = chapter.LevelCount;
        int columns = Mathf.Max(1, levelColumns);
        float contentWidth = Mathf.Min(panelWidth, Screen.width - margin * 2) - 48f;
        float levelButtonWidth = Mathf.Max(120f, (contentWidth - gap * (columns - 1)) / columns);
        int rowCount = Mathf.CeilToInt(levelCount / (float)columns);

        for (int row = 0; row < rowCount; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < columns; column++)
            {
                int i = row * columns + column;
                if (i >= levelCount)
                {
                    GUILayout.Space(levelButtonWidth);
                    continue;
                }

                LinearLevelEntry entry = chapter.levels[i];
                bool valid = entry != null && entry.levelData != null;
                string levelId = chapter.ResolveLevelId(entry, i);
                bool playable = LinearLevelValidation.CanPlay(entry, out string invalidReason);
                bool isCurrent = director != null &&
                                 director.CurrentChapterIndex == _selectedChapterIndex &&
                                 director.CurrentLevelIndex == i;
                bool isCleared = progress.IsLevelCleared(chapterId, levelId);

                GUI.enabled = valid && playable && director != null;
                var style = isCurrent ? _currentLevelButtonStyle : _levelButtonStyle;
                string label = BuildLevelButtonLabel(entry, i, isCurrent, isCleared, playable, invalidReason);
                if (GUILayout.Button(label, style, GUILayout.Width(levelButtonWidth), GUILayout.Height(levelButtonHeight)))
                {
                    if (director.TryJumpToLevel(_selectedChapterIndex, i))
                        Hide();
                }
                GUI.enabled = true;

                if (column < columns - 1)
                    GUILayout.Space(gap);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(gap);
        }

        if (levelCount <= 0)
            GUILayout.Label("No levels.", _mutedLabelStyle);

        GUILayout.EndScrollView();
    }

    private void SyncSelectedChapterToCurrentLevel()
    {
        var director = FindObjectOfType<LinearModeDirector>();
        if (director != null)
            _selectedChapterIndex = Mathf.Max(0, director.CurrentChapterIndex);
    }

    private static string ResolveCampaignTitle(LinearCampaignConfigSO config)
    {
        if (config == null)
            return "Linear Campaign";

        return !string.IsNullOrWhiteSpace(config.displayName)
            ? config.displayName
            : config.ResolvedCampaignId;
    }

    private static string ResolveChapterTitle(LinearChapterSO chapter, int index)
    {
        if (chapter != null && !string.IsNullOrWhiteSpace(chapter.displayName))
            return chapter.displayName;

        return $"Chapter {index + 1}";
    }

    private static string ResolveLevelTitle(LinearLevelEntry entry, int index)
    {
        if (entry != null && !string.IsNullOrWhiteSpace(entry.displayName))
            return entry.displayName;

        if (entry != null && entry.levelData != null && !string.IsNullOrWhiteSpace(entry.levelData.levelName))
            return entry.levelData.levelName;

        if (entry != null && entry.levelData != null)
            return entry.levelData.name;

        if (entry != null && !string.IsNullOrWhiteSpace(entry.levelId))
            return entry.levelId;

        return $"Level {index + 1}";
    }

    private static string ResolveLevelMode(LinearLevelEntry entry)
    {
        if (entry == null)
            return "--";

        return entry.subMode switch
        {
            LinearLevelSubMode.StepLimit => $"Steps {Mathf.Max(1, entry.stepLimit)}",
            LinearLevelSubMode.Escort => "Escort",
            _ => "Classic"
        };
    }

    private static string BuildLevelMarker(bool isCurrent, bool isCleared)
    {
        if (isCurrent)
            return ">";

        return isCleared ? "OK" : "";
    }

    private static string BuildLevelButtonLabel(
        LinearLevelEntry entry,
        int index,
        bool isCurrent,
        bool isCleared,
        bool playable,
        string invalidReason)
    {
        string title = ResolveLevelTitle(entry, index);
        string marker = BuildLevelMarker(isCurrent, isCleared);

        if (!playable)
            return $"{index + 1:00}\n{title}\n{invalidReason}";

        if (!string.IsNullOrEmpty(marker))
            return $"{marker}  {index + 1:00}\n{title}\n{ResolveLevelMode(entry)}";

        return $"{index + 1:00}\n{title}\n{ResolveLevelMode(entry)}";
    }

    private void EnsureStyles()
    {
        EnsureIcons();

        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18
        };

        _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(18, 18, 18, 18)
        };

        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 38,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        _chapterButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24
        };

        _currentChapterButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24,
            fontStyle = FontStyle.Bold
        };

        _levelButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            wordWrap = true,
            padding = new RectOffset(8, 8, 8, 8)
        };

        _currentLevelButtonStyle ??= new GUIStyle(_levelButtonStyle)
        {
            fontStyle = FontStyle.Bold
        };

        _labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            normal = { textColor = Color.white }
        };

        _mutedLabelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14,
            normal = { textColor = Color.gray }
        };
    }

    private void EnsureIcons()
    {
        if (_chapterIcon != null)
            return;

        _chapterIcon = Resources.Load<Texture2D>(ChapterIconResourcePath);
    }

    private void DrawChapterIcon(Rect buttonRect)
    {
        if (_chapterIcon == null)
        {
            GUI.Label(buttonRect, "章", _buttonStyle);
            return;
        }

        float iconSize = Mathf.Min(buttonRect.width, buttonRect.height) - 12f;
        var iconRect = new Rect(
            buttonRect.x + (buttonRect.width - iconSize) * 0.5f,
            buttonRect.y + (buttonRect.height - iconSize) * 0.5f,
            iconSize,
            iconSize);
        GUI.DrawTexture(iconRect, _chapterIcon, ScaleMode.ScaleToFit, true);
    }

    private static void ShowBorrowedBackdrop()
    {
        PostSystem.Instance?.Send("期望显示面板", new RunRoundBackdropUIRequest());
    }

    private static void HideBorrowedBackdrop()
    {
        PostSystem.Instance?.Send("期望隐藏面板", RunRoundUIIds.Backdrop);
    }
}
