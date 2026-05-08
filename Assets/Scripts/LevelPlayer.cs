using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 播放场景入口：Inspector 可拖入 LevelData SO 直接预览，
/// 面板按钮一键重建 Mesh。
/// 优先级：QuickPlaySession > 直接引用。
/// </summary>
public class LevelPlayer : MonoBehaviour
{
    [Header("关卡数据")]
    [SerializeField, Tooltip("直接拖入 LevelData SO（QuickPlaySession 激活时会覆盖它）")]
    private LevelData levelData;

    [SerializeField, Tooltip("直接拖入 TileMappingConfig（可选，为空则无墙壁）")]
    private TileMappingConfig tileConfig;

    [Header("Mesh 参数")]
    public float cellSize = 1f;
    public float wallHeight = 0.4f;
    public float tagMarkerSize = 0.35f;
    public float tagYOffset = 0.02f;

    [Header("渲染")]
    [Tooltip("拖入预设材质。留空则自动创建（重建时复用，属性不丢失）。")]
    public Material material;

    [Header("ECS")]
    [SerializeField] private int maxEntityCount = 256;

    private LevelData _level;
    private TileMappingConfig _config;
    private GameObject _meshGO;
    private Material _materialInstance;
    private bool _usingQuickPlaySession;

    private void Start()
    {
        ResolveLevelData();
        BuildMesh();
        BuildEntities();
    }

    private void ResolveLevelData()
    {
        if (_usingQuickPlaySession)
            return;

        // 快速播放优先于场景里的固定引用。
        var session = Resources.Load<QuickPlaySession>("QuickPlaySession");
        if (session != null && session.active && session.targetLevel != null)
        {
            _level = session.targetLevel;
            _config = session.config != null ? session.config : tileConfig;
            _config?.RebuildCache();
            session.active = false;
            _usingQuickPlaySession = true;
            return;
        }

        // 正常播放使用直接引用
        if (levelData != null)
        {
            _level = levelData;
            _config = tileConfig;
            _config?.RebuildCache();
            return;
        }
    }

