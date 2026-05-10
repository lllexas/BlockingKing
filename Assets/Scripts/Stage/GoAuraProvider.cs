using System.Collections.Generic;
using UnityEngine;

public sealed class GoAuraProvider : BatchAuraProvider
{
    private readonly Queue<Vector2Int> _frontier = new();
    private readonly List<int> _groupIndices = new();
    private readonly HashSet<int> _sourceEntityIds = new();
    private bool[] _visited;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    public override void Clear(AuraContext context, IReadOnlyList<AuraSource> sources)
    {
        var entitySystem = context.EntitySystem;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        var entities = entitySystem.entities;
        for (int i = 0; i < sources.Count; i++)
        {
            int index = sources[i].EntityIndex;
            if (index < 0 || index >= entities.entityCount)
                continue;

            entities.statusComponents[index].AttackModifier = 0;
            entities.statusComponents[index].MaxHealthModifier = 0;
            entities.propertyComponents[index].Parameter0 = 0;
            entities.propertyComponents[index].Attack = CombatStats.GetAttack(entities.statusComponents[index]);
        }
    }

    public override void Apply(AuraContext context, IReadOnlyList<AuraSource> sources)
    {
        var entitySystem = context.EntitySystem;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        var entities = entitySystem.entities;
        BuildSourceSet(entitySystem, sources);

        int mapSize = Mathf.Max(0, entities.mapWidth * entities.mapHeight);
        if (_visited == null || _visited.Length != mapSize)
            _visited = new bool[mapSize];
        else
            System.Array.Clear(_visited, 0, _visited.Length);

        for (int i = 0; i < sources.Count; i++)
        {
            int entityIndex = sources[i].EntityIndex;
            if (entityIndex < 0 || entityIndex >= entities.entityCount)
                continue;

            int currentId = entities.coreComponents[entityIndex].Id;
            if (!_sourceEntityIds.Contains(currentId))
                continue;

            ref var core = ref entities.coreComponents[entityIndex];
            if (core.EntityType != EntityType.Enemy)
                continue;

            int mapIndex = ToMapIndex(entities, core.Position);
            if (mapIndex < 0 || mapIndex >= _visited.Length || _visited[mapIndex])
                continue;

            int groupSize = CollectGroup(entitySystem, core.Position);
            ApplyGroupStats(entitySystem, groupSize);
        }
    }

    private void BuildSourceSet(EntitySystem entitySystem, IReadOnlyList<AuraSource> sources)
    {
        _sourceEntityIds.Clear();
        var entities = entitySystem.entities;
        for (int i = 0; i < sources.Count; i++)
        {
            int index = sources[i].EntityIndex;
            if (index < 0 || index >= entities.entityCount)
                continue;

            _sourceEntityIds.Add(entities.coreComponents[index].Id);
        }
    }

    private int CollectGroup(EntitySystem entitySystem, Vector2Int start)
    {
        var entities = entitySystem.entities;
        _frontier.Clear();
        _groupIndices.Clear();

        _frontier.Enqueue(start);
        _visited[ToMapIndex(entities, start)] = true;

        while (_frontier.Count > 0)
        {
            Vector2Int current = _frontier.Dequeue();
            int entityIndex = GetGoEnemyIndexAt(entitySystem, current);
            if (entityIndex < 0)
                continue;

            _groupIndices.Add(entityIndex);

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (!entitySystem.IsInsideMap(next))
                    continue;

                int mapIndex = ToMapIndex(entities, next);
                if (_visited[mapIndex])
                    continue;

                if (GetGoEnemyIndexAt(entitySystem, next) < 0)
                    continue;

                _visited[mapIndex] = true;
                _frontier.Enqueue(next);
            }
        }

        return _groupIndices.Count;
    }

    private void ApplyGroupStats(EntitySystem entitySystem, int groupSize)
    {
        int value = Mathf.Max(1, groupSize);
        var entities = entitySystem.entities;
        for (int i = 0; i < _groupIndices.Count; i++)
        {
            int index = _groupIndices[i];
            ref var status = ref entities.statusComponents[index];
            status.AttackModifier = value - status.BaseAttack;
            status.MaxHealthModifier = value - status.BaseMaxHealth;
            entities.propertyComponents[index].Attack = CombatStats.GetAttack(status);
            entities.propertyComponents[index].Parameter0 = value;
        }
    }

    private int GetGoEnemyIndexAt(EntitySystem entitySystem, Vector2Int pos)
    {
        int occupantId = entitySystem.GetOccupantId(pos);
        if (occupantId < 0)
            return -1;

        int index = entitySystem.GetIndex(entitySystem.GetHandleFromId(occupantId));
        if (index < 0)
            return -1;

        var entities = entitySystem.entities;
        return entities.coreComponents[index].EntityType == EntityType.Enemy &&
               _sourceEntityIds.Contains(entities.coreComponents[index].Id)
            ? index
            : -1;
    }

    private static int ToMapIndex(EntityComponents entities, Vector2Int pos)
    {
        return pos.y * entities.mapWidth + pos.x;
    }
}
