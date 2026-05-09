using NekoGraph;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class RunStageOnGUIFrontend : MonoBehaviour
{
    private RunMsgPayload _currentMsg;
    private Vector2 _scroll;
    private bool _subscribed;
    private GUIStyle _titleStyle;
    private GUIStyle _speakerStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _boxStyle;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (PostSystem.Instance != null)
            PostSystem.Instance.Off(RunMsgResource.ExecuteEventName, OnRunMsgExecute);

        _subscribed = false;
    }

    private void Update()
    {
        TrySubscribe();

        if (_currentMsg == null && RunMsgResource.TryConsumePendingPayload(out var payload))
            ShowMessage(payload);
    }

    private void TrySubscribe()
    {
        if (_subscribed || PostSystem.Instance == null)
            return;

        PostSystem.Instance.On(RunMsgResource.ExecuteEventName, OnRunMsgExecute);
        _subscribed = true;
    }

    private void OnRunMsgExecute(object payload)
    {
        ShowMessage(payload as RunMsgPayload);
    }

    private void ShowMessage(RunMsgPayload payload)
    {
        if (payload == null)
            return;

        _currentMsg = payload;
        _scroll = Vector2.zero;
    }

    private void OnGUI()
    {
        var payload = _currentMsg;
        var message = payload?.Message;
        if (message == null)
            return;

        int oldDepth = GUI.depth;
        GUI.depth = -20;
        EnsureStyles();

        float width = Mathf.Clamp(Screen.width * 0.67f, 860f, 1320f);
        float height = Mathf.Clamp(Screen.height * 0.7f, 620f, 820f);
        var rect = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.5f));

        GUILayout.BeginArea(rect, _boxStyle);
        _scroll = GUILayout.BeginScrollView(_scroll);

        if (!string.IsNullOrWhiteSpace(message.title))
            GUILayout.Label(message.title, _titleStyle);

        if (!string.IsNullOrWhiteSpace(message.speaker))
            GUILayout.Label(message.speaker, _speakerStyle);

        GUILayout.Space(18f);
        GUILayout.Label(message.body ?? string.Empty, _bodyStyle);
        GUILayout.Space(24f);

        int choiceCount = message.choices?.Count ?? 0;
        int targetCount = payload.Targets?.Count ?? 0;
        int count = Mathf.Min(choiceCount, targetCount);

        for (int i = 0; i < count; i++)
        {
            string text = message.choices[i]?.text;
            if (string.IsNullOrWhiteSpace(text))
                text = $"Option {i + 1}";

            if (GUILayout.Button(text, _buttonStyle, GUILayout.Height(64f)))
            {
                SelectChoice(i);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                GUI.depth = oldDepth;
                return;
            }
        }

        if (choiceCount != targetCount)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"choices={choiceCount}, targets={targetCount}", _bodyStyle);
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.depth = oldDepth;
    }

    private void SelectChoice(int index)
    {
        if (_currentMsg == null ||
            _currentMsg.Targets == null ||
            index < 0 ||
            index >= _currentMsg.Targets.Count)
        {
            return;
        }

        var target = _currentMsg.Targets[index];
        var runner = GraphHub.Instance?.DefaultRunner;
        if (runner == null)
            return;

        bool resumed = runner.ResumeSuspendedSignalToTarget(
            _currentMsg.PackID,
            _currentMsg.SignalId,
            _currentMsg.SourceNodeId,
            target.TargetNodeId);

        if (!resumed)
            return;

        var facade = GraphHub.Instance?.GetFacade<RunStageFacade>();
        if (facade != null && facade.HasWaitingStage())
            facade.ResumeWaitingStage(0);

        _currentMsg = null;
    }

    private void EnsureStyles()
    {
        _boxStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(28, 28, 24, 24)
        };

        _titleStyle ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            padding = new RectOffset(12, 12, 10, 10)
        };

        _speakerStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };

        _bodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };

        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 28,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
    }

    private static void DrawSolidRect(Rect rect, Color color)
    {
        Color oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }
}
