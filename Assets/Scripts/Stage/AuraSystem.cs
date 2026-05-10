using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public readonly struct AuraContext
{
    public readonly EntitySystem EntitySystem;
    public readonly StageEvent StageEvent;
    public readonly AuraSource Source;

    public AuraContext(EntitySystem entitySystem, StageEvent stageEvent, AuraSource source = default)
    {
        EntitySystem = entitySystem;
        StageEvent = stageEvent;
        Source = source;
    }
}

public readonly struct AuraSource
{
    public readonly EntityHandle Entity;
    public readonly int EntityIndex;
    public readonly int SourceTagId;
    public readonly EntityAuraDefinition Definition;

    public AuraSource(EntityHandle entity, int entityIndex, int sourceTagId, EntityAuraDefinition definition)
    {
        Entity = entity;
        EntityIndex = entityIndex;
        SourceTagId = sourceTagId;
        Definition = definition;
    }
}

public abstract class AuraProvider
{
    public abstract void Clear(AuraContext context);
    public abstract void Apply(AuraContext context);
}

public abstract class BatchAuraProvider
{
    public abstract void Clear(AuraContext context, IReadOnlyList<AuraSource> sources);
    public abstract void Apply(AuraContext context, IReadOnlyList<AuraSource> sources);
}

[System.Serializable]
public abstract class EntityAuraDefinition
{
    [ReadOnly, ShowInInspector]
    public virtual string Label => GetType().Name;

    public virtual bool SupportsBatch => false;
    public virtual string BatchKey => GetType().FullName;

    public virtual AuraProvider CreateProvider(AuraSource source)
    {
        return null;
    }

    public virtual BatchAuraProvider CreateBatchProvider()
    {
        return null;
    }
}

[System.Serializable]
public sealed class GoConnectedGroupAuraDefinition : EntityAuraDefinition
{
    public override string Label => "Go Connected Group";
    public override bool SupportsBatch => true;
    public override string BatchKey => "go.connected_group";

    public override BatchAuraProvider CreateBatchProvider()
    {
        return new GoAuraProvider();
    }
}

public class AuraResolutionSystem : MonoBehaviour
{
    public static AuraResolutionSystem Instance { get; private set; }

    private readonly List<AuraSource> _sources = new();
    private readonly List<AuraProvider> _singleProviders = new();
    private readonly Dictionary<string, BatchGroup> _batchGroups = new();

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        EventBusSystem.Instance?.On(StageEventType.AuraUpdate, OnAuraUpdate);
    }

    private void OnDisable()
    {
        EventBusSystem.Instance?.Off(StageEventType.AuraUpdate, OnAuraUpdate);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnAuraUpdate(StageEvent evt)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        CollectSources(entitySystem);
        BuildProviders();

        var context = new AuraContext(entitySystem, evt);
        for (int i = 0; i < _singleProviders.Count; i++)
            _singleProviders[i].Clear(context);

        foreach (var group in _batchGroups.Values)
            group.Provider.Clear(context, group.Sources);

        for (int i = 0; i < _singleProviders.Count; i++)
            _singleProviders[i].Apply(context);

        foreach (var group in _batchGroups.Values)
            group.Provider.Apply(context, group.Sources);
    }

    private void CollectSources(EntitySystem entitySystem)
    {
        _sources.Clear();
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            EntityBP sourceBP = entities.propertyComponents[i].SourceBP;
            if (sourceBP == null || sourceBP.auras == null)
                continue;

            var handle = entitySystem.GetHandleFromId(entities.coreComponents[i].Id);
            int sourceTagId = entities.propertyComponents[i].SourceTagId;
            for (int auraIndex = 0; auraIndex < sourceBP.auras.Count; auraIndex++)
            {
                var definition = sourceBP.auras[auraIndex];
                if (definition == null)
                    continue;

                _sources.Add(new AuraSource(handle, i, sourceTagId, definition));
            }
        }
    }

    private void BuildProviders()
    {
        _singleProviders.Clear();
        _batchGroups.Clear();

        for (int i = 0; i < _sources.Count; i++)
        {
            var source = _sources[i];
            if (!source.Definition.SupportsBatch)
            {
                var provider = source.Definition.CreateProvider(source);
                if (provider != null)
                    _singleProviders.Add(provider);
                continue;
            }

            string batchKey = source.Definition.BatchKey ?? source.Definition.GetType().FullName;
            if (!_batchGroups.TryGetValue(batchKey, out var group))
            {
                var provider = source.Definition.CreateBatchProvider();
                if (provider == null)
                    continue;

                group = new BatchGroup(provider);
                _batchGroups[batchKey] = group;
            }

            group.Sources.Add(source);
        }
    }

    private sealed class BatchGroup
    {
        public readonly BatchAuraProvider Provider;
        public readonly List<AuraSource> Sources = new();

        public BatchGroup(BatchAuraProvider provider)
        {
            Provider = provider;
        }
    }
}
