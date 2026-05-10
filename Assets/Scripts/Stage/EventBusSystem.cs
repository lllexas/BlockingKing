using System;
using System.Collections.Generic;
using UnityEngine;

public enum StageEventType : byte
{
    None,
    BeforeIntentExecute,
    AfterIntentExecute,
    IntentResolutionBegin,
    AuraUpdate,
    DeathCheck,
    IntentResolutionEnd,
    EntityCreated,
    EntityDestroyed,
    EntityMoved
}

public sealed class IntentResolutionContext
{
    public EntityHandle Actor;
    public IntentType IntentType;
    public int Pass;
    public bool AnyDeathResolved;
}

public readonly struct StageEvent
{
    public readonly StageEventType Type;
    public readonly EntityHandle Actor;
    public readonly EntityHandle Entity;
    public readonly EntityType EntityType;
    public readonly IntentType IntentType;
    public readonly Intent Intent;
    public readonly Vector2Int From;
    public readonly Vector2Int To;
    public readonly int SourceTagId;
    public readonly IntentResolutionContext ResolutionContext;

    public StageEvent(
        StageEventType type,
        EntityHandle actor = default,
        EntityHandle entity = default,
        EntityType entityType = EntityType.None,
        IntentType intentType = IntentType.None,
        Intent intent = null,
        Vector2Int from = default,
        Vector2Int to = default,
        int sourceTagId = 0,
        IntentResolutionContext resolutionContext = null)
    {
        Type = type;
        Actor = actor;
        Entity = entity;
        EntityType = entityType;
        IntentType = intentType;
        Intent = intent;
        From = from;
        To = to;
        SourceTagId = sourceTagId;
        ResolutionContext = resolutionContext;
    }
}

public class EventBusSystem : MonoBehaviour
{
    public static EventBusSystem Instance { get; private set; }

    private sealed class Handler
    {
        public object Target;
        public Action<StageEvent> Action;
        public int Priority;
    }

    private readonly Dictionary<StageEventType, List<Handler>> _eventTable = new();
    private readonly Dictionary<object, HashSet<StageEventType>> _targetToEvents = new();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Publish(StageEvent evt)
    {
        if (!_eventTable.TryGetValue(evt.Type, out var handlers))
            return;

        for (int i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            try
            {
                if (handler.Target is UnityEngine.Object unityTarget && unityTarget == null)
                {
                    handlers.RemoveAt(i);
                    i--;
                    continue;
                }

                handler.Action.Invoke(evt);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EventBusSystem] {evt.Type} handler failed: {e}");
            }
        }
    }

    public void On(StageEventType eventType, Action<StageEvent> callback, int priority = 0)
    {
        if (eventType == StageEventType.None || callback == null)
            return;

        if (!_eventTable.TryGetValue(eventType, out var handlers))
        {
            handlers = new List<Handler>();
            _eventTable[eventType] = handlers;
        }

        for (int i = 0; i < handlers.Count; i++)
        {
            if (handlers[i].Action == callback)
                return;
        }

        var target = callback.Target;
        handlers.Add(new Handler { Target = target, Action = callback, Priority = priority });
        handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        if (target == null)
            return;

        if (!_targetToEvents.TryGetValue(target, out var events))
        {
            events = new HashSet<StageEventType>();
            _targetToEvents[target] = events;
        }

        events.Add(eventType);
    }

    public void Off(StageEventType eventType, Action<StageEvent> callback)
    {
        if (callback == null || !_eventTable.TryGetValue(eventType, out var handlers))
            return;

        int removed = handlers.RemoveAll(handler => handler.Action == callback);
        if (removed <= 0 || callback.Target == null)
            return;

        if (_targetToEvents.TryGetValue(callback.Target, out var events))
            events.Remove(eventType);
    }

    public void Unregister(object target)
    {
        if (target == null || !_targetToEvents.TryGetValue(target, out var events))
            return;

        foreach (var eventType in events)
        {
            if (_eventTable.TryGetValue(eventType, out var handlers))
                handlers.RemoveAll(handler => handler.Target == target);
        }

        _targetToEvents.Remove(target);
    }

    public void Clear()
    {
        _eventTable.Clear();
        _targetToEvents.Clear();
    }
}
