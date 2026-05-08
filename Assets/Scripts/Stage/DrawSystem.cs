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

    [Header("Mesh")]
    [SerializeField] private Mesh playerMesh;
    [SerializeField] private Mesh boxMesh;
    [SerializeField] private Mesh enemyMesh;
    [SerializeField] private Mesh wallMesh;

    [Header("Material")]
    [SerializeField] private Material playerMaterial;
    [SerializeField] private Material boxMaterial;
    [SerializeField] private Material enemyMaterial;
    [SerializeField] private Material wallMaterial;

    [Header("Transform")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector3 playerScale = new(0.7f, 1f, 0.7f);
    [SerializeField] private Vector3 boxScale = new(0.85f, 0.85f, 0.85f);
    [SerializeField] private Vector3 enemyScale = new(0.75f, 0.35f, 0.75f);
    [SerializeField] private Vector3 wallScale = new(0.95f, 0.95f, 0.95f);
    [SerializeField] private float wallY = -0.2f;

    [Header("Stats Text")]
    [SerializeField] private bool showStatsText = true;
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

    private readonly Matrix4x4[] _playerMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _boxMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _enemyMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _wallMatrices = new Matrix4x4[BatchSize];
    private readonly List<StatTextPair> _statTexts = new();
    private int _playerCount;
    private int _boxCount;
    private int _enemyCount;
    private int _wallCount;
    private int _statTextCount;
    private Material _runtimeStatsVisibleMaterial;
    private Material _runtimeStatsOccludedMaterial;
    private Material _runtimeStatsMaterialSource;
    private TMP_FontAsset _runtimeStatsTextFont;

    private void Awake()
    {
        Instance = this;
        EnsureResources();
    }

    private void OnDestroy()
    {
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

    private void LateUpdate()
    {
        UpdateStatsMaterialProperties();
        DrawEntities();
    }

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
            return;

        _playerCount = 0;
        _boxCount = 0;
        _enemyCount = 0;
        _wallCount = 0;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            switch (core.EntityType)
            {
                case EntityType.Player:
                    AddPlayer(core.Position);
                    AddStatsText(core.Position, entities.propertyComponents[i].Attack, core.Health);
                    break;
                case EntityType.Box:
                    AddBox(core.Position);
                    AddStatsText(core.Position, entities.propertyComponents[i].Attack, core.Health);
                    break;
                case EntityType.Enemy:
                    AddEnemy(core.Position);
                    AddStatsText(core.Position, entities.propertyComponents[i].Attack, core.Health);
                    break;
                case EntityType.Wall:
                    AddWall(core.Position);
                    AddStatsText(core.Position, entities.propertyComponents[i].Attack, core.Health);
                    break;
            }
        }

        FlushPlayers();
        FlushBoxes();
        FlushEnemies();
        FlushWalls();
        HideUnusedStatTexts();
    }

    private void AddPlayer(Vector2Int gridPos)
    {
        _playerMatrices[_playerCount++] = Matrix4x4.TRS(ToWorld(gridPos), Quaternion.identity, playerScale);
        if (_playerCount == BatchSize)
            FlushPlayers();
    }

    private void AddBox(Vector2Int gridPos)
    {
        _boxMatrices[_boxCount++] = Matrix4x4.TRS(ToWorld(gridPos), Quaternion.identity, boxScale);
        if (_boxCount == BatchSize)
            FlushBoxes();
    }

    private void AddEnemy(Vector2Int gridPos)
    {
        _enemyMatrices[_enemyCount++] = Matrix4x4.TRS(ToEnemyWorld(gridPos), Quaternion.identity, enemyScale);
        if (_enemyCount == BatchSize)
            FlushEnemies();
    }

    private void AddWall(Vector2Int gridPos)
    {
        _wallMatrices[_wallCount++] = Matrix4x4.TRS(ToWallWorld(gridPos), Quaternion.identity, wallScale);
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

    private void FlushEnemies()
    {
        if (_enemyCount == 0)
            return;

        if (enemyMesh == null || enemyMaterial == null)
        {
            _enemyCount = 0;
            return;
        }

        Graphics.DrawMeshInstanced(enemyMesh, 0, enemyMaterial, _enemyMatrices, _enemyCount);
        _enemyCount = 0;
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

    private void AddStatsText(Vector2Int gridPos, int attack, int health)
    {
        if (!showStatsText)
            return;

        var pair = GetStatTextPair(_statTextCount++);
        pair.Root.SetActive(true);
        pair.SetAttackText(attack.ToString());
        pair.SetHealthText(health.ToString());

        float y = statsTextHeight * cellSize;
        pair.AttackRoot.transform.position = new Vector3(gridPos.x * cellSize, y, gridPos.y * cellSize);
        pair.HealthRoot.transform.position = new Vector3((gridPos.x + 1f) * cellSize, y, gridPos.y * cellSize);
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
        return new StatTextPair(root, attack, health);
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
        text.alignment = parent.name == "Attack"
            ? TextAlignmentOptions.BottomLeft
            : TextAlignmentOptions.BottomRight;
        text.fontSize = statsTextFontSize;
        text.color = color;
        text.enableWordWrapping = false;
        text.text = "0";

        RectTransform rectTransform = text.rectTransform;
        rectTransform.pivot = parent.name == "Attack" ? Vector2.zero : new Vector2(1f, 0f);
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
            return;

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

    private void EnsureResources()
    {
        if (playerMesh == null)
            playerMesh = CreatePrimitiveMesh(PrimitiveType.Capsule);
        if (boxMesh == null)
            boxMesh = CreatePrimitiveMesh(PrimitiveType.Cube);
        if (enemyMesh == null)
            enemyMesh = CreatePrimitiveMesh(PrimitiveType.Sphere);
        if (wallMesh == null)
            wallMesh = CreatePrimitiveMesh(PrimitiveType.Cube);

        if (playerMaterial == null)
            playerMaterial = CreateMaterial(new Color(0.2f, 0.55f, 1f));
        if (boxMaterial == null)
            boxMaterial = CreateMaterial(new Color(0.9f, 0.65f, 0.25f));
        if (enemyMaterial == null)
            enemyMaterial = CreateMaterial(new Color(0.92f, 0.88f, 0.78f));
        if (playerMaterial != null)
            playerMaterial.enableInstancing = true;
        if (boxMaterial != null)
            boxMaterial.enableInstancing = true;
        if (enemyMaterial != null)
            enemyMaterial.enableInstancing = true;
        if (wallMaterial != null)
            wallMaterial.enableInstancing = true;
    }

    private static Mesh CreatePrimitiveMesh(PrimitiveType primitiveType)
    {
        var go = GameObject.CreatePrimitive(primitiveType);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Destroy(go);
        return mesh;
    }

    private static Material CreateMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader);
        material.color = color;
        material.enableInstancing = true;
        return material;
    }

    private readonly struct StatTextPair
    {
        public readonly GameObject Root;
        public readonly GameObject AttackRoot;
        public readonly GameObject HealthRoot;
        private readonly TextMeshPro _attackVisible;
        private readonly TextMeshPro _attackOccluded;
        private readonly TextMeshPro _healthVisible;
        private readonly TextMeshPro _healthOccluded;

        public StatTextPair(GameObject root, StatTextStack attack, StatTextStack health)
        {
            Root = root;
            AttackRoot = attack.Root;
            HealthRoot = health.Root;
            _attackVisible = attack.Visible;
            _attackOccluded = attack.Occluded;
            _healthVisible = health.Visible;
            _healthOccluded = health.Occluded;
        }

        public void SetAttackText(string text)
        {
            _attackVisible.text = text;
            _attackOccluded.text = text;
        }

        public void SetHealthText(string text)
        {
            _healthVisible.text = text;
            _healthOccluded.text = text;
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
