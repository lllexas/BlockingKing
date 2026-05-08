using UnityEngine;
using System.Collections.Generic;

// ─────────── EntityHandle ───────────

public struct EntityHandle : System.IEquatable<EntityHandle>
{
    public int Id;
    public int Version;

    public static EntityHandle None => new() { Id = -1, Version = 0 };

    public bool Equals(EntityHandle other) => Id == other.Id && Version == other.Version;
    public override bool Equals(object obj) => obj is EntityHandle other && Equals(other);
    public override int GetHashCode() => Id * 397 ^ Version;
    public static bool operator ==(EntityHandle left, EntityHandle right) => left.Equals(right);
    public static bool operator !=(EntityHandle left, EntityHandle right) => !left.Equals(right);
}

// ─────────── EntitySystem ───────────

public class EntitySystem : MonoBehaviour
{
    #region Singleton

    public static EntitySystem Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        TickSystem.OnTick -= UpdateTick;
        if (Instance == this)
            Instance = null;
    }

    #endregion

    #region Fields

    public bool IsInitialized => _initialized;
    public EntityComponents entities;

    private bool _initialized;
    private int _maxEntityCount;
    private int[] _idToDataIndex;
    private int[] _dataIndexToId;
    private int[] _idVersions;
    private System.Collections.Generic.Queue<int> _freeIds;
    private readonly HashSet<int> _wallTerrainIds = new();
    private int _defaultFloorTerrainId;

    #endregion

    #region Initialize

    public void Initialize(int maxEntityCount, int mapWidth, int mapHeight)
    {
        bool needsReallocation = !_initialized || _maxEntityCount != maxEntityCount;
        _maxEntityCount = maxEntityCount;

        entities ??= new EntityComponents();

        entities.entityCount = 0;
        entities.mapWidth = mapWidth;
        entities.mapHeight = mapHeight;

        if (needsReallocation)
        {
            entities.coreComponents = new CoreComponent[maxEntityCount];
            entities.propertyComponents = new PropertyComponent[maxEntityCount];
            entities.intentComponents = new IntentComponent[maxEntityCount];

            entities.groundMap = new int[mapWidth * mapHeight];
            entities.gridMap = new int[mapWidth * mapHeight];

            _idToDataIndex = new int[maxEntityCount];
            _dataIndexToId = new int[maxEntityCount];
            _idVersions = new int[maxEntityCount];
            _freeIds = new System.Collections.Generic.Queue<int>();

            for (int i = 0; i < maxEntityCount; i++)
            {
                _freeIds.Enqueue(i);
                _idVersions[i] = 1;
                _idToDataIndex[i] = -1;
            }
        }
        else
        {
            System.Array.Clear(entities.coreComponents, 0, maxEntityCount);
            System.Array.Clear(entities.propertyComponents, 0, maxEntityCount);
            System.Array.Clear(entities.intentComponents, 0, maxEntityCount);
            System.Array.Clear(entities.groundMap, 0, entities.groundMap.Length);
            System.Array.Clear(entities.gridMap, 0, entities.gridMap.Length);

            _freeIds.Clear();
            for (int i = 0; i < maxEntityCount; i++)
            {
                _freeIds.Enqueue(i);
                _idVersions[i]++;
                _idToDataIndex[i] = -1;
            }
        }

        // gridMap 初始为 -1（无占据）
        for (int i = 0; i < entities.gridMap.Length; i++)
            entities.gridMap[i] = -1;

        TickSystem.OnTick -= UpdateTick;
        TickSystem.OnTick += UpdateTick;

        _initialized = true;
        Debug.Log($"[EntitySystem] 初始化完成，容量={maxEntityCount}，地图={mapWidth}x{mapHeight}");
    }

    #endregion

    #region Create / Destroy

    public EntityHandle CreateEntity(EntityType type, Vector2Int pos, bool occupiesGrid = true)
    {
        if (!_initialized) return EntityHandle.None;
        if (entities.entityCount >= _maxEntityCount || _freeIds.Count == 0)
            return EntityHandle.None;

        int index = entities.entityCount;
        entities.entityCount++;
        int id = _freeIds.Dequeue();
        int version = _idVersions[id];
        _idToDataIndex[id] = index;
        _dataIndexToId[index] = id;
        var handle = new EntityHandle { Id = id, Version = version };

        ref var core = ref entities.coreComponents[index];
        core.EntityType = type;
        core.Position = pos;
        core.Id = id;
        core.Health = 1;
        core.OccupiesGrid = occupiesGrid;

        entities.propertyComponents[index] = default;
        entities.intentComponents[index] = default;

        int mapIdx = pos.y * entities.mapWidth + pos.x;
        if (occupiesGrid && mapIdx >= 0 && mapIdx < entities.gridMap.Length)
            entities.gridMap[mapIdx] = id;

        return handle;
    }

    public void DestroyEntity(EntityHandle handle)
    {
        if (!IsValid(handle)) return;

        int idToRemove = handle.Id;
        int indexToRemove = _idToDataIndex[idToRemove];
        int lastIndex = entities.entityCount - 1;

        var pos = entities.coreComponents[indexToRemove].Position;
        int mapIdx = pos.y * entities.mapWidth + pos.x;
        if (entities.coreComponents[indexToRemove].OccupiesGrid
            && mapIdx >= 0 && mapIdx < entities.gridMap.Length
            && entities.gridMap[mapIdx] == idToRemove)
            entities.gridMap[mapIdx] = -1;

        if (indexToRemove != lastIndex)
        {
            entities.coreComponents[indexToRemove] = entities.coreComponents[lastIndex];
            entities.propertyComponents[indexToRemove] = entities.propertyComponents[lastIndex];
            entities.intentComponents[indexToRemove] = entities.intentComponents[lastIndex];

            int idOfMoved = _dataIndexToId[lastIndex];
            _idToDataIndex[idOfMoved] = indexToRemove;
            _dataIndexToId[indexToRemove] = idOfMoved;
        }

        _idToDataIndex[idToRemove] = -1;
        entities.entityCount--;

        _freeIds.Enqueue(idToRemove);
        _idVersions[idToRemove]++;
    }

    #endregion

    #region Query

    public bool IsValid(EntityHandle handle)
    {
        if (handle.Id < 0 || handle.Id >= _idVersions.Length) return false;
        if (_idVersions[handle.Id] != handle.Version) return false;
        int dataIndex = _idToDataIndex[handle.Id];
        return dataIndex >= 0 && dataIndex < entities.entityCount;
    }

    public EntityHandle GetHandleFromId(int id)
    {
        if (id < 0 || id >= _idVersions.Length) return EntityHandle.None;
        return new EntityHandle { Id = id, Version = _idVersions[id] };
    }

    public int GetIndex(EntityHandle handle)
    {
        if (!IsValid(handle)) return -1;
        return _idToDataIndex[handle.Id];
    }

    #endregion

    #region Tick

    private void UpdateTick()
    {
        IntentSystem.Instance?.Tick();
        EnemyAutoAISystem.Instance?.Tick();
    }

    #endregion

    #region Clear

    public void ClearWorld()
    {
        if (!_initialized || entities == null) return;

        _freeIds.Clear();
        for (int i = 0; i < _maxEntityCount; i++)
        {
            _freeIds.Enqueue(i);
            _idVersions[i]++;
            _idToDataIndex[i] = -1;
        }

        TickSystem.OnTick -= UpdateTick;
        entities.entityCount = 0;

        if (entities.gridMap != null)
            System.Array.Fill(entities.gridMap, -1);

        if (entities.groundMap != null)
            System.Array.Fill(entities.groundMap, 0);
    }

    #endregion

    #region Grid Helpers

    public int GetOccupantId(Vector2Int pos)
    {
        int idx = pos.y * entities.mapWidth + pos.x;
        if (idx < 0 || idx >= entities.gridMap.Length) return -1;
        return entities.gridMap[idx];
    }

    public EntityHandle GetOccupant(Vector2Int pos)
    {
        int id = GetOccupantId(pos);
        return id >= 0 ? GetHandleFromId(id) : EntityHandle.None;
    }

    public int GetTerrain(Vector2Int pos)
    {
        int idx = pos.y * entities.mapWidth + pos.x;
        if (idx < 0 || idx >= entities.groundMap.Length) return 0;
        return entities.groundMap[idx];
    }

    public bool IsBlocked(Vector2Int pos)
    {
        if (!IsInsideMap(pos)) return true;

        int idx = ToMapIndex(pos);
        if (entities.gridMap[idx] >= 0)
            return true;

        return IsWallTerrain(entities.groundMap[idx]);
    }

    public bool IsWall(Vector2Int pos)
    {
        if (!IsInsideMap(pos)) return false;
        return IsWallTerrain(entities.groundMap[ToMapIndex(pos)]);
    }

    public void SetWallTerrainIds(IEnumerable<int> wallTerrainIds, int defaultFloorTerrainId)
    {
        _wallTerrainIds.Clear();
        if (wallTerrainIds != null)
        {
            foreach (int terrainId in wallTerrainIds)
                _wallTerrainIds.Add(terrainId);
        }

        _defaultFloorTerrainId = defaultFloorTerrainId;
    }

    public EntityHandle TryMaterializeWall(Vector2Int pos, int health)
    {
        if (!IsInsideMap(pos)) return EntityHandle.None;

        int idx = ToMapIndex(pos);
        int occupantId = entities.gridMap[idx];
        if (occupantId >= 0)
        {
            var occupant = GetHandleFromId(occupantId);
            int occupantIndex = GetIndex(occupant);
            if (occupantIndex >= 0 && entities.coreComponents[occupantIndex].EntityType == EntityType.Wall)
                return occupant;

            return EntityHandle.None;
        }

        if (!IsWallTerrain(entities.groundMap[idx]))
            return EntityHandle.None;

        var handle = CreateEntity(EntityType.Wall, pos);
        int entityIndex = GetIndex(handle);
        if (entityIndex >= 0)
            entities.coreComponents[entityIndex].Health = Mathf.Max(1, health);

        entities.groundMap[idx] = _defaultFloorTerrainId;
        return handle;
    }

    public void SetTerrain(Vector2Int pos, int terrainId)
    {
        if (!IsInsideMap(pos)) return;
        entities.groundMap[ToMapIndex(pos)] = terrainId;
    }

    public void SetTerrain(int[][] map)
    {
        if (map == null) return;
        for (int y = 0; y < map.Length; y++)
            for (int x = 0; x < map[y].Length; x++)
            {
                int idx = y * entities.mapWidth + x;
                if (idx < entities.groundMap.Length)
                    entities.groundMap[idx] = map[y][x];
            }
    }

    public bool IsInsideMap(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < entities.mapWidth && pos.y >= 0 && pos.y < entities.mapHeight;
    }

    private int ToMapIndex(Vector2Int pos)
    {
        return pos.y * entities.mapWidth + pos.x;
    }

    private bool IsWallTerrain(int terrainId)
    {
        return _wallTerrainIds.Contains(terrainId);
    }

    #endregion
}
