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
    private const int MaxResolutionPasses = 8;

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

        IntentType executingType = intentComponent.Type;
        Intent executingIntent = intentComponent.Intent;
        var eventBus = EventBusSystem.Instance;
        eventBus?.Publish(new StageEvent(
            StageEventType.BeforeIntentExecute,
            actor: actor,
            entity: actor,
            entityType: entitySystem.entities.coreComponents[entityIndex].EntityType,
            intentType: executingType,
            intent: executingIntent,
            from: entitySystem.entities.coreComponents[entityIndex].Position,
            to: entitySystem.entities.coreComponents[entityIndex].Position,
            sourceTagId: entitySystem.entities.propertyComponents[entityIndex].SourceTagId));

        switch (executingType)
        {
            case IntentType.Move:
                MoveSystem.Instance?.Execute(actor, executingIntent as MoveIntent);
                break;
            case IntentType.Attack:
                AttackSystem.Instance?.Execute(actor, executingIntent as AttackIntent);
                break;
            case IntentType.Card:
                if (CardEffectSystem.Instance != null)
                    CardEffectSystem.Instance.Execute(actor, executingIntent as CardIntent);
                else
                    Debug.LogWarning("[IntentSystem] Card intent was submitted, but CardEffectSystem is missing.");
                break;
            case IntentType.Spawn:
                if (SpawnSystem.Instance != null)
                    SpawnSystem.Instance.Execute(actor, executingIntent as SpawnIntent);
                else
                    Debug.LogWarning("[IntentSystem] Spawn intent was submitted, but SpawnSystem is missing.");
                break;
        }

        if (entitySystem.IsValid(actor))
        {
            int postIndex = entitySystem.GetIndex(actor);
            eventBus?.Publish(new StageEvent(
                StageEventType.AfterIntentExecute,
                actor: actor,
                entity: actor,
                entityType: entitySystem.entities.coreComponents[postIndex].EntityType,
                intentType: executingType,
                intent: executingIntent,
                from: entitySystem.entities.coreComponents[postIndex].Position,
                to: entitySystem.entities.coreComponents[postIndex].Position,
                sourceTagId: entitySystem.entities.propertyComponents[postIndex].SourceTagId));
        }
        else
        {
            eventBus?.Publish(new StageEvent(
                StageEventType.AfterIntentExecute,
                actor: actor,
                entity: actor,
                intentType: executingType,
                intent: executingIntent));
        }

        intentComponent.Type = IntentType.None;
        intentComponent.Intent = null;

        ResolveWorldState(actor, executingType);

        Return(executingIntent);
    }

    public void ResolveWorldState()
    {
        ResolveWorldState(EntityHandle.None, IntentType.None);
    }

    public void ResolveWorldState(EntityHandle actor, IntentType intentType)
    {
        var eventBus = EventBusSystem.Instance;
        if (eventBus == null)
            return;

        var context = new IntentResolutionContext
        {
            Actor = actor,
            IntentType = intentType
        };

        eventBus.Publish(new StageEvent(
            StageEventType.IntentResolutionBegin,
            actor: actor,
            intentType: intentType,
            resolutionContext: context));

        for (int pass = 0; pass < MaxResolutionPasses; pass++)
        {
            context.Pass = pass;
            context.AnyDeathResolved = false;

            eventBus.Publish(new StageEvent(
                StageEventType.AuraUpdate,
                actor: actor,
                intentType: intentType,
                resolutionContext: context));

            eventBus.Publish(new StageEvent(
                StageEventType.DeathCheck,
                actor: actor,
                intentType: intentType,
                resolutionContext: context));

            if (!context.AnyDeathResolved)
                break;
        }

        eventBus.Publish(new StageEvent(
            StageEventType.IntentResolutionEnd,
            actor: actor,
            intentType: intentType,
            resolutionContext: context));
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
