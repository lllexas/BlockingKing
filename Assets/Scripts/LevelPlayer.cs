using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum LevelPlayMode
{
    Classic,
    StepLimit,
    Escort
}

public enum LevelDataSource
{
    None,
    Inspector,
    QuickPlaySession,
    RuntimeRequest
}

public enum LevelPlayResult
{
    None,
    Success,
    Failure,
    Cancelled
}

public sealed class LevelPlayRequest
{
    public LevelData Level;
    public TileMappingConfig Config;
    public LevelPlayMode Mode = LevelPlayMode.Classic;
    public int StepLimit = 30;
}

/// <summary>
/// 关卡播放入口。生命周期分为：
/// LoadLevel -> RebuildWorld -> StartPlayback -> StopPlayback。
/// Inspector/QuickPlay 只负责提供默认关卡；route/stage 应通过 LevelPlayRequest 显式播放。
/// </summary>
public class LevelPlayer : MonoBehaviour
{
    private const string LevelMeshObjectName = "LevelMesh";
    private const int DefaultPlayerTagID = 1;
    private const int DefaultBoxTagID = 2;
    private const int DefaultTargetTagID = 3;
    private const int DefaultTargetCoreTagID = 7;

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

    [SerializeField, Tooltip("B版地形渲染：用 GPU Instancing 绘制格子地形，支持 groundMap 变化自动刷新。")]
    private bool useInstancedTerrain = true;

    [Header("ECS")]
    [SerializeField] private int maxEntityCount = 256;

    [Header("Play Mode")]
    [SerializeField] private LevelPlayMode defaultPlayMode = LevelPlayMode.Classic;
    [SerializeField, Min(1)] private int defaultStepLimit = 30;

    private LevelData _level;
    private TileMappingConfig _config;
    private GameObject _meshGO;
    private Material _materialInstance;
    private LevelDataSource _levelSource;
    private ILevelPlayRule _playRule;
    private bool _isPlaying;
    private bool _isSettled;
    private LevelPlayMode _playMode;
    private LevelPlayResult _lastResult;
    private int _remainingSteps = -1;
    private GUIStyle _stepLimitStyle;
    private GUIStyle _stepLimitShadowStyle;
    private readonly HashSet<Vector2Int> _targetCells = new();
    private readonly HashSet<Vector2Int> _coreTargetCells = new();

    public LevelData CurrentLevel => _level;
    public TileMappingConfig CurrentConfig => _config;
    public LevelDataSource CurrentLevelSource => _levelSource;
    public LevelPlayMode PlayMode => _playMode;
    public LevelPlayResult LastResult => _lastResult;
    public int RemainingSteps => _remainingSteps;
    public bool IsPlaying => _isPlaying;

    private void Start()
    {
        var flow = GameFlowController.Instance;
        if (flow != null && !flow.ShouldLevelPlayerAutoBuild)
            return;

        if (LoadConfiguredLevel())
        {
            RebuildWorld();
            flow?.EnterLevel();
        }
    }

    private void OnDestroy()
    {
        StopPlayback();
    }

    private void OnGUI()
    {
        if (!_isPlaying || _playMode != LevelPlayMode.StepLimit)
            return;

        EnsureStepLimitStyles();

        string text = $"Steps: {Mathf.Max(0, _remainingSteps)}";
        float width = Mathf.Min(460f, Screen.width - 32f);
        var rect = new Rect((Screen.width - width) * 0.5f, 18f, width, 58f);
        var shadowRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height);

