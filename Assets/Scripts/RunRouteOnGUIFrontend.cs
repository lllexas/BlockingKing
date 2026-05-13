using NekoGraph;
using UnityEngine;

public class RunRouteOnGUIFrontend : MonoBehaviour
{
    [SerializeField] private bool visible = true;
    [SerializeField] private Vector2 nodeSize = new Vector2(170f, 64f);
    [SerializeField] private float layerSpacing = 240f;
    [SerializeField] private float laneSpacing = 180f;
    [SerializeField] private Vector2 contentPadding = new Vector2(140f, 86f);
    [SerializeField] private Color backgroundColor = new Color(0.06f, 0.045f, 0.028f, 0.58f);
    [SerializeField] private Color scrollColor = new Color(0.78f, 0.62f, 0.38f, 0.82f);
    [SerializeField] private Color scrollEdgeColor = new Color(0.31f, 0.18f, 0.08f, 0.94f);
    [SerializeField] private float maxPanelHeight = 900f;
    [SerializeField] private float horizontalMargin = 30f;

    private Vector2 _scroll;
    private Vector2 _deckScroll;
    private GUIStyle _nodeStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _hintStyle;
    private GUIStyle _deckTitleStyle;
    private GUIStyle _deckBodyStyle;
    private GUIStyle _deckItemTitleStyle;
    private GUIStyle _deckItemBodyStyle;
    private GUIStyle _deckEmptyStyle;
    private int _laneMin;
    private int _laneMax;
    private bool _showingDeck;

    public bool Visible
    {
        get => visible;
        set => visible = value;
    }

    public void OpenDeck()
    {
        if (RunDeckPanelAnimator.Instance != null)
        {
            PostSystem.Instance?.Send("期望显示面板", new RunDeckPanelUIRequest());
            return;
        }

        _showingDeck = true;
    }

    public void CloseDeck()
    {
        if (RunDeckPanelAnimator.Instance != null)
            PostSystem.Instance?.Send("期望隐藏面板", new RunDeckPanelUIRequest());

        _showingDeck = false;
    }

    private void OnGUI()
    {
        if (!visible)
            return;

        var flow = GameFlowController.Instance;
        if (flow != null && flow.IsInLevel)
        {
            CloseDeck();
            return;
        }

        if (flow != null && flow.Mode == GameFlowMode.RoundFlow)
        {
            int oldRoundDepth = GUI.depth;
            GUI.depth = 20;
            EnsureStyles();
            if (_showingDeck)
                DrawDeckFullscreen();
            else
                DrawDeckButton();
            GUI.depth = oldRoundDepth;
            return;
        }

        if (flow != null && flow.Mode != GameFlowMode.RouteMap)
        {
            CloseDeck();
            return;
        }

        int oldDepth = GUI.depth;
        GUI.depth = 20;

        var facade = GraphHub.Instance?.GetFacade<RunRouteFacade>();
        if (facade == null)
        {
            facade = new RunRouteFacade();
            GraphHub.Instance?.RegisterFacade(facade);
        }

        if (facade == null)
        {
            GUI.depth = oldDepth;
            return;
        }

        var view = facade.GetRouteView();
        CacheLaneRange(view);

        if (_showingDeck)
            DrawDeckFullscreen();
        else
            DrawRouteFullscreen(facade, view);

        GUI.depth = oldDepth;
    }

    private void DrawRouteFullscreen(RunRouteFacade facade, RunRouteView view)
    {
        EnsureStyles();

        var screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
        DrawSolidRect(screenRect, backgroundColor);

        float panelHeight = Mathf.Min(maxPanelHeight, Screen.height * 0.84f);
        float panelTop = Mathf.Max(24f, (Screen.height - panelHeight) * 0.5f);
        var panelRect = new Rect(
            horizontalMargin,
            panelTop,
            Mathf.Max(1f, Screen.width - horizontalMargin * 2f),
            Mathf.Max(1f, panelHeight));

        DrawScrollPanel(panelRect);

        GUI.Label(new Rect(panelRect.x, panelRect.y + 12f, panelRect.width, 34f), "Run Route", _titleStyle);
        GUI.Label(new Rect(panelRect.x, panelRect.yMax - 34f, panelRect.width, 22f), "选择发亮的节点继续旅途", _hintStyle);

        var scrollRect = new Rect(panelRect.x + 18f, panelRect.y + 58f, panelRect.width - 36f, panelRect.height - 104f);
        var contentRect = BuildContentRect(view, scrollRect);

        float maxScrollX = Mathf.Max(0f, contentRect.width - scrollRect.width);
        _scroll.x = Mathf.Clamp(_scroll.x, 0f, maxScrollX);
        _scroll.y = 0f;

        _scroll = GUI.BeginScrollView(
            scrollRect,
            _scroll,
            contentRect,
            alwaysShowHorizontal: contentRect.width > scrollRect.width,
            alwaysShowVertical: false);
        _scroll.y = 0f;

        foreach (var edge in view.edges)
        {
            var from = view.nodes.Find(node => node.nodeId == edge.fromNodeId);
            var to = view.nodes.Find(node => node.nodeId == edge.toNodeId);
            if (from == null || to == null)
                continue;

            DrawLine(GetNodeRightAnchor(from), GetNodeLeftAnchor(to), new Color(0.65f, 0.65f, 0.65f, 0.9f), 3f);
        }

        foreach (var node in view.nodes)
        {
            DrawNode(facade, node);
        }

        GUI.EndScrollView();

        DrawDeckButton();
    }

