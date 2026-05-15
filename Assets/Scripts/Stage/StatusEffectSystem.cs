using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatusEffectDurationKind : byte
{
    Permanent,
    UntilTurnEnd,
    UntilLevelEnd
}

[Serializable]
public sealed class StatusEffectState
{
    public int EntityId;
    public int EntityVersion;
    public string EffectId;
    public int Stacks;
    public StatusEffectDurationKind DurationKind;
    public int SourceEntityId;
}

public sealed class StatusEffectSystem : MonoBehaviour
{
    public const string OverrunEffectId = "overrun";

    public static StatusEffectSystem Instance { get; private set; }

    private readonly List<StatusEffectState> _effects = new();

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        EventBusSystem.Instance?.On(StageEventType.EntityDestroyed, OnEntityDestroyed);
        EventBusSystem.Instance?.On(StageEventType.IntentResolutionEnd, OnIntentResolutionEnd);
    }

    private void OnDisable()
    {
        EventBusSystem.Instance?.Off(StageEventType.EntityDestroyed, OnEntityDestroyed);
        EventBusSystem.Instance?.Off(StageEventType.IntentResolutionEnd, OnIntentResolutionEnd);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public int GetStacks(EntityHandle entity, string effectId)
    {
        int index = FindIndex(entity, effectId);
        return index >= 0 ? Mathf.Max(0, _effects[index].Stacks) : 0;
    }

    public bool HasStacks(EntityHandle entity, string effectId, int amount = 1)
    {
        return GetStacks(entity, effectId) >= Mathf.Max(1, amount);
    }

    public bool AddStacks(EntityHandle entity, string effectId, int amount, StatusEffectDurationKind durationKind = StatusEffectDurationKind.Permanent, EntityHandle source = default)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(effectId) || !IsValidEntity(entity))
            return false;

        int index = FindIndex(entity, effectId);
        if (index < 0)
        {
            _effects.Add(new StatusEffectState
            {
                EntityId = entity.Id,
                EntityVersion = entity.Version,
                EffectId = effectId,
                Stacks = amount,
                DurationKind = durationKind,
                SourceEntityId = source.Id
            });
            return true;
        }

        _effects[index].Stacks = Mathf.Max(0, _effects[index].Stacks) + amount;
        if (_effects[index].DurationKind != StatusEffectDurationKind.Permanent &&
            (durationKind == StatusEffectDurationKind.Permanent || durationKind > _effects[index].DurationKind))
        {
            _effects[index].DurationKind = durationKind;
        }

        return true;
    }

    public bool TryConsumeStacks(EntityHandle entity, string effectId, int amount = 1)
    {
        amount = Mathf.Max(1, amount);
        int index = FindIndex(entity, effectId);
        if (index < 0 || _effects[index].Stacks < amount)
            return false;

        _effects[index].Stacks -= amount;
        if (_effects[index].Stacks <= 0)
            _effects.RemoveAt(index);

        return true;
    }

    public List<StatusEffectState> CaptureSnapshot()
    {
        var snapshot = new List<StatusEffectState>(_effects.Count);
        for (int i = 0; i < _effects.Count; i++)
            snapshot.Add(Clone(_effects[i]));

        return snapshot;
    }

    public void RestoreSnapshot(IReadOnlyList<StatusEffectState> snapshot)
    {
        _effects.Clear();
        if (snapshot == null)
            return;

        for (int i = 0; i < snapshot.Count; i++)
        {
            var state = snapshot[i];
            if (state == null || string.IsNullOrWhiteSpace(state.EffectId) || state.Stacks <= 0)
                continue;

            _effects.Add(Clone(state));
        }
    }

    public List<StatusEffectState> CaptureEntityEffects(EntityHandle entity, StatusEffectDurationKind? durationFilter = null)
    {
        var results = new List<StatusEffectState>();
        if (!IsValidEntity(entity))
            return results;

        for (int i = 0; i < _effects.Count; i++)
        {
            var state = _effects[i];
            if (state.EntityId != entity.Id || state.EntityVersion != entity.Version)
                continue;

            if (durationFilter.HasValue && state.DurationKind != durationFilter.Value)
                continue;

            results.Add(Clone(state));
        }

        return results;
    }

    public void ApplyEntityEffects(EntityHandle entity, IReadOnlyList<StatusEffectState> effects)
    {
        if (!IsValidEntity(entity))
            return;

        RemoveEffects(entity);
        if (effects == null)
            return;

        for (int i = 0; i < effects.Count; i++)
        {
            var state = effects[i];
            if (state == null || string.IsNullOrWhiteSpace(state.EffectId) || state.Stacks <= 0)
                continue;

            AddStacks(entity, state.EffectId, state.Stacks, state.DurationKind, EntityHandle.None);
        }
    }

    public void Clear()
    {
        _effects.Clear();
    }

    private void OnEntityDestroyed(StageEvent evt)
    {
        if (evt.Entity.Id < 0)
            return;

        RemoveEffects(evt.Entity);
    }

    private void RemoveEffects(EntityHandle entity)
    {
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            if (_effects[i].EntityId == entity.Id && _effects[i].EntityVersion == entity.Version)
                _effects.RemoveAt(i);
        }
    }

    private void OnIntentResolutionEnd(StageEvent evt)
    {
        if (evt.IntentType == IntentType.None)
            return;

        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            if (_effects[i].DurationKind == StatusEffectDurationKind.UntilTurnEnd)
                _effects.RemoveAt(i);
        }
    }

    private int FindIndex(EntityHandle entity, string effectId)
    {
        if (string.IsNullOrWhiteSpace(effectId))
            return -1;

        for (int i = 0; i < _effects.Count; i++)
        {
            var state = _effects[i];
            if (state.EntityId == entity.Id &&
                state.EntityVersion == entity.Version &&
                string.Equals(state.EffectId, effectId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsValidEntity(EntityHandle entity)
    {
        var entitySystem = EntitySystem.Instance;
        return entitySystem != null && entitySystem.IsInitialized && entitySystem.IsValid(entity);
    }

    private static StatusEffectState Clone(StatusEffectState state)
    {
        return new StatusEffectState
        {
            EntityId = state.EntityId,
            EntityVersion = state.EntityVersion,
            EffectId = state.EffectId,
            Stacks = Mathf.Max(0, state.Stacks),
            DurationKind = state.DurationKind,
            SourceEntityId = state.SourceEntityId
        };
    }
}