        GUI.Label(shadowRect, text, _stepLimitShadowStyle);
        GUI.Label(rect, text, _stepLimitStyle);
    }

    public bool PlayLevel(LevelData level)
    {
        return PlayLevel(new LevelPlayRequest
        {
            Level = level,
            Config = tileConfig,
            Mode = defaultPlayMode,
            StepLimit = defaultStepLimit
        });
    }

    public bool PlayLevel(LevelData level, LevelPlayMode mode)
    {
        return PlayLevel(new LevelPlayRequest
        {
            Level = level,
            Config = tileConfig,
            Mode = mode,
            StepLimit = defaultStepLimit
        });
    }

    public bool PlayLevel(LevelData level, LevelPlayMode mode, int maxSteps)
    {
        return PlayLevel(new LevelPlayRequest
        {
            Level = level,
            Config = tileConfig,
            Mode = mode,
            StepLimit = maxSteps
        });
    }

    public bool PlayLevel(LevelPlayRequest request)
    {
        if (request == null)
        {
            Debug.LogError("[LevelPlayer] PlayLevel failed: request is null.");
            return false;
        }

        StopPlayback();

        if (!LoadLevel(request.Level, request.Config != null ? request.Config : tileConfig, LevelDataSource.RuntimeRequest))
            return false;

        RebuildWorld();
        StartPlayback(request);
        return true;
    }

    public bool LoadLevel(LevelData level, TileMappingConfig config, LevelDataSource source)
    {
        if (level == null)
        {
            Debug.LogWarning("[LevelPlayer] LoadLevel failed: level is null.");
            return false;
        }

        _level = level;
        _config = config;
        _config?.RebuildCache();
        _levelSource = source;

        if (_config == null)
            Debug.LogWarning("[LevelPlayer] TileMappingConfig is missing. Tag IDs will use project defaults.");

        Debug.Log($"[LevelPlayer] Level loaded: {level.name}, source={source}");
        return true;
    }

    public bool LoadConfiguredLevel()
    {
        var session = Resources.Load<QuickPlaySession>("QuickPlaySession");
        if (session != null && session.active && session.targetLevel != null)
        {
            bool loaded = LoadLevel(
                session.targetLevel,
                session.config != null ? session.config : tileConfig,
                LevelDataSource.QuickPlaySession);

            session.active = false;
            return loaded;
        }

        return LoadLevel(levelData, tileConfig, LevelDataSource.Inspector);
    }

    public void RebuildWorld()
    {
        if (_level == null)
        {
            Debug.LogWarning("[LevelPlayer] RebuildWorld failed: no loaded level.");
            return;
        }

        BuildEntitiesInternal();
        CacheTargetCells();
        RebuildTerrainVisualsInternal();
    }

    public void StartPlayback(LevelPlayRequest request)
    {
        if (_level == null)
        {
            Debug.LogWarning("[LevelPlayer] StartPlayback failed: no loaded level.");
            return;
        }

        _playMode = request != null ? request.Mode : defaultPlayMode;
        _playRule = CreatePlayRule(_playMode);
        _isPlaying = true;
        _isSettled = false;
        _lastResult = LevelPlayResult.None;

        _playRule.Begin(this, request);

        GameFlowController.Instance?.EnterLevel();
        TickSystem.OnTick -= HandleTick;
        TickSystem.OnTick += HandleTick;

        Debug.Log($"[LevelPlayer] Playback started: {_level.levelName}, mode={_playMode}, steps={_remainingSteps}");
        _playRule.Evaluate(this);
    }

    public void StopPlayback()
    {
        TickSystem.OnTick -= HandleTick;
        _playRule = null;
        _isPlaying = false;
    }

    [Button("Rebuild World", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void RebuildWorldFromInspector()
    {
        if (_level == null && !LoadConfiguredLevel())
            return;

        RebuildWorld();
    }

    [Button("Rebuild Mesh", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void BuildMesh()
    {
        if (_level == null && !LoadConfiguredLevel())
            return;

        if (useInstancedTerrain)
        {
            if (EntitySystem.Instance == null || !EntitySystem.Instance.IsInitialized)
                BuildEntitiesInternal();

            CacheTargetCells();
            RebuildTerrainVisualsInternal();
        }
        else
        {
            BuildMeshInternal();
        }
    }

    [Button("Restart Level", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void RestartLevel()
    {
        PlayLevel(new LevelPlayRequest
        {
            Level = _level != null ? _level : levelData,
            Config = _config != null ? _config : tileConfig,
            Mode = _playMode,
            StepLimit = _remainingSteps > 0 ? _remainingSteps : defaultStepLimit
        });
    }

    public void BuildEntities()
    {
        if (_level == null && !LoadConfiguredLevel())
            return;

        BuildEntitiesInternal();
        CacheTargetCells();
    }

    [Button("Clear Mesh", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void ClearMesh()
    {
        int removedCount = ClearLevelMeshObjects();
        DisableInstancedTerrainRenderer();
        if (removedCount == 0)
        {
            Debug.LogWarning("[LevelPlayer] 没有可清除的 LevelMesh。Instanced terrain renderer was disabled if present.");
            return;
        }

        Debug.Log($"[LevelPlayer] Mesh 已清除，数量={removedCount}");
    }

    internal bool AreAllBoxesOnTargets()
    {
        if (_targetCells.Count == 0)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var entities = entitySystem.entities;
        bool hasBox = false;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box)
                continue;

            hasBox = true;
            if (!_targetCells.Contains(entities.coreComponents[i].Position))
                return false;
        }

        return hasBox;
    }

    internal bool IsAnyCoreBoxAlive()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType == EntityType.Box &&
                entities.propertyComponents[i].IsCore)
            {
                return true;
            }
        }

        return false;
    }

    internal bool IsAnyCoreBoxOnTarget()
    {
        if (_coreTargetCells.Count == 0)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box ||
                !entities.propertyComponents[i].IsCore)
            {
                continue;
            }

            if (_coreTargetCells.Contains(entities.coreComponents[i].Position))
                return true;
        }

        return false;
    }

    internal void SetRemainingSteps(int value)
    {
        _remainingSteps = value;
    }

    internal void SettleLevel(LevelPlayResult result, string reason)
    {
        if (_isSettled)
            return;

        _isSettled = true;
        _lastResult = result;
        StopPlayback();

        Debug.Log($"[LevelPlayer] Level settled: result={result}, reason={reason}");

        var stageFacade = NekoGraph.GraphHub.Instance?.GetFacade<RunStageFacade>();
        if (stageFacade != null && stageFacade.HasWaitingStage())
            stageFacade.ResumeWaitingStage(result == LevelPlayResult.Success ? 0 : 1);

        GameFlowController.Instance?.OnRouteClassicLevelSettled(result);
    }

    private void BuildMeshInternal()
    {
        DisableInstancedTerrainRenderer();
        Debug.Log($"[LevelPlayer] 开始构建: {_level.levelName}, size={_level.width}x{_level.height}, wallHeight={wallHeight}");

        ClearLevelMeshObjects();

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

        _meshGO = new GameObject(LevelMeshObjectName);
        _meshGO.transform.SetParent(transform);
        _meshGO.AddComponent<MeshFilter>().mesh = mesh;

        EnsureLevelMaterial();
        _meshGO.AddComponent<MeshRenderer>().sharedMaterial = _materialInstance;

        var camCtrl = Camera.main?.GetComponent<CameraController>();
        if (camCtrl != null)
        {
            camCtrl.SetMapBounds(_level.width, _level.height);
            camCtrl.ResetView();
        }

        Debug.Log($"[LevelPlayer] {_level.levelName} 已构建, 顶点={mesh.vertexCount}, wallHeight={wallHeight}");
    }

    private void RebuildTerrainVisualsInternal()
    {
        EnsureLevelMaterial();

        if (!useInstancedTerrain)
        {
            DisableInstancedTerrainRenderer();
            BuildMeshInternal();
            return;
        }

        ClearLevelMeshObjects();

        var terrainDrawSystem = EnsureRuntimeSystem<TerrainDrawSystem>();
        terrainDrawSystem.enabled = true;
        terrainDrawSystem.Configure(
            _config,
            _materialInstance,
            cellSize,
            wallHeight,
            tagMarkerSize,
            tagYOffset,
            GetLevelMarkerTags());

        var overlayDrawSystem = GridOverlayDrawSystem.Instance != null
            ? GridOverlayDrawSystem.Instance
            : EnsureRuntimeSystem<GridOverlayDrawSystem>();
        overlayDrawSystem.ConfigureSurfaceHeights(wallHeight);

        var camCtrl = Camera.main?.GetComponent<CameraController>();
        if (camCtrl != null)
        {
            camCtrl.SetMapBounds(_level.width, _level.height);
            camCtrl.ResetView();
        }

        Debug.Log($"[LevelPlayer] Instanced terrain ready: {_level.levelName}, size={_level.width}x{_level.height}");
    }

    private void DisableInstancedTerrainRenderer()
    {
        var terrainDrawSystem = GetComponent<TerrainDrawSystem>();
        if (terrainDrawSystem == null)
            return;

        terrainDrawSystem.Clear();
        terrainDrawSystem.enabled = false;
    }

    private void BuildEntitiesInternal()
    {
        EnsureLevelMaterial();

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null)
            entitySystem = gameObject.AddComponent<EntitySystem>();

        EnsureRuntimeSystem<IntentSystem>();
        EnsureRuntimeSystem<MoveSystem>();
        EnsureRuntimeSystem<AttackSystem>();
        var cardEffectSystem = EnsureRuntimeSystem<CardEffectSystem>();
        EnsureRuntimeSystem<EnemyAutoAISystem>();
        var drawSystem = EnsureRuntimeSystem<DrawSystem>();
        EnsureRuntimeSystem<UserInputReader>();
        drawSystem.SetWallMaterial(_materialInstance);

        entitySystem.Initialize(maxEntityCount, _level.width, _level.height);
        IntentSystem.Instance?.Clear();
        entitySystem.SetTerrain(_level.GetMap2D());
        entitySystem.SetWallTerrainIds(GetWallTerrainIds(), GetDefaultFloorTerrainId());

        int playerTagID = ResolveTagID("player", DefaultPlayerTagID);
        int boxTagID = ResolveTagID("box", DefaultBoxTagID);
        int boxCoreTagID = ResolveTagID("Box.Core", -1);
        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetCoreTagID = ResolveTagID("Target.Core", DefaultTargetCoreTagID);
        int enemyGoTagID = ResolveTagID("Enemy.Go", 4);
        int unstableWallTagID = ResolveTagID("Wall.Unstable", -1);
        ConfigureCardWallHealth(cardEffectSystem, unstableWallTagID);

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
            else if (targetCoreTagID > 0 && tag.tagID == targetCoreTagID)
                CreateTaggedEntity(entitySystem, EntityType.Target, pos, tag.tagID, false);
            else if (tag.tagID == enemyGoTagID)
                CreateTaggedEntity(entitySystem, EntityType.Enemy, pos, tag.tagID);
            else if (unstableWallTagID > 0 && tag.tagID == unstableWallTagID)
                CreateUnstableWall(entitySystem, pos, tag.tagID);
        }

        Debug.Log($"[LevelPlayer] ECS 已初始化，实体数={entitySystem.entities.entityCount}");
    }

    private void HandleTick()
    {
        if (!_isPlaying || _isSettled || _playRule == null)
            return;

        _playRule.OnTick(this);
        _playRule.Evaluate(this);
    }

    private ILevelPlayRule CreatePlayRule(LevelPlayMode mode)
    {
        return mode switch
        {
            LevelPlayMode.StepLimit => new StepLimitPlayRule(),
            LevelPlayMode.Escort => new EscortPlayRule(),
            _ => new ClassicPlayRule()
        };
    }

    private void EnsureStepLimitStyles()
    {
        _stepLimitStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.92f, 0.45f, 1f) }
        };

        _stepLimitShadowStyle ??= new GUIStyle(_stepLimitStyle)
        {
            normal = { textColor = new Color(0f, 0f, 0f, 0.72f) }
        };
    }

    private T EnsureRuntimeSystem<T>() where T : Component
    {
        var system = GetComponent<T>();
        if (system == null)
            system = gameObject.AddComponent<T>();
        return system;
    }

    private int ClearLevelMeshObjects()
    {
        int removedCount = 0;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null)
                continue;

            if (!string.Equals(child.name, LevelMeshObjectName, System.StringComparison.Ordinal))
                continue;

            CaptureReusableMaterial(child.gameObject);
            DestroyUnityObjectImmediate(child.gameObject);
            removedCount++;
        }

        if (_meshGO != null)
        {
            CaptureReusableMaterial(_meshGO);
            DestroyUnityObjectImmediate(_meshGO);
            removedCount++;
        }

        _meshGO = null;
        return removedCount;
    }

    private void CaptureReusableMaterial(GameObject meshObject)
    {
        if (meshObject == null || material != null)
            return;

        var mr = meshObject.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null)
            _materialInstance = mr.sharedMaterial;
    }

    private static void DestroyUnityObjectImmediate(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        DestroyImmediate(obj);
    }

    private void EnsureLevelMaterial()
    {
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

    private void ConfigureCardWallHealth(CardEffectSystem cardEffectSystem, int unstableWallTagID)
    {
        if (cardEffectSystem == null || unstableWallTagID <= 0 || _config == null)
            return;

        EntityBP wallBP = _config.GetTagEntityBP(unstableWallTagID);
        if (wallBP == null)
            return;

        cardEffectSystem.ConfigureMaterializedTerrainWallHealth(wallBP.health);
    }

    private List<LevelTagEntry> GetLevelMarkerTags()
    {
        var result = new List<LevelTagEntry>();
        if (_level?.tags == null)
            return result;

        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetCoreTagID = ResolveTagID("Target.Core", DefaultTargetCoreTagID);
        foreach (var tag in _level.tags)
        {
            if (tag.tagID == targetTagID || tag.tagID == targetCoreTagID)
                result.Add(tag);
        }

        return result;
    }

    private void CacheTargetCells()
    {
        _targetCells.Clear();
        _coreTargetCells.Clear();

        if (_level?.tags == null)
            return;

        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetCoreTagID = ResolveTagID("Target.Core", DefaultTargetCoreTagID);
        foreach (var tag in _level.tags)
        {
            if (tag.tagID == targetTagID)
                _targetCells.Add(new Vector2Int(tag.x, tag.y));
            else if (tag.tagID == targetCoreTagID)
                _coreTargetCells.Add(new Vector2Int(tag.x, tag.y));
        }
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

    private interface ILevelPlayRule
    {
        void Begin(LevelPlayer player, LevelPlayRequest request);
        void OnTick(LevelPlayer player);
        void Evaluate(LevelPlayer player);
    }

    private sealed class ClassicPlayRule : ILevelPlayRule
    {
        public void Begin(LevelPlayer player, LevelPlayRequest request)
        {
            player.SetRemainingSteps(-1);
        }

        public void OnTick(LevelPlayer player)
        {
        }

        public void Evaluate(LevelPlayer player)
        {
            if (player.AreAllBoxesOnTargets())
                player.SettleLevel(LevelPlayResult.Success, "all boxes are on targets");
        }
    }

    private sealed class StepLimitPlayRule : ILevelPlayRule
    {
        public void Begin(LevelPlayer player, LevelPlayRequest request)
        {
            int stepLimit = request != null ? request.StepLimit : 0;
            player.SetRemainingSteps(Mathf.Max(1, stepLimit));
        }

        public void OnTick(LevelPlayer player)
        {
            player.SetRemainingSteps(Mathf.Max(0, player.RemainingSteps - 1));
        }

        public void Evaluate(LevelPlayer player)
        {
            if (player.AreAllBoxesOnTargets())
            {
                player.SettleLevel(LevelPlayResult.Success, "all boxes are on targets");
                return;
            }

            if (player.RemainingSteps <= 0)
                player.SettleLevel(LevelPlayResult.Failure, "step limit reached");
        }
    }

    private sealed class EscortPlayRule : ILevelPlayRule
    {
        public void Begin(LevelPlayer player, LevelPlayRequest request)
        {
            player.SetRemainingSteps(-1);
        }

        public void OnTick(LevelPlayer player)
        {
        }

        public void Evaluate(LevelPlayer player)
        {
            if (player.IsAnyCoreBoxOnTarget())
            {
                player.SettleLevel(LevelPlayResult.Success, "core box reached a target");
                return;
            }

            if (!player.IsAnyCoreBoxAlive())
                player.SettleLevel(LevelPlayResult.Failure, "core box destroyed");
        }
    }
}
