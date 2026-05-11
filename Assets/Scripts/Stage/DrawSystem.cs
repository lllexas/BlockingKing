using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 读取实体数据并绘制单位。不参与逻辑 Tick。
/// </summary>
public class DrawSystem : MonoBehaviour
{
    public static DrawSystem Instance { get; private set; }

    private const int BatchSize = 1023;
    private const int EnemyVisualKindCount = 7;

    private enum EnemyVisualKind
    {
        Go = 0,
        Grenadier = 1,
        Crossbow = 2,
        Artillery = 3,
        CurseCaster = 4,
        Guokui = 5,
        Ertong = 6
    }

    [Header("Mesh")]
    [SerializeField] private Mesh playerMesh;
    [SerializeField] private Mesh boxMesh;
    [SerializeField] private Mesh enemyMesh;
    [SerializeField] private Mesh wallMesh;

    [Header("Material")]
    [SerializeField] private Material playerMaterial;
    [SerializeField] private Material boxMaterial;
    [SerializeField] private Material coreBoxMaterial;
    [SerializeField] private Material boxGlassMaterial;
    [SerializeField] private Material coreBoxGlassMaterial;
    [SerializeField] private Material enemyMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material unitFallbackMaterial;
    [SerializeField] private Material boxGlassFallbackMaterial;

    [Header("Enemy Colors")]
    [SerializeField] private Color enemyGoColor = new(0.92f, 0.88f, 0.78f);
    [SerializeField] private Color enemyGrenadierColor = new(0.95f, 0.45f, 0.18f);
    [SerializeField] private Color enemyCrossbowColor = new(0.35f, 0.72f, 1f);
    [SerializeField] private Color enemyArtilleryColor = new(0.74f, 0.42f, 1f);
    [SerializeField] private Color enemyCurseCasterColor = new(0.45f, 0.14f, 0.95f);
    [SerializeField] private Color enemyGuokuiColor = new(0.85f, 0.85f, 0.28f);
    [SerializeField] private Color enemyErtongColor = new(0.95f, 0.2f, 0.58f);