    [Button("Rebuild Mesh", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void BuildMesh()
    {
        ResolveLevelData();
        if (_level == null)
        {
            Debug.LogWarning("[LevelPlayer] 无可用关卡数据。请拖入 LevelData SO 或设置 QuickPlaySession。");
            return;
        }

        Debug.Log($"[LevelPlayer] 开始构建: {_level.levelName}, " +
                  $"size={_level.width}x{_level.height}, wallHeight={wallHeight}");

        // 销毁旧 Mesh（保留材质实例以便复用）
        if (_meshGO != null)
        {
            var mr = _meshGO.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null && material == null)
                _materialInstance = mr.sharedMaterial;

            DestroyImmediate(_meshGO);
            _meshGO = null;
        }

        var builder = new LevelMeshBuilder
        {
            cellSize = cellSize,
            wallHeight = wallHeight,
            tagMarkerSize = tagMarkerSize,
            tagYOffset = tagYOffset,
        };

        Mesh mesh = builder.Build(_level.GetMap2D(), _config, GetLevelMarkerTags());
        if (mesh == null)
        {
            Debug.LogWarning("[LevelPlayer] 构建失败: mesh 为空");
            return;
        }

        _meshGO = new GameObject("LevelMesh");
        _meshGO.transform.SetParent(transform);
        _meshGO.AddComponent<MeshFilter>().mesh = mesh;

        EnsureLevelMaterial();

        _meshGO.AddComponent<MeshRenderer>().sharedMaterial = _materialInstance;

        // 通知相机控制器
        var camCtrl = Camera.main?.GetComponent<CameraController>();
        if (camCtrl != null)
        {
            camCtrl.SetMapBounds(_level.width, _level.height);
            camCtrl.ResetView();
        }

        Debug.Log($"[LevelPlayer] {_level.levelName} 已构建, 顶点={mesh.vertexCount}, " +
                  $"wallHeight={wallHeight}");
    }

    [Button("Restart Level", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void RestartLevel()
    {
        ResolveLevelData();
        BuildMesh();
        BuildEntities();
    }

    public void BuildEntities()
    {
        ResolveLevelData();
        if (_level == null)
        {
            Debug.LogWarning("[LevelPlayer] 无可用关卡数据，无法初始化 ECS。");
            return;
        }

        EnsureLevelMaterial();

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null)
            entitySystem = gameObject.AddComponent<EntitySystem>();

        EnsureRuntimeSystem<IntentSystem>();
        EnsureRuntimeSystem<MoveSystem>();
        EnsureRuntimeSystem<AttackSystem>();
        EnsureRuntimeSystem<EnemyAutoAISystem>();
        var drawSystem = EnsureRuntimeSystem<DrawSystem>();
        EnsureRuntimeSystem<UserInputReader>();
        drawSystem.SetWallMaterial(_materialInstance);

        entitySystem.Initialize(maxEntityCount, _level.width, _level.height);
        entitySystem.SetTerrain(_level.GetMap2D());
        entitySystem.SetWallTerrainIds(GetWallTerrainIds(), GetDefaultFloorTerrainId());

        int playerTagID = ResolveTagID("player", 3);
        int boxTagID = ResolveTagID("box", 2);
        int boxCoreTagID = ResolveTagID("Box.Core", -1);
        int targetTagID = ResolveTagID("target", 1);
        int enemyGoTagID = ResolveTagID("Enemy.Go", 4);
        int unstableWallTagID = ResolveTagID("Wall.Unstable", -1);

        foreach (var tag in _level.tags)
        {
            var pos = new Vector2Int(tag.x, tag.y);
            if (tag.tagID == playerTagID)
                CreateTaggedEntity(entitySystem, EntityType.Player, pos, tag.tagID);
            else if (tag.tagID == boxTagID)
                CreateTaggedEntity(entitySystem, EntityType.Box, pos, tag.tagID);
            else if (boxCoreTagID > 0 && tag.tagID == boxCoreTagID)
                CreateCoreBox(entitySystem, pos, tag.tagID);
            else if (tag.tagID == targetTagID)
                CreateTaggedEntity(entitySystem, EntityType.Target, pos, tag.tagID, false);
            else if (tag.tagID == enemyGoTagID)
                CreateTaggedEntity(entitySystem, EntityType.Enemy, pos, tag.tagID);
            else if (unstableWallTagID > 0 && tag.tagID == unstableWallTagID)
                CreateUnstableWall(entitySystem, pos, tag.tagID);
        }

        Debug.Log($"[LevelPlayer] ECS 已初始化，实体数={entitySystem.entities.entityCount}");
    }

    [Button("Clear Mesh", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void ClearMesh()
    {
        if (_meshGO == null)
        {
            Debug.LogWarning("[LevelPlayer] 没有可清除的 Mesh。");
            return;
        }

        DestroyImmediate(_meshGO);
        _meshGO = null;
        Debug.Log("[LevelPlayer] Mesh 已清除");
    }

    private T EnsureRuntimeSystem<T>() where T : Component
    {
        var system = GetComponent<T>();
        if (system == null)
            system = gameObject.AddComponent<T>();
        return system;
    }

    private void EnsureLevelMaterial()
    {
        // 材质：优先用 Inspector 拖入的 asset，其次复用已有实例，最后新建
        if (material != null)
        {
            _materialInstance = material;
            return;
        }

        if (_materialInstance == null)
            _materialInstance = new Material(Shader.Find("BlockingKing/LevelGeometric")
                                          ?? Shader.Find("Universal Render Pipeline/Lit"));
    }

    private EntityHandle CreateTaggedEntity(
        EntitySystem entitySystem,
        EntityType entityType,
        Vector2Int pos,
        int tagId,
        bool occupiesGrid = true)
    {
        var handle = entitySystem.CreateEntity(entityType, pos, occupiesGrid);
        ApplyEntityBP(entitySystem, handle, tagId);
        return handle;
    }

    private void CreateCoreBox(EntitySystem entitySystem, Vector2Int pos, int tagId)
    {
        var handle = CreateTaggedEntity(entitySystem, EntityType.Box, pos, tagId);
        int index = entitySystem.GetIndex(handle);
        if (index >= 0)
            entitySystem.entities.propertyComponents[index].IsCore = true;
    }

    private void CreateUnstableWall(EntitySystem entitySystem, Vector2Int pos, int tagId)
    {
        CreateTaggedEntity(entitySystem, EntityType.Wall, pos, tagId);
        entitySystem.SetTerrain(pos, GetDefaultFloorTerrainId());
    }

    private void ApplyEntityBP(EntitySystem entitySystem, EntityHandle handle, int tagId)
    {
        int index = entitySystem.GetIndex(handle);
        if (index < 0)
            return;

        EntityBP bp = _config != null ? _config.GetTagEntityBP(tagId) : null;
        if (bp == null)
            return;

        ref var core = ref entitySystem.entities.coreComponents[index];
        ref var properties = ref entitySystem.entities.propertyComponents[index];
        core.Health = Mathf.Max(1, bp.health);
        properties.Attack = Mathf.Max(0, bp.attack);
    }

    private List<LevelTagEntry> GetLevelMarkerTags()
    {
        var result = new List<LevelTagEntry>();
        if (_level?.tags == null)
            return result;

        int targetTagID = ResolveTagID("target", 1);
        foreach (var tag in _level.tags)
        {
            if (tag.tagID == targetTagID)
                result.Add(tag);
        }

        return result;
    }

    private int ResolveTagID(string tagName, int fallback)
    {
        if (_config == null || _config.tagDefinitions == null)
            return fallback;

        foreach (var tag in _config.tagDefinitions)
        {
            if (tag == null)
                continue;

            if (string.Equals(tag.tagName, tagName, System.StringComparison.OrdinalIgnoreCase))
                return tag.tagID;
        }

        return fallback;
    }

    private List<int> GetWallTerrainIds()
    {
        var result = new List<int>();
        if (_config == null || _config.entries == null)
            return result;

        foreach (var entry in _config.entries)
        {
            if (entry != null && entry.isWall)
                result.Add(entry.tileID);
        }

        return result;
    }

    private int GetDefaultFloorTerrainId()
    {
        if (_config == null || _config.entries == null)
            return 0;

        foreach (var entry in _config.entries)
        {
            if (entry != null && !entry.isWall && entry.tileID != 0)
                return entry.tileID;
        }

        return 0;
    }
}