    private void DrawDeckFullscreen()
    {
        EnsureStyles();

        DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0.75f));

        float width = Mathf.Clamp(Screen.width * 0.78f, 920f, 1480f);
        float height = Mathf.Clamp(Screen.height * 0.78f, 700f, 900f);
        float left = (Screen.width - width) * 0.5f;
        float top = (Screen.height - height) * 0.5f;
        var panelRect = new Rect(left, top, width, height);

        if (Event.current.type == EventType.MouseDown && !panelRect.Contains(Event.current.mousePosition))
        {
            CloseDeck();
            Event.current.Use();
            return;
        }

        DrawScrollPanel(panelRect);

        GUILayout.BeginArea(panelRect, GetDeckBoxStyle());
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("牌库", _deckTitleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("X", GUILayout.Width(44f), GUILayout.Height(36f)))
        {
            CloseDeck();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            return;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);
        GUILayout.Label("点击空白处或右上角关闭，当前只做列表展示", _deckBodyStyle);
        GUILayout.Space(14f);

        var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck == null)
        {
            deck = new CardDeckFacade();
            GraphHub.Instance?.RegisterFacade(deck);
        }

        var cards = deck?.GetCards();
        _deckScroll = GUILayout.BeginScrollView(_deckScroll);

        if (cards == null || cards.Count == 0)
        {
            GUILayout.Label("牌库为空。", _deckEmptyStyle);
        }
        else
        {
            foreach (var card in cards)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(BuildCardHeader(card), _deckItemTitleStyle);
                GUILayout.Label(BuildCardBody(card), _deckItemBodyStyle);
                GUILayout.EndVertical();
                GUILayout.Space(8f);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private Rect BuildContentRect(RunRouteView view, Rect scrollRect)
    {
        float maxLayer = 0f;
        foreach (var node in view.nodes)
            maxLayer = Mathf.Max(maxLayer, node.side.layer);

        float laneBandHeight = Mathf.Max(nodeSize.y, (_laneMax - _laneMin) * laneSpacing + nodeSize.y);

        return new Rect(
            0f,
            0f,
            Mathf.Max(scrollRect.width, contentPadding.x * 2f + maxLayer * layerSpacing + nodeSize.x),
            Mathf.Max(scrollRect.height, laneBandHeight + contentPadding.y * 2f));
    }

    private void DrawNode(RunRouteFacade facade, RunRouteNodeView node)
    {
        var rect = GetNodeRect(node);
        Color oldColor = GUI.color;
        GUI.color = GetNodeColor(node.side.state);

        bool enabled = GUI.enabled;
        GUI.enabled = node.side.state == RunRouteNodeState.Available && !facade.IsRouteNodeRunning;

        string label = string.IsNullOrWhiteSpace(node.side.stageId) ? node.name : node.side.stageId;
        if (GUI.Button(rect, label, _nodeStyle))
        {
            facade.TryStartRouteNode(node.nodeId);
        }

        GUI.enabled = enabled;
        GUI.color = oldColor;
    }

    private Rect GetNodeRect(RunRouteNodeView node)
    {
        return new Rect(
            contentPadding.x + node.side.layer * layerSpacing,
            GetLaneY(node.side.lane),
            nodeSize.x,
            nodeSize.y);
    }

    private float GetLaneY(int lane)
    {
        float panelHeight = Mathf.Min(maxPanelHeight, Screen.height * 0.84f);
        float scrollHeight = Mathf.Max(1f, panelHeight - 104f);
        float centerY = scrollHeight * 0.5f;
        float centerLane = (_laneMin + _laneMax) * 0.5f;
        float nodeCenterY = centerY + (lane - centerLane) * laneSpacing;
        return nodeCenterY - nodeSize.y * 0.5f;
    }

    private void CacheLaneRange(RunRouteView view)
    {
        if (view == null || view.nodes.Count == 0)
        {
            _laneMin = 0;
            _laneMax = 0;
            return;
        }

        int minLane = int.MaxValue;
        int maxLane = int.MinValue;
        foreach (var node in view.nodes)
        {
            minLane = Mathf.Min(minLane, node.side.lane);
            maxLane = Mathf.Max(maxLane, node.side.lane);
        }

        _laneMin = minLane == int.MaxValue ? 0 : minLane;
        _laneMax = maxLane == int.MinValue ? 0 : maxLane;
    }

    private Vector2 GetNodeLeftAnchor(RunRouteNodeView node)
    {
        var rect = GetNodeRect(node);
        return new Vector2(rect.x, rect.y + rect.height * 0.5f);
    }

    private Vector2 GetNodeRightAnchor(RunRouteNodeView node)
    {
        var rect = GetNodeRect(node);
        return new Vector2(rect.xMax, rect.y + rect.height * 0.5f);
    }

    private static Color GetNodeColor(RunRouteNodeState state)
    {
        return state switch
        {
            RunRouteNodeState.Available => new Color(0.78f, 0.9f, 1f, 1f),
            RunRouteNodeState.Completed => new Color(0.55f, 0.95f, 0.65f, 1f),
            _ => new Color(0.45f, 0.45f, 0.45f, 1f)
        };
    }

    private static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
    {
        Color oldColor = GUI.color;
        Matrix4x4 oldMatrix = GUI.matrix;

        Vector2 delta = end - start;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        float length = delta.magnitude;

        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, start);
        GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, length, width), Texture2D.whiteTexture);

        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    private void EnsureStyles()
    {
        _nodeStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.18f, 0.1f, 0.04f, 1f) }
        };

        _hintStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.23f, 0.13f, 0.06f, 0.85f) }
        };

        _deckTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            normal = { textColor = new Color(0.2f, 0.1f, 0.03f, 1f) }
        };

        _deckBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            normal = { textColor = new Color(0.22f, 0.12f, 0.05f, 0.95f) }
        };

        _deckItemTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            normal = { textColor = new Color(0.18f, 0.1f, 0.04f, 1f) }
        };

        _deckItemBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            normal = { textColor = new Color(0.22f, 0.12f, 0.05f, 0.95f) }
        };

        _deckEmptyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = new Color(0.22f, 0.12f, 0.05f, 0.95f) }
        };
    }

    private GUIStyle _deckBoxStyle;

    private GUIStyle GetDeckBoxStyle()
    {
        _deckBoxStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(24, 24, 18, 18)
        };

        return _deckBoxStyle;
    }

    private void DrawDeckButton()
    {
        var buttonRect = new Rect(Screen.width - 168f, Screen.height - 74f, 138f, 42f);
        if (GUI.Button(buttonRect, "牌库", _nodeStyle))
            OpenDeck();
    }

    private static string BuildCardHeader(CardSO card)
    {
        if (card == null)
            return "Unknown Card";

        string name = string.IsNullOrWhiteSpace(card.displayName) ? card.name : card.displayName;
        if (string.IsNullOrWhiteSpace(name))
            name = "Unnamed Card";

        return $"{name}  Cost:{card.cost}";
    }

    private static string BuildCardBody(CardSO card)
    {
        if (card == null)
            return string.Empty;

        string id = string.IsNullOrWhiteSpace(card.cardId) ? "-" : card.cardId;
        string desc = string.IsNullOrWhiteSpace(card.description) ? "No description." : card.description;
        return $"ID: {id}\n{desc}";
    }

    private void DrawScrollPanel(Rect panelRect)
    {
        DrawSolidRect(panelRect, scrollColor);

        const float rodHeight = 14f;
        var topRod = new Rect(panelRect.x - 12f, panelRect.y - rodHeight * 0.5f, panelRect.width + 24f, rodHeight);
        var bottomRod = new Rect(panelRect.x - 12f, panelRect.yMax - rodHeight * 0.5f, panelRect.width + 24f, rodHeight);
        DrawSolidRect(topRod, scrollEdgeColor);
        DrawSolidRect(bottomRod, scrollEdgeColor);

        DrawSolidRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 2f), new Color(0.95f, 0.82f, 0.55f, 0.55f));
        DrawSolidRect(new Rect(panelRect.x, panelRect.yMax - 2f, panelRect.width, 2f), new Color(0.18f, 0.09f, 0.03f, 0.38f));
    }

    private static void DrawSolidRect(Rect rect, Color color)
    {
        Color oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }
}
