using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10)]
public class RunSetupOnGUIFrontend : MonoBehaviour
{
    [SerializeField] private List<RunConfigSO> runConfigs = new List<RunConfigSO>();

    private int _selectedIndex = -1;
    private Vector2 _scroll;

    private GUIStyle _boxStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _cardStyle;
    private GUIStyle _cardTitleStyle;
    private GUIStyle _cardBodyStyle;
    private GUIStyle _cardStatStyle;
    private GUIStyle _selectedCardStyle;
    private GUIStyle _startButtonStyle;
    private GUIStyle _disabledButtonStyle;
    private GUIStyle _emptyTitleStyle;
    private GUIStyle _emptyBodyStyle;

    private void Awake()
    {
        if (runConfigs.Count == 0)
        {
            var loaded = Resources.LoadAll<RunConfigSO>("");
            if (loaded != null && loaded.Length > 0)
                runConfigs.AddRange(loaded);
        }

        if (runConfigs.Count > 0)
            _selectedIndex = 0;
    }

    private void OnGUI()
    {
        EnsureStyles();

        DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.08f, 0.08f, 0.12f, 1f));

        if (runConfigs.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        float titleHeight = 70f;
        float bottomBarHeight = 110f;
        float margin = 40f;

        // Title
        var titleRect = new Rect(margin, margin, Screen.width - margin * 2f, titleHeight);
        GUI.Label(titleRect, "选择 Run 配置", _titleStyle);

        // Config cards area
        float listTop = margin + titleHeight + 20f;
        float listHeight = Screen.height - listTop - bottomBarHeight - margin;
        var listRect = new Rect(margin, listTop, Screen.width - margin * 2f, listHeight);

        GUILayout.BeginArea(listRect);
        _scroll = GUILayout.BeginScrollView(_scroll);
        for (int i = 0; i < runConfigs.Count; i++)
        {
            DrawConfigCard(i, runConfigs[i], listRect.width);
            GUILayout.Space(12f);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        // Bottom bar with Start button
        float bottomY = Screen.height - bottomBarHeight - margin;
        var bottomRect = new Rect(margin, bottomY, Screen.width - margin * 2f, bottomBarHeight);
        GUILayout.BeginArea(bottomRect);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        bool hasSelection = _selectedIndex >= 0 && _selectedIndex < runConfigs.Count;
        GUI.enabled = hasSelection;
        if (GUILayout.Button("开始 Run", hasSelection ? _startButtonStyle : _disabledButtonStyle,
                GUILayout.Width(260f), GUILayout.Height(64f)))
        {
            StartSelectedRun();
        }
        GUI.enabled = true;

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawConfigCard(int index, RunConfigSO config, float listWidth)
    {
        bool isSelected = index == _selectedIndex;
        var style = isSelected ? _selectedCardStyle : _cardStyle;

        float cardWidth = Mathf.Min(420f, listWidth - 20f);
        float cardX = (listWidth - cardWidth) * 0.5f;

        GUILayout.BeginHorizontal();
        GUILayout.Space(cardX);
        GUILayout.BeginVertical(style, GUILayout.Width(cardWidth));
        {
            // Card header
            string name = string.IsNullOrWhiteSpace(config.displayName) ? config.name : config.displayName;
            GUILayout.Label(name, _cardTitleStyle);

            // Description
            string description = config.startSettings != null && !string.IsNullOrWhiteSpace(config.startSettings.description)
                ? config.startSettings.description
                : config.routeSettings != null ? $"Route: {config.routeSettings.name}" : "";
            if (!string.IsNullOrWhiteSpace(description))
                GUILayout.Label(description, _cardBodyStyle);

            GUILayout.Space(8f);

            // Stats row
            GUILayout.BeginHorizontal();
            DrawStat("层数", config.routeSettings != null
                ? config.routeSettings.GetResolvedLayerCount().ToString()
                : "-");
            DrawStat("通道数", config.routeSettings != null
                ? config.routeSettings.GetResolvedLaneCount().ToString()
                : "-");
            DrawStat("难度", config.difficultySettings != null
                ? config.difficultySettings.overallDifficulty.ToString("F1")
                : "-");
            DrawStat("初始金币", config.startSettings != null
                ? config.startSettings.startingGold.ToString()
                : "-");
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.Space(cardX);
        GUILayout.EndHorizontal();

        // Click detection on the card area
        var lastRect = GUILayoutUtility.GetLastRect();
        if (Event.current.type == EventType.MouseDown && lastRect.Contains(Event.current.mousePosition))
            _selectedIndex = index;
    }

    private static void DrawStat(string label, string value)
    {
        GUILayout.BeginVertical(GUILayout.Width(90f));
        GUILayout.Label(label, new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.6f, 0.6f, 0.65f) }
        });
        GUILayout.Label(value, new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        });
        GUILayout.EndVertical();
    }

    private void DrawEmptyState()
    {
        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;

        var titleRect = new Rect(centerX - 300f, centerY - 60f, 600f, 50f);
        GUI.Label(titleRect, "没有找到可用的 Run 配置", _emptyTitleStyle);

        var bodyRect = new Rect(centerX - 300f, centerY, 600f, 50f);
        GUI.Label(bodyRect, "请在 Resources 目录下创建 RunConfigSO 资产", _emptyBodyStyle);
    }

    private void StartSelectedRun()
    {
        if (_selectedIndex < 0 || _selectedIndex >= runConfigs.Count)
            return;

        var config = runConfigs[_selectedIndex];
        var controller = GameFlowController.Instance;
        if (controller == null)
        {
            Debug.LogError("[RunSetup] GameFlowController.Instance 为空，无法启动 Run。");
            return;
        }

        controller.StartRun(config, GameFlowMode.RouteMap);

        var setup = FindObjectOfType<RunSetupOnGUIFrontend>();
        if (setup != null)
            Destroy(setup);
    }

    private void EnsureStyles()
    {
        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };

        _cardStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(22, 22, 18, 18),
            normal = { background = MakeColorTex(new Color(0.18f, 0.18f, 0.24f)) }
        };

        _selectedCardStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(22, 22, 18, 18),
            normal = { background = MakeColorTex(new Color(0.22f, 0.28f, 0.42f)) }
        };

        _cardTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white },
            wordWrap = true
        };

        _cardBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = new Color(0.7f, 0.7f, 0.75f) },
            wordWrap = true
        };

        _startButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0)
        };

        _disabledButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0)
        };

        _emptyTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.6f, 0.6f, 0.65f) }
        };

        _emptyBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.45f, 0.45f, 0.5f) }
        };
    }

    private static Texture2D MakeColorTex(Color color)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private static void DrawSolidRect(Rect rect, Color color)
    {
        Color oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }
}