    [Header("Transform")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector3 playerScale = new(0.7f, 1f, 0.7f);
    [SerializeField] private Vector3 boxScale = new(0.45f, 0.45f, 0.45f);
    [SerializeField] private Vector3 boxGlassScale = new(0.9f, 0.9f, 0.9f);
    [SerializeField] private Vector3 enemyScale = new(0.75f, 0.35f, 0.75f);
    [SerializeField] private Vector3 wallScale = new(0.95f, 0.95f, 0.95f);
    [SerializeField] private float wallY = -0.2f;

    [Header("Beat Motion")]
    [SerializeField] private bool enableBeatMotion = true;
    [SerializeField, Min(0.03f)] private float beatDuration = 0.16f;
    [SerializeField, Range(0.05f, 1f)] private float movementPortion = 0.86f;
    [SerializeField, Range(0.05f, 1f)] private float attackPortion = 0.72f;
    [SerializeField, Range(0f, 0.5f)] private float attackLungeDistance = 0.28f;
    [SerializeField, Range(0f, 1.5f)] private float spawnRiseDistance = 0.75f;

    [Header("Stats Text")]
    [SerializeField] public bool showStatsText = true;
    [SerializeField] private float statsTextHeight = 0.01f;
    [SerializeField] private float statsTextScale = 1f;
    [SerializeField] private float statsTextFontSize = 3f;
    [SerializeField] private Vector2 statsTextRectSize = new(0.35f, 0.25f);
    [SerializeField] private TMP_FontAsset statsFont;
    [SerializeField] private Material statsTextMaterial;
    [SerializeField, Range(0f, 1f)] private float statsOccludedGrayMix = 0.72f;
    [SerializeField, Range(0f, 1f)] private float statsOccludedAlpha = 0.78f;
    [SerializeField, Range(0f, 0.5f)] private float statsOccludedOutlineWidth = 0.18f;
    [SerializeField, Range(0f, 1f)] private float statsOccludedOutlineAlpha = 0.9f;
    [SerializeField] private Color statsOccludedOutlineColor = new(0.08f, 0.08f, 0.08f, 1f);
    [SerializeField] private Color attackTextColor = new(0.95f, 0.35f, 0.25f);
    [SerializeField] private Color healthTextColor = new(0.35f, 0.9f, 0.45f);
    [SerializeField] private Color countdownTextColor = new(1f, 0.16f, 0.08f);

    private readonly Matrix4x4[] _playerMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _boxMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _coreBoxMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _boxGlassMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _coreBoxGlassMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[][] _enemyMatricesByKind = new Matrix4x4[EnemyVisualKindCount][];
    private readonly Matrix4x4[] _wallMatrices = new Matrix4x4[BatchSize];
    private readonly List<StatTextPair> _statTexts = new();
    private int _playerCount;
    private int _boxCount;
    private int _coreBoxCount;
    private int _boxGlassCount;
    private int _coreBoxGlassCount;
    private readonly int[] _enemyCountsByKind = new int[EnemyVisualKindCount];
    private int _wallCount;
    private int _statTextCount;
    private Material _runtimeStatsVisibleMaterial;
    private Material _runtimeStatsOccludedMaterial;
    private Material _runtimeStatsMaterialSource;
    private readonly Material[] _enemyMaterialsByKind = new Material[EnemyVisualKindCount];
    private TMP_FontAsset _runtimeStatsTextFont;
    private EventBusSystem _registeredBus;
    private float _nextBeatStartTime;
    private float _activeIntentStartTime;
    private float _activeIntentEndTime;
    private int _activeIntentSlotId;
    private bool _hasIntentContext;
    private bool _hasBatchContext;
    private bool _hasActiveIntentSlot;
    private bool _loggedEntitySystemUnavailable;
    private bool _loggedEmptyEntities;
    private bool _loggedStatsFontUnavailable;

    private void Awake()
    {
        Instance = this;
        EnsureResources();
    }

    private void OnDestroy()
    {
        UnregisterEventBus();

        if (_runtimeStatsVisibleMaterial != null)
        {
            Destroy(_runtimeStatsVisibleMaterial);
            _runtimeStatsVisibleMaterial = null;
        }

        if (_runtimeStatsOccludedMaterial != null)
        {
            Destroy(_runtimeStatsOccludedMaterial);
            _runtimeStatsOccludedMaterial = null;
        }

        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        TryRegisterEventBus();
    }

    private void OnDisable()
    {
        UnregisterEventBus();
    }

    private void LateUpdate()
    {
        TryRegisterEventBus();
        UpdateStatsMaterialProperties();
        DrawEntities();
    }

    public bool IsBeatMotionBusy => enableBeatMotion && Time.time < _nextBeatStartTime;

    public float BeatMotionBusyUntil => enableBeatMotion ? _nextBeatStartTime : 0f;

    public void SetWallMaterial(Material material)
    {
        if (material == null)
            return;

        wallMaterial = material;
        wallMaterial.enableInstancing = true;
    }

    private void DrawEntities()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
        {
            if (!_loggedEntitySystemUnavailable)
            {
                Debug.LogWarning("[DrawSystem] EntitySystem is unavailable or not initialized. Entity meshes and stats text will not draw.");
                _loggedEntitySystemUnavailable = true;
            }

            return;
        }

        _playerCount = 0;
        _boxCount = 0;
        _coreBoxCount = 0;
        _boxGlassCount = 0;
        _coreBoxGlassCount = 0;
        ResetEnemyCounts();
        _wallCount = 0;

        var entities = entitySystem.entities;
        if (entities.entityCount <= 0)
        {
            if (!_loggedEmptyEntities)
            {
                Debug.LogWarning("[DrawSystem] EntitySystem has no entities. Entity meshes and stats text will not draw.");
                _loggedEmptyEntities = true;
            }

            HideUnusedStatTexts();
            return;
        }

        _loggedEntitySystemUnavailable = false;
        _loggedEmptyEntities = false;

        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            switch (core.EntityType)
            {
                case EntityType.Player:
                    AddPlayer(i, core.Position);
                    AddStatsText(i, core.EntityType, core.Position, CombatStats.GetAttack(entities.statusComponents[i]), CombatStats.GetCurrentHealth(entities.statusComponents[i]));
                    break;
                case EntityType.Box:
                    AddBox(i, core.Position, entities.propertyComponents[i].IsCore);
                    if (entities.propertyComponents[i].IsCore)
                        AddStatsText(i, core.EntityType, core.Position, CombatStats.GetAttack(entities.statusComponents[i]), CombatStats.GetCurrentHealth(entities.statusComponents[i]));
                    break;
                case EntityType.Enemy:
                    AddEnemy(i, core.Position, entities.propertyComponents[i]);
                    AddStatsText(i, core.EntityType, core.Position, CombatStats.GetAttack(entities.statusComponents[i]), CombatStats.GetCurrentHealth(entities.statusComponents[i]));
                    break;
                case EntityType.Wall:
                    AddWall(i, core.Position);
                    AddStatsText(i, core.EntityType, core.Position, CombatStats.GetAttack(entities.statusComponents[i]), CombatStats.GetCurrentHealth(entities.statusComponents[i]));
                    break;
                case EntityType.Target:
                    if (entities.counterComponents[i].NextTick > 0)
                        AddCountdownText(i, core.EntityType, core.Position, Mathf.Max(0, entities.counterComponents[i].NextTick - entitySystem.GlobalTick));
                    break;
            }
        }

