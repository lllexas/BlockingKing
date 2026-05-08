using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Intent 对象池 + 消费入口。
/// </summary>
public class IntentSystem : MonoBehaviour
{
    public static IntentSystem Instance { get; private set; }

    private readonly Dictionary<Type, Stack<Intent>> _pools = new();
    private readonly List<EntityHandle> _activeIntentEntities = new();
    private EntityHandle _playerIntentActor = EntityHandle.None;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Tick()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        if (_playerIntentActor != EntityHandle.None)
            ExecuteIntent(entitySystem, _playerIntentActor);

        for (int i = 0; i < _activeIntentEntities.Count; i++)
        {
            var actor = _activeIntentEntities[i];
            if (actor == _playerIntentActor)
                continue;

            ExecuteIntent(entitySystem, actor);
        }

        _activeIntentEntities.Clear();
        _playerIntentActor = EntityHandle.None;
    }

    // ──────── 对象池 ────────

    public T Request<T>() where T : Intent, new()
    {
        var type = typeof(T);
        if (!_pools.TryGetValue(type, out var stack))
        {
            stack = new Stack<Intent>();
            _pools[type] = stack;
        }

        if (stack.Count > 0)
            return (T)stack.Pop();

        return new T();
    }

    public void Return(Intent intent)
    {
        if (intent == null) return;
        intent.Reset();

        var type = intent.GetType();
        if (!_pools.TryGetValue(type, out var stack))
        {
            stack = new Stack<Intent>();
            _pools[type] = stack;
        }
        stack.Push(intent);
    }

    public bool SetIntent(EntityHandle actor, IntentType intentType, Intent intent)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsValid(actor) || intent == null)
            return false;

        int index = entitySystem.GetIndex(actor);
        if (index < 0)
            return false;

        ref var intentComponent = ref entitySystem.entities.intentComponents[index];
        if (intentComponent.Intent != null)
            Return(intentComponent.Intent);

        intentComponent.Type = intentType;
        intentComponent.Intent = intent;

        if (!_activeIntentEntities.Contains(actor))
            _activeIntentEntities.Add(actor);

        return true;
    }

    public bool SetPlayerIntent(EntityHandle actor, IntentType intentType, Intent intent)
    {
        if (!SetIntent(actor, intentType, intent))
            return false;

        _playerIntentActor = actor;
        return true;
    }

    private void ExecuteIntent(EntitySystem entitySystem, EntityHandle actor)
    {
        if (entitySystem == null || !entitySystem.IsValid(actor))
            return;

        int entityIndex = entitySystem.GetIndex(actor);
        if (entityIndex < 0)
            return;

        ref var intentComponent = ref entitySystem.entities.intentComponents[entityIndex];
        if (intentComponent.Type == IntentType.None || intentComponent.Intent == null)
            return;

        switch (intentComponent.Type)
        {
            case IntentType.Move:
                MoveSystem.Instance?.Execute(actor, intentComponent.Intent as MoveIntent);
                break;
            case IntentType.Attack:
                AttackSystem.Instance?.Execute(actor, intentComponent.Intent as AttackIntent);
                break;
        }

        Return(intentComponent.Intent);
        intentComponent.Type = IntentType.None;
        intentComponent.Intent = null;
    }

    public void ForEachActiveIntent(System.Action<EntityHandle, IntentComponent> visitor)
    {
        if (visitor == null)
            return;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        for (int i = 0; i < _activeIntentEntities.Count; i++)
        {
            var actor = _activeIntentEntities[i];
            int index = entitySystem.GetIndex(actor);
            if (index < 0)
                continue;

            var intentComponent = entitySystem.entities.intentComponents[index];
            if (intentComponent.Type == IntentType.None || intentComponent.Intent == null)
                continue;

            visitor(actor, intentComponent);
        }
    }

    public void Clear()
    {
        foreach (var stack in _pools.Values)
            stack.Clear();
        _pools.Clear();
        _activeIntentEntities.Clear();
        _playerIntentActor = EntityHandle.None;
    }
}