        FlushPlayers();
        FlushBoxes();
        FlushCoreBoxes();
        FlushBoxGlass();
        FlushCoreBoxGlass();
        FlushEnemies();
        FlushWalls();
        HideUnusedStatTexts();
    }

    private void AddPlayer(int entityIndex, Vector2Int gridPos)
    {
        _playerMatrices[_playerCount++] = Matrix4x4.TRS(GetVisualPosition(entityIndex, EntityType.Player, gridPos), Quaternion.identity, playerScale);
        if (_playerCount == BatchSize)
            FlushPlayers();
    }

    private void AddBox(int entityIndex, Vector2Int gridPos, bool isCore)
    {
        Vector3 position = GetVisualPosition(entityIndex, EntityType.Box, gridPos);
        if (isCore)
        {
            _coreBoxMatrices[_coreBoxCount++] = Matrix4x4.TRS(position, Quaternion.identity, boxScale);
            _coreBoxGlassMatrices[_coreBoxGlassCount++] = Matrix4x4.TRS(position, Quaternion.identity, boxGlassScale);
            if (_coreBoxCount == BatchSize)
                FlushCoreBoxes();
            if (_coreBoxGlassCount == BatchSize)
                FlushCoreBoxGlass();
            return;
        }

        _boxMatrices[_boxCount++] = Matrix4x4.TRS(position, Quaternion.identity, boxScale);
        _boxGlassMatrices[_boxGlassCount++] = Matrix4x4.TRS(position, Quaternion.identity, boxGlassScale);
        if (_boxCount == BatchSize)
            FlushBoxes();
        if (_boxGlassCount == BatchSize)
            FlushBoxGlass();
    }

    private void AddEnemy(int entityIndex, Vector2Int gridPos, PropertyComponent properties)
    {
        int kind = (int)ResolveEnemyVisualKind(properties);
        _enemyMatricesByKind[kind][_enemyCountsByKind[kind]++] = Matrix4x4.TRS(GetVisualPosition(entityIndex, EntityType.Enemy, gridPos), Quaternion.identity, enemyScale);
        if (_enemyCountsByKind[kind] == BatchSize)
            FlushEnemyKind(kind);
    }

    private void AddWall(int entityIndex, Vector2Int gridPos)
    {
        _wallMatrices[_wallCount++] = Matrix4x4.TRS(GetVisualPosition(entityIndex, EntityType.Wall, gridPos), Quaternion.identity, wallScale);
        if (_wallCount == BatchSize)
            FlushWalls();
    }

    private void FlushPlayers()
    {
        if (_playerCount == 0)
            return;

        if (playerMesh == null || playerMaterial == null)
        {
            _playerCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(playerMesh, 0, playerMaterial, _playerMatrices, _playerCount);
        _playerCount = 0;
    }

    private void FlushBoxes()
    {
        if (_boxCount == 0)
            return;

        if (boxMesh == null || boxMaterial == null)
        {
            _boxCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(boxMesh, 0, boxMaterial, _boxMatrices, _boxCount);
        _boxCount = 0;
    }

    private void FlushCoreBoxes()
    {
        if (_coreBoxCount == 0)
            return;

        if (boxMesh == null || coreBoxMaterial == null)
        {
            _coreBoxCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(boxMesh, 0, coreBoxMaterial, _coreBoxMatrices, _coreBoxCount);
        _coreBoxCount = 0;
    }

    private void FlushBoxGlass()
    {
        if (_boxGlassCount == 0)
            return;

        if (boxMesh == null || boxGlassMaterial == null)
        {
            _boxGlassCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(boxMesh, 0, boxGlassMaterial, _boxGlassMatrices, _boxGlassCount);
        _boxGlassCount = 0;
    }

    private void FlushCoreBoxGlass()
    {
        if (_coreBoxGlassCount == 0)
            return;

        if (boxMesh == null || coreBoxGlassMaterial == null)
        {
            _coreBoxGlassCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(boxMesh, 0, coreBoxGlassMaterial, _coreBoxGlassMatrices, _coreBoxGlassCount);
        _coreBoxGlassCount = 0;
    }

    private void FlushEnemies()
    {
        for (int i = 0; i < EnemyVisualKindCount; i++)
            FlushEnemyKind(i);
    }

    private void FlushEnemyKind(int kind)
    {
        int count = _enemyCountsByKind[kind];
        if (count == 0)
            return;

        var material = kind >= 0 && kind < _enemyMaterialsByKind.Length ? _enemyMaterialsByKind[kind] : enemyMaterial;
        if (enemyMesh == null || material == null)
        {
            _enemyCountsByKind[kind] = 0;
            return;
        }

        Graphics.DrawMeshInstanced(enemyMesh, 0, material, _enemyMatricesByKind[kind], count);
        _enemyCountsByKind[kind] = 0;
    }

    private void FlushWalls()
    {
        if (_wallCount == 0)
            return;

        if (wallMesh == null || wallMaterial == null)
        {
            _wallCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(wallMesh, 0, wallMaterial, _wallMatrices, _wallCount);
        _wallCount = 0;
    }

    private void AddStatsText(int entityIndex, EntityType entityType, Vector2Int gridPos, int attack, int health)
    {
        if (!showStatsText)
            return;

        var pair = GetStatTextPair(_statTextCount++);
        pair.Root.SetActive(true);
        pair.SetAttackVisible(attack > 0);
        pair.SetHealthVisible(true);
        pair.SetCountdownVisible(false);
        if (attack > 0)
            pair.SetAttackText(attack.ToString());

        pair.SetHealthText(health.ToString());

        Vector3 visualPosition = GetVisualPosition(entityIndex, entityType, gridPos);
        float y = statsTextHeight * cellSize;
        pair.AttackRoot.transform.position = new Vector3(visualPosition.x - 0.5f * cellSize, y, visualPosition.z - 0.5f * cellSize);
        pair.HealthRoot.transform.position = new Vector3(visualPosition.x + 0.5f * cellSize, y, visualPosition.z - 0.5f * cellSize);
    }

    private void AddCountdownText(int entityIndex, EntityType entityType, Vector2Int gridPos, int remainingTicks)
    {
        if (!showStatsText)
            return;

        var pair = GetStatTextPair(_statTextCount++);
        pair.Root.SetActive(true);
        pair.SetAttackVisible(false);
        pair.SetHealthVisible(false);
        pair.SetCountdownVisible(true);
        pair.SetCountdownText(remainingTicks.ToString());

        Vector3 visualPosition = GetVisualPosition(entityIndex, entityType, gridPos);
        float y = statsTextHeight * cellSize;
        pair.CountdownRoot.transform.position = new Vector3(visualPosition.x + 0.5f * cellSize, y, visualPosition.z + 0.5f * cellSize);
    }

    private StatTextPair GetStatTextPair(int index)
    {
        while (_statTexts.Count <= index)
            _statTexts.Add(CreateStatTextPair(_statTexts.Count));

        return _statTexts[index];
    }

    private StatTextPair CreateStatTextPair(int index)
    {
        var root = new GameObject($"StatText_{index:000}");
        root.transform.SetParent(transform, false);

        var attack = CreateStatTextStack(root.transform, "Attack", attackTextColor);
        var health = CreateStatTextStack(root.transform, "Health", healthTextColor);
        var countdown = CreateStatTextStack(root.transform, "Countdown", countdownTextColor);
        return new StatTextPair(root, attack, health, countdown);
    }

    private StatTextStack CreateStatTextStack(Transform parent, string name, Color color)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        root.transform.localScale = Vector3.one * statsTextScale;

        var visible = CreateStatText(root.transform, "Visible", color, ResolveStatsVisibleMaterial());
        var occluded = CreateStatText(root.transform, "Occluded", color, ResolveStatsOccludedMaterial());
        return new StatTextStack(root, visible, occluded);
    }

    private TextMeshPro CreateStatText(Transform parent, string name, Color color, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var text = go.AddComponent<TextMeshPro>();
        text.font = ResolveStatsFont();
        text.fontSharedMaterial = material;
        text.alignment = parent.name switch
        {
            "Attack" => TextAlignmentOptions.BottomLeft,
            "Countdown" => TextAlignmentOptions.TopRight,
            _ => TextAlignmentOptions.BottomRight
        };
        text.fontSize = statsTextFontSize;
        text.color = color;
        text.enableWordWrapping = false;
        text.text = "0";

        RectTransform rectTransform = text.rectTransform;
        rectTransform.pivot = parent.name switch
        {
            "Attack" => Vector2.zero,
            "Countdown" => new Vector2(1f, 1f),
            _ => new Vector2(1f, 0f)
        };
        rectTransform.sizeDelta = statsTextRectSize;
        return text;
    }

    private TMP_FontAsset ResolveStatsFont()
    {
        if (statsFont != null)
            return statsFont;

        return TMP_Settings.defaultFontAsset;
    }

    private Material ResolveStatsVisibleMaterial()
    {
        EnsureStatsMaterials();
        return _runtimeStatsVisibleMaterial;
    }

    private Material ResolveStatsOccludedMaterial()
    {
        EnsureStatsMaterials();
        return _runtimeStatsOccludedMaterial;
    }

    private void EnsureStatsMaterials()
    {
        var font = ResolveStatsFont();
        if (font == null || font.atlasTexture == null)
        {
            if (!_loggedStatsFontUnavailable)
            {
                Debug.LogWarning("[DrawSystem] TMP font is unavailable. Stats text will not draw. Assign Stats Font or configure TMP Settings default font.");
                _loggedStatsFontUnavailable = true;
            }

            return;
        }

        _loggedStatsFontUnavailable = false;

        if (_runtimeStatsVisibleMaterial != null
            && _runtimeStatsOccludedMaterial != null
            && _runtimeStatsMaterialSource == statsTextMaterial
            && _runtimeStatsTextFont == font)
            return;

        Material source = statsTextMaterial;
        if (source == null)
        {
            var shader = Shader.Find("BlockingKing/TMP Ground Stats");
            if (shader == null)
            {
                _runtimeStatsVisibleMaterial = font.material;
                _runtimeStatsOccludedMaterial = font.material;
                return;
            }

            source = new Material(shader)
            {
                name = "Runtime_TMP_GroundStats_Template"
            };
        }

        if (_runtimeStatsVisibleMaterial != null && _runtimeStatsVisibleMaterial != font.material)
            Destroy(_runtimeStatsVisibleMaterial);
        if (_runtimeStatsOccludedMaterial != null && _runtimeStatsOccludedMaterial != font.material)
            Destroy(_runtimeStatsOccludedMaterial);

        _runtimeStatsVisibleMaterial = new Material(source)
        {
            name = "Runtime_TMP_GroundStats_Visible"
        };
        _runtimeStatsOccludedMaterial = new Material(source)
        {
            name = "Runtime_TMP_GroundStats_Occluded"
        };

        ConfigureStatsMaterial(_runtimeStatsVisibleMaterial, font, 4f, 0f, 1f);
        ConfigureStatsMaterial(_runtimeStatsOccludedMaterial, font, 5f, statsOccludedGrayMix, statsOccludedAlpha);
        _runtimeStatsVisibleMaterial.renderQueue = 3201;
        _runtimeStatsOccludedMaterial.renderQueue = 3200;
        _runtimeStatsVisibleMaterial.SetFloat("_OutlineWidth", 0f);
        _runtimeStatsVisibleMaterial.SetFloat("_OutlineAlpha", 0f);
        UpdateStatsMaterialProperties();
        _runtimeStatsMaterialSource = statsTextMaterial;
        _runtimeStatsTextFont = font;
    }

    private void UpdateStatsMaterialProperties()
    {
        if (_runtimeStatsOccludedMaterial == null)
            return;

        _runtimeStatsOccludedMaterial.SetFloat("_GrayMix", statsOccludedGrayMix);
        _runtimeStatsOccludedMaterial.SetFloat("_AlphaScale", statsOccludedAlpha);
        _runtimeStatsOccludedMaterial.SetFloat("_OutlineWidth", statsOccludedOutlineWidth);
        _runtimeStatsOccludedMaterial.SetFloat("_OutlineAlpha", statsOccludedOutlineAlpha);
        _runtimeStatsOccludedMaterial.SetColor("_OutlineColor", statsOccludedOutlineColor);
    }

    private static void ConfigureStatsMaterial(Material material, TMP_FontAsset font, float zTest, float grayMix, float alphaScale)
    {
        material.SetTexture("_MainTex", font.atlasTexture);
        material.SetFloat("_ZTest", zTest);
        material.SetFloat("_GrayMix", grayMix);
        material.SetFloat("_AlphaScale", alphaScale);
    }

    private void HideUnusedStatTexts()
    {
        for (int i = _statTextCount; i < _statTexts.Count; i++)
            _statTexts[i].Root.SetActive(false);

        _statTextCount = 0;
    }

    private Vector3 ToWorld(Vector2Int gridPos)
    {
        return new Vector3((gridPos.x + 0.5f) * cellSize, 0.5f * cellSize, (gridPos.y + 0.5f) * cellSize);
    }

    private Vector3 ToEnemyWorld(Vector2Int gridPos)
    {
        return new Vector3((gridPos.x + 0.5f) * cellSize, 0.25f * cellSize, (gridPos.y + 0.5f) * cellSize);
    }

    private Vector3 ToWallWorld(Vector2Int gridPos)
    {
        return new Vector3((gridPos.x + 0.5f) * cellSize, wallY * cellSize, (gridPos.y + 0.5f) * cellSize);
    }

    private Vector3 ToEntityWorld(EntityType entityType, Vector2Int gridPos)
    {
        return entityType switch
        {
            EntityType.Enemy => ToEnemyWorld(gridPos),
            EntityType.Wall => ToWallWorld(gridPos),
            _ => ToWorld(gridPos)
        };
    }

    private Vector3 GetVisualPosition(int entityIndex, EntityType entityType, Vector2Int gridPos)
    {
        Vector3 logicalPosition = ToEntityWorld(entityType, gridPos);
        if (!enableBeatMotion)
            return logicalPosition;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null
            || entitySystem.entities == null
            || entitySystem.entities.visualMotionComponents == null
            || entitySystem.entities.visualImpulseComponents == null
            || entityIndex < 0
            || entityIndex >= entitySystem.entities.entityCount)
            return logicalPosition;

        ref var motion = ref entitySystem.entities.visualMotionComponents[entityIndex];
        float now = Time.time;
        Vector3 position = motion.Type == VisualMotionType.None
            ? logicalPosition
            : motion.Evaluate(now, logicalPosition);
        if (motion.Type != VisualMotionType.None && motion.IsComplete(now))
            motion.Clear();

        ref var impulse = ref entitySystem.entities.visualImpulseComponents[entityIndex];
        if (impulse.Type == VisualImpulseType.None)
            return position;

        Vector3 offset = impulse.Evaluate(now);
        if (impulse.IsComplete(now))
            impulse.Clear();

        return position + offset;
    }

    private void TryRegisterEventBus()
    {
        if (_registeredBus != null)
            return;

        var bus = EventBusSystem.Instance;
        if (bus == null)
            return;

        _registeredBus = bus;
        _registeredBus.On(StageEventType.BeforeIntentExecute, OnStageEvent, 100);
        _registeredBus.On(StageEventType.AfterIntentExecute, OnStageEvent, -100);
        _registeredBus.On(StageEventType.EntityMoved, OnStageEvent);
        _registeredBus.On(StageEventType.EntityCreated, OnStageEvent);
        _registeredBus.On(StageEventType.EntityDestroyed, OnStageEvent);
        _registeredBus.On(StageEventType.PresentationBatchBegin, OnStageEvent, 100);
        _registeredBus.On(StageEventType.PresentationBatchEnd, OnStageEvent, -100);
        _registeredBus.On(StageEventType.PresentationBeat, OnStageEvent, 100);
    }

    private void UnregisterEventBus()
    {
        if (_registeredBus == null)
            return;

        _registeredBus.Off(StageEventType.BeforeIntentExecute, OnStageEvent);
        _registeredBus.Off(StageEventType.AfterIntentExecute, OnStageEvent);
        _registeredBus.Off(StageEventType.EntityMoved, OnStageEvent);
        _registeredBus.Off(StageEventType.EntityCreated, OnStageEvent);
        _registeredBus.Off(StageEventType.EntityDestroyed, OnStageEvent);
        _registeredBus.Off(StageEventType.PresentationBatchBegin, OnStageEvent);
        _registeredBus.Off(StageEventType.PresentationBatchEnd, OnStageEvent);
        _registeredBus.Off(StageEventType.PresentationBeat, OnStageEvent);
        _registeredBus = null;
    }

    private void OnStageEvent(StageEvent evt)
    {
        if (!enableBeatMotion)
            return;

        switch (evt.Type)
        {
            case StageEventType.BeforeIntentExecute:
                if (!_hasBatchContext)
                {
                    _hasIntentContext = true;
                    _hasActiveIntentSlot = false;
                }
                TryScheduleAttack(evt);
                break;
            case StageEventType.AfterIntentExecute:
                if (!_hasBatchContext)
                {
                    _hasIntentContext = false;
                    _hasActiveIntentSlot = false;
                }
                break;
            case StageEventType.EntityMoved:
                ScheduleMove(evt);
                break;
            case StageEventType.EntityCreated:
                ScheduleSpawn(evt);
                break;
            case StageEventType.EntityDestroyed:
                ClearMotion(evt.Entity);
                break;
            case StageEventType.PresentationBatchBegin:
                _hasBatchContext = true;
                _hasIntentContext = true;
                _hasActiveIntentSlot = false;
                break;
            case StageEventType.PresentationBatchEnd:
                _hasBatchContext = false;
                _hasIntentContext = false;
                _hasActiveIntentSlot = false;
                break;
            case StageEventType.PresentationBeat:
                _hasIntentContext = true;
                _hasActiveIntentSlot = false;
                EnsureIntentBeat();
                _hasIntentContext = false;
                _hasActiveIntentSlot = false;
                break;
        }
    }

    private bool EnsureIntentBeat()
    {
        if (_hasActiveIntentSlot)
            return true;

        if (!_hasIntentContext)
            return false;

        float now = Time.time;
        if (_nextBeatStartTime < now)
            _nextBeatStartTime = now;

        _activeIntentStartTime = _nextBeatStartTime;
        _activeIntentEndTime = _activeIntentStartTime + beatDuration;
        _nextBeatStartTime = _activeIntentEndTime;
        _activeIntentSlotId++;
        _hasActiveIntentSlot = true;
        return true;
    }

    private void ScheduleMove(StageEvent evt)
    {
        if (!EnsureIntentBeat() || !TryGetMotion(evt.Entity, out var state))
            return;

        float start = _activeIntentStartTime;
        float end = Mathf.Lerp(_activeIntentStartTime, _activeIntentEndTime, movementPortion);
        Vector3 from = ToEntityWorld(evt.EntityType, evt.From);
        Vector3 to = ToEntityWorld(evt.EntityType, evt.To);

        if (state.SlotId == _activeIntentSlotId && state.Type == VisualMotionType.Move)
            from = state.From;

        state.Schedule(VisualMotionType.Move, _activeIntentSlotId, from, to, start, end);
    }

    private void TryScheduleAttack(StageEvent evt)
    {
        if (evt.IntentType != IntentType.Attack || evt.Intent is not AttackIntent attack || attack.TargetCount <= 0)
            return;

        if (!EnsureIntentBeat() || !TryGetImpulse(evt.Actor, out var impulse))
            return;

        Vector3 origin = ToEntityWorld(evt.EntityType, evt.From);
        Vector2Int targetCell = attack.TargetPositions[0];
        Vector3 target = ToEntityWorld(evt.EntityType, targetCell);
        Vector3 direction = target - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();
        Vector3 offset = direction * (attackLungeDistance * cellSize);
        float end = Mathf.Lerp(_activeIntentStartTime, _activeIntentEndTime, attackPortion);
        impulse.Schedule(VisualImpulseType.Lunge, _activeIntentSlotId, offset, _activeIntentStartTime, end);
    }

    private void ScheduleSpawn(StageEvent evt)
    {
        if (!EnsureIntentBeat() || !TryGetMotion(evt.Entity, out var motion))
            return;

        Vector3 to = ToEntityWorld(evt.EntityType, evt.To);
        Vector3 from = to + Vector3.down * (spawnRiseDistance * cellSize);
        motion.Schedule(VisualMotionType.Rise, _activeIntentSlotId, from, to, _activeIntentStartTime, _activeIntentEndTime);
    }

    private bool TryGetMotion(EntityHandle entity, out RefVisualMotion motion)
    {
        motion = default;
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(entity))
            return false;

        int index = entitySystem.GetIndex(entity);
        if (index < 0
            || entitySystem.entities == null
            || entitySystem.entities.visualMotionComponents == null
            || index >= entitySystem.entities.visualMotionComponents.Length)
            return false;

        motion = new RefVisualMotion(entitySystem.entities.visualMotionComponents, index);
        return true;
    }

    private void ClearMotion(EntityHandle entity)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(entity))
            return;

        int index = entitySystem.GetIndex(entity);
        if (index < 0
            || entitySystem.entities?.visualMotionComponents == null
            || entitySystem.entities.visualImpulseComponents == null
            || index >= entitySystem.entities.visualMotionComponents.Length)
            return;

        entitySystem.entities.visualMotionComponents[index].Clear();
        entitySystem.entities.visualImpulseComponents[index].Clear();
    }

    private bool TryGetImpulse(EntityHandle entity, out RefVisualImpulse impulse)
    {
        impulse = default;
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(entity))
            return false;

        int index = entitySystem.GetIndex(entity);
        if (index < 0
            || entitySystem.entities == null
            || entitySystem.entities.visualImpulseComponents == null
            || index >= entitySystem.entities.visualImpulseComponents.Length)
            return false;

        impulse = new RefVisualImpulse(entitySystem.entities.visualImpulseComponents, index);
        return true;
    }

    private readonly struct RefVisualMotion
    {
        private readonly VisualMotionComponent[] _components;
        private readonly int _index;

        public RefVisualMotion(VisualMotionComponent[] components, int index)
        {
            _components = components;
            _index = index;
        }

        public VisualMotionType Type => _components != null ? _components[_index].Type : VisualMotionType.None;
        public int SlotId => _components[_index].SlotId;
        public Vector3 From => _components[_index].From;

        public void Schedule(VisualMotionType type, int slotId, Vector3 from, Vector3 to, float start, float end)
        {
            _components[_index].Schedule(type, slotId, from, to, start, end);
        }
    }

    private readonly struct RefVisualImpulse
    {
        private readonly VisualImpulseComponent[] _components;
        private readonly int _index;

        public RefVisualImpulse(VisualImpulseComponent[] components, int index)
        {
            _components = components;
            _index = index;
        }

        public void Schedule(VisualImpulseType type, int slotId, Vector3 offset, float start, float end)
        {
            _components[_index].Schedule(type, slotId, offset, start, end);
        }
    }

    private void EnsureResources()
    {
        EnsureEnemyBuffers();

        if (playerMesh == null)
            playerMesh = CreatePrimitiveMesh(PrimitiveType.Capsule);
        if (boxMesh == null)
            boxMesh = CreatePrimitiveMesh(PrimitiveType.Cube);
        if (enemyMesh == null)
            enemyMesh = CreatePrimitiveMesh(PrimitiveType.Sphere);
        if (wallMesh == null)
            wallMesh = CreatePrimitiveMesh(PrimitiveType.Cube);

        EnsureUnitFallbackMaterial();
        EnsureBoxGlassFallbackMaterial();

        if (playerMaterial == null)
            playerMaterial = CreateUnitMaterial(new Color(0.2f, 0.55f, 1f));
        if (boxMaterial == null)
            boxMaterial = CreateUnitMaterial(new Color(0.9f, 0.65f, 0.25f));
        if (coreBoxMaterial == null)
            coreBoxMaterial = CreateUnitMaterial(new Color(0.12f, 0.55f, 1f));
        if (boxGlassMaterial == null)
            boxGlassMaterial = CreateGlassMaterial(new Color(1f, 0.75f, 0.22f, 1f));
        if (coreBoxGlassMaterial == null)
            coreBoxGlassMaterial = CreateGlassMaterial(new Color(0.12f, 0.7f, 1f, 1f));
        if (enemyMaterial == null)
            enemyMaterial = CreateUnitMaterial(enemyGoColor);
        EnsureEnemyMaterials();
        if (playerMaterial != null)
            playerMaterial.enableInstancing = true;
        if (boxMaterial != null)
            boxMaterial.enableInstancing = true;
        if (coreBoxMaterial != null)
            coreBoxMaterial.enableInstancing = true;
        if (boxGlassMaterial != null)
            boxGlassMaterial.enableInstancing = true;
        if (coreBoxGlassMaterial != null)
            coreBoxGlassMaterial.enableInstancing = true;
        if (enemyMaterial != null)
            enemyMaterial.enableInstancing = true;
        if (wallMaterial != null)
            wallMaterial.enableInstancing = true;
    }

    private void EnsureEnemyBuffers()
    {
        for (int i = 0; i < EnemyVisualKindCount; i++)
            _enemyMatricesByKind[i] ??= new Matrix4x4[BatchSize];
    }

    private void EnsureEnemyMaterials()
    {
        SetMaterialColor(enemyMaterial, enemyGoColor);
        _enemyMaterialsByKind[(int)EnemyVisualKind.Go] = enemyMaterial;
        _enemyMaterialsByKind[(int)EnemyVisualKind.Grenadier] ??= CreateUnitMaterial(enemyGrenadierColor);
        _enemyMaterialsByKind[(int)EnemyVisualKind.Crossbow] ??= CreateUnitMaterial(enemyCrossbowColor);
        _enemyMaterialsByKind[(int)EnemyVisualKind.Artillery] ??= CreateUnitMaterial(enemyArtilleryColor);
        _enemyMaterialsByKind[(int)EnemyVisualKind.CurseCaster] ??= CreateUnitMaterial(enemyCurseCasterColor);
        _enemyMaterialsByKind[(int)EnemyVisualKind.Guokui] ??= CreateUnitMaterial(enemyGuokuiColor);
        _enemyMaterialsByKind[(int)EnemyVisualKind.Ertong] ??= CreateUnitMaterial(enemyErtongColor);
    }

    private void ResetEnemyCounts()
    {
        for (int i = 0; i < _enemyCountsByKind.Length; i++)
            _enemyCountsByKind[i] = 0;
    }

    private static EnemyVisualKind ResolveEnemyVisualKind(PropertyComponent properties)
    {
        string bpName = properties.SourceBP != null ? properties.SourceBP.name : string.Empty;
        if (Contains(bpName, "Grenadier"))
            return EnemyVisualKind.Grenadier;
        if (Contains(bpName, "Crossbow") || Contains(bpName, "Arbalest"))
            return EnemyVisualKind.Crossbow;
        if (Contains(bpName, "Artillery") || Contains(bpName, "Cannon"))
            return EnemyVisualKind.Artillery;
        if (Contains(bpName, "Curse") || Contains(bpName, "Caster") || Contains(bpName, "Sorcerer"))
            return EnemyVisualKind.CurseCaster;
        if (Contains(bpName, "Guokui"))
            return EnemyVisualKind.Guokui;
        if (Contains(bpName, "Ertong"))
            return EnemyVisualKind.Ertong;

        return EnemyVisualKind.Go;
    }

    private static bool Contains(string value, string token)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Mesh CreatePrimitiveMesh(PrimitiveType primitiveType)
    {
        var go = GameObject.CreatePrimitive(primitiveType);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Destroy(go);
        return mesh;
    }

    private void EnsureUnitFallbackMaterial()
    {
        if (unitFallbackMaterial != null)
        {
            unitFallbackMaterial.enableInstancing = true;
            return;
        }

        unitFallbackMaterial = Resources.Load<Material>("DrawSystemLitFallback");
        if (unitFallbackMaterial != null)
        {
            unitFallbackMaterial.enableInstancing = true;
            return;
        }

        Debug.LogWarning("[DrawSystem] Unit fallback material was not assigned and Resources/DrawSystemLitFallback was not found. Falling back to Shader.Find; this may fail in Player builds if the shader is stripped.");
    }

    private Material CreateUnitMaterial(Color color)
    {
        if (unitFallbackMaterial != null)
        {
            var clonedMaterial = new Material(unitFallbackMaterial);
            SetMaterialColor(clonedMaterial, color);
            return clonedMaterial;
        }

        var shader = FindFirstShader(
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Standard");
        if (shader == null)
        {
            Debug.LogError("[DrawSystem] Could not find a lit unit shader. Assign Unit Fallback Material or create Assets/Resources/DrawSystemLitFallback.mat.");
            return null;
        }

        var material = new Material(shader);
        SetMaterialColor(material, color);
        return material;
    }

    private void EnsureBoxGlassFallbackMaterial()
    {
        if (boxGlassFallbackMaterial != null)
        {
            boxGlassFallbackMaterial.enableInstancing = true;
            return;
        }

        boxGlassFallbackMaterial = Resources.Load<Material>("DrawSystemBoxGlassFallback");
        if (boxGlassFallbackMaterial != null)
        {
            boxGlassFallbackMaterial.enableInstancing = true;
            return;
        }

        Debug.LogWarning("[DrawSystem] Box glass fallback material was not assigned and Resources/DrawSystemBoxGlassFallback was not found. Falling back to Shader.Find; this may fail in Player builds if the shader is stripped.");
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        material.color = color;
        material.SetColor("_Color", color);
        material.SetColor("_BaseColor", color);
        material.enableInstancing = true;
    }

    private Material CreateGlassMaterial(Color color)
    {
        if (boxGlassFallbackMaterial != null)
        {
            var clonedMaterial = new Material(boxGlassFallbackMaterial)
            {
                renderQueue = 3050
            };
            SetMaterialColor(clonedMaterial, color);
            return clonedMaterial;
        }

        var shader = FindFirstShader(
            "BlockingKing/BoxGlass",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color");
        if (shader == null)
        {
            Debug.LogError("[DrawSystem] Could not find a glass shader. Assign Box Glass Fallback Material or create Assets/Resources/DrawSystemBoxGlassFallback.mat.");
            return null;
        }

        var material = new Material(shader);
        material.color = color;
        material.SetColor("_Color", color);
        material.SetColor("_BaseColor", color);
        material.renderQueue = 3050;
        material.enableInstancing = true;
        return material;
    }

    private static Shader FindFirstShader(params string[] shaderNames)
    {
        foreach (string shaderName in shaderNames)
        {
            var shader = Shader.Find(shaderName);
            if (shader != null)
                return shader;
        }

        return null;
    }

    private readonly struct StatTextPair
    {
        public readonly GameObject Root;
        public readonly GameObject AttackRoot;
        public readonly GameObject HealthRoot;
        public readonly GameObject CountdownRoot;
        private readonly TextMeshPro _attackVisible;
        private readonly TextMeshPro _attackOccluded;
        private readonly TextMeshPro _healthVisible;
        private readonly TextMeshPro _healthOccluded;
        private readonly TextMeshPro _countdownVisible;
        private readonly TextMeshPro _countdownOccluded;

        public StatTextPair(GameObject root, StatTextStack attack, StatTextStack health, StatTextStack countdown)
        {
            Root = root;
            AttackRoot = attack.Root;
            HealthRoot = health.Root;
            CountdownRoot = countdown.Root;
            _attackVisible = attack.Visible;
            _attackOccluded = attack.Occluded;
            _healthVisible = health.Visible;
            _healthOccluded = health.Occluded;
            _countdownVisible = countdown.Visible;
            _countdownOccluded = countdown.Occluded;
        }

        public void SetAttackText(string text)
        {
            _attackVisible.text = text;
            _attackOccluded.text = text;
        }

        public void SetAttackVisible(bool visible)
        {
            AttackRoot.SetActive(visible);
        }

        public void SetHealthText(string text)
        {
            _healthVisible.text = text;
            _healthOccluded.text = text;
        }

        public void SetHealthVisible(bool visible)
        {
            HealthRoot.SetActive(visible);
        }

        public void SetCountdownText(string text)
        {
            _countdownVisible.text = text;
            _countdownOccluded.text = text;
        }

        public void SetCountdownVisible(bool visible)
        {
            CountdownRoot.SetActive(visible);
        }
    }

    private readonly struct StatTextStack
    {
        public readonly GameObject Root;
        public readonly TextMeshPro Visible;
        public readonly TextMeshPro Occluded;

        public StatTextStack(GameObject root, TextMeshPro visible, TextMeshPro occluded)
        {
            Root = root;
            Visible = visible;
            Occluded = occluded;
        }
    }
}
