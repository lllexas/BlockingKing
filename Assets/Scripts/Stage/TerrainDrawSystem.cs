using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// B版地形渲染：从 EntitySystem.groundMap 读取地形，用 GPU instancing 绘制。
/// 不创建每格 GameObject；地形变化时通过 TerrainVersion 自动重建 matrix 缓存。
/// </summary>
public class TerrainDrawSystem : MonoBehaviour
{
    private const int BatchSize = 1023;

    private readonly List<Matrix4x4> _floorMatrices = new();
    private readonly List<Matrix4x4> _wallMatrices = new();
    private readonly List<Matrix4x4> _targetMatrices = new();
    private readonly List<Matrix4x4> _completedTargetMatrices = new();
    private readonly List<Matrix4x4> _coreTargetMatrices = new();
    private readonly List<Matrix4x4> _completedCoreTargetMatrices = new();
    private readonly List<Matrix4x4> _enemyTargetMatrices = new();
    private readonly Matrix4x4[] _batch = new Matrix4x4[BatchSize];

    private TileMappingConfig _config;
    private Material _material;
    private Material _targetMaterial;
    private Material _coreTargetMaterial;
    private Mesh _floorMesh;
    private Mesh _wallMesh;
    private Mesh _targetMesh;
    private Mesh _completedTargetMesh;
    private Mesh _coreTargetMesh;
    private Mesh _completedCoreTargetMesh;
    private Mesh _enemyTargetMesh;
    private Mesh _runtimeTargetMesh;
    private Mesh _runtimeCompletedTargetMesh;
    private Mesh _runtimeCoreTargetMesh;
    private Mesh _runtimeCompletedCoreTargetMesh;
    private Mesh _runtimeEnemyTargetMesh;
    private List<LevelTagEntry> _targetTags = new();
    private float _cellSize = 1f;
    private float _wallHeight = 0.4f;
    private float _tagMarkerSize = 0.35f;
    private float _tagYOffset = 0.02f;
    [SerializeField, Min(0.1f)] private float targetBeaconHeight = 1.8f;
    [SerializeField, Range(0.01f, 0.16f)] private float targetBeaconWidthRatio = 0.03f;
    [SerializeField, Range(0.08f, 0.5f)] private float targetCornerBracketRatio = 0.28f;
    [SerializeField] private Color completedTargetColor = new(0.15f, 1f, 0.35f, 1f);
    [SerializeField, Min(1f)] private float coreTargetBeaconHeightMultiplier = 1.85f;
    private int _observedTerrainVersion = -1;
    private bool _dirty = true;

    public void Configure(
        TileMappingConfig config,
        Material material,
        float cellSize,
        float wallHeight,
        float tagMarkerSize,
        float tagYOffset,
        IReadOnlyList<LevelTagEntry> targetTags)
    {
        _config = config;
        _material = material;
        _cellSize = cellSize;
        _wallHeight = wallHeight;
        _tagMarkerSize = tagMarkerSize;
        _tagYOffset = tagYOffset;
        _targetTags = targetTags != null ? new List<LevelTagEntry>(targetTags) : new List<LevelTagEntry>();

        if (_material != null)
            _material.enableInstancing = true;

        EnsureTargetMaterial();
        RebuildMeshes();
        MarkDirty();
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    public void ReplaceTargetTag(int x, int y, int oldTagId, int newTagId)
    {
        if (_targetTags == null)
            return;

        for (int i = 0; i < _targetTags.Count; i++)
        {
            if (_targetTags[i].x == x && _targetTags[i].y == y && _targetTags[i].tagID == oldTagId)
            {
                _targetTags[i] = new LevelTagEntry { x = x, y = y, tagID = newTagId };
                _dirty = true;
                return;
            }
        }
    }

    public void Clear()
    {
        _floorMatrices.Clear();
        _wallMatrices.Clear();
        _targetMatrices.Clear();
        _completedTargetMatrices.Clear();
        _coreTargetMatrices.Clear();
        _completedCoreTargetMatrices.Clear();
        _enemyTargetMatrices.Clear();
        _observedTerrainVersion = -1;
        _dirty = true;
    }

    private void OnDestroy()
    {
        DestroyGeneratedMesh(_floorMesh);
        DestroyGeneratedMesh(_wallMesh);
        DestroyGeneratedMesh(_targetMesh);
        DestroyGeneratedMesh(_completedTargetMesh);
        DestroyGeneratedMesh(_coreTargetMesh);
        DestroyGeneratedMesh(_completedCoreTargetMesh);
        DestroyGeneratedMesh(_enemyTargetMesh);
        DestroyGeneratedMesh(_runtimeTargetMesh);
        DestroyGeneratedMesh(_runtimeCompletedTargetMesh);
        DestroyGeneratedMesh(_runtimeCoreTargetMesh);
        DestroyGeneratedMesh(_runtimeCompletedCoreTargetMesh);
        DestroyGeneratedMesh(_runtimeEnemyTargetMesh);
        DestroyGeneratedMaterial(_targetMaterial);
        DestroyGeneratedMaterial(_coreTargetMaterial);
        _floorMesh = null;
        _wallMesh = null;
        _targetMesh = null;
        _completedTargetMesh = null;
        _coreTargetMesh = null;
        _completedCoreTargetMesh = null;
        _enemyTargetMesh = null;
        _runtimeTargetMesh = null;
        _runtimeCompletedTargetMesh = null;
        _runtimeCoreTargetMesh = null;
        _runtimeCompletedCoreTargetMesh = null;
        _runtimeEnemyTargetMesh = null;
        _targetMaterial = null;
        _coreTargetMaterial = null;
    }

    private void LateUpdate()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        if (_dirty || _observedTerrainVersion != entitySystem.TerrainVersion)
            RebuildMatrices(entitySystem);

        RebuildTargetMatrices(entitySystem);

        if (_material == null || _targetMaterial == null || _coreTargetMaterial == null)
            return;

        DrawInstanced(_floorMesh, _floorMatrices, _material);
        DrawInstanced(_wallMesh, _wallMatrices, _material);
        RebuildRuntimeTargetMeshes(entitySystem);
        DrawMesh(_runtimeTargetMesh, _targetMaterial);
        DrawMesh(_runtimeCompletedTargetMesh, _targetMaterial);
        DrawMesh(_runtimeCoreTargetMesh, _coreTargetMaterial);
        DrawMesh(_runtimeCompletedCoreTargetMesh, _coreTargetMaterial);
        DrawMesh(_runtimeEnemyTargetMesh, _targetMaterial);
    }

    private void RebuildMatrices(EntitySystem entitySystem)
    {
        _floorMatrices.Clear();
        _wallMatrices.Clear();

        var entities = entitySystem.entities;
        if (entities.groundMap == null)
            return;

        for (int y = 0; y < entities.mapHeight; y++)
        {
            for (int x = 0; x < entities.mapWidth; x++)
            {
                int index = y * entities.mapWidth + x;
                int terrainId = entities.groundMap[index];
                if (terrainId == 0)
                    continue;

                var position = new Vector3(x * _cellSize, 0f, y * _cellSize);
                if (_config != null && _config.IsWall(terrainId))
                    _wallMatrices.Add(Matrix4x4.TRS(position, Quaternion.identity, Vector3.one));
                else
                    _floorMatrices.Add(Matrix4x4.TRS(position, Quaternion.identity, Vector3.one));
            }
        }

        _observedTerrainVersion = entitySystem.TerrainVersion;
        _dirty = false;
    }

    private void RebuildTargetMatrices(EntitySystem entitySystem)
    {
        _targetMatrices.Clear();
        _completedTargetMatrices.Clear();
        _coreTargetMatrices.Clear();
        _completedCoreTargetMatrices.Clear();
        _enemyTargetMatrices.Clear();

        if (_targetTags == null || _targetTags.Count == 0)
            return;

        foreach (var tag in _targetTags)
        {
            var cell = new Vector2Int(tag.x, tag.y);
            if (!entitySystem.IsInsideMap(cell))
                continue;

            int terrainId = entitySystem.GetTerrain(cell);
            bool onWall = _config != null && _config.IsWall(terrainId);
            float y = (onWall ? _wallHeight : 0f) + _tagYOffset;
            var position = new Vector3(tag.x * _cellSize, y, tag.y * _cellSize);
            var matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
            bool isCoreTarget = IsCoreTarget(tag.tagID);
            bool isEnemyTarget = IsEnemyTarget(tag.tagID);

            if (isEnemyTarget)
                _enemyTargetMatrices.Add(matrix);
            else if (isCoreTarget && HasBoxOnCell(entitySystem, cell))
                _completedCoreTargetMatrices.Add(matrix);
            else if (isCoreTarget)
                _coreTargetMatrices.Add(matrix);
            else if (HasBoxOnCell(entitySystem, cell))
                _completedTargetMatrices.Add(matrix);
            else
                _targetMatrices.Add(matrix);
        }
    }

    private bool IsCoreTarget(int tagId)
    {
        return _config != null &&
               string.Equals(_config.GetTagName(tagId), "Target.Core", System.StringComparison.OrdinalIgnoreCase);
    }

    private bool IsEnemyTarget(int tagId)
    {
        return _config != null &&
               string.Equals(_config.GetTagName(tagId), "Target.Enemy", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasBoxOnCell(EntitySystem entitySystem, Vector2Int cell)
    {
        EntityHandle occupant = entitySystem.GetOccupant(cell);
        if (!entitySystem.IsValid(occupant))
            return false;

        int occupantIndex = entitySystem.GetIndex(occupant);
        return occupantIndex >= 0 && entitySystem.entities.coreComponents[occupantIndex].EntityType == EntityType.Box;
    }

    private void RebuildRuntimeTargetMeshes(EntitySystem entitySystem)
    {
        var normalTargets = new HashSet<Vector2Int>();
        var coreTargets = new HashSet<Vector2Int>();
        var enemyTargets = new HashSet<Vector2Int>();

        if (_targetTags != null)
        {
            foreach (var tag in _targetTags)
            {
                var cell = new Vector2Int(tag.x, tag.y);
                if (!entitySystem.IsInsideMap(cell))
                    continue;

                if (IsCoreTarget(tag.tagID))
                    coreTargets.Add(cell);
                else if (IsEnemyTarget(tag.tagID))
                    enemyTargets.Add(cell);
                else
                    normalTargets.Add(cell);
            }
        }

        var targetQuads = new List<Quad>();
        var completedTargetQuads = new List<Quad>();
        var coreTargetQuads = new List<Quad>();
        var completedCoreTargetQuads = new List<Quad>();
        var enemyTargetQuads = new List<Quad>();

        AppendTargetKindQuads(entitySystem, normalTargets, ResolveTargetColor(), completedTargetColor, targetBeaconHeight, targetQuads, completedTargetQuads);
        AppendTargetKindQuads(entitySystem, coreTargets, ResolveTagColor("Target.Core"), completedTargetColor, targetBeaconHeight * coreTargetBeaconHeightMultiplier, coreTargetQuads, completedCoreTargetQuads);
        AppendTargetKindQuads(entitySystem, enemyTargets, ResolveTagColor("Target.Enemy"), completedTargetColor, targetBeaconHeight, enemyTargetQuads, null);

        ApplyQuadsToMesh(ref _runtimeTargetMesh, "RuntimeTargetMarkers", targetQuads);
        ApplyQuadsToMesh(ref _runtimeCompletedTargetMesh, "RuntimeCompletedTargetMarkers", completedTargetQuads);
        ApplyQuadsToMesh(ref _runtimeCoreTargetMesh, "RuntimeCoreTargetMarkers", coreTargetQuads);
        ApplyQuadsToMesh(ref _runtimeCompletedCoreTargetMesh, "RuntimeCompletedCoreTargetMarkers", completedCoreTargetQuads);
        ApplyQuadsToMesh(ref _runtimeEnemyTargetMesh, "RuntimeEnemyTargetMarkers", enemyTargetQuads);
    }

    private void AppendTargetKindQuads(
        EntitySystem entitySystem,
        HashSet<Vector2Int> targets,
        Color color,
        Color completedColor,
        float beaconHeight,
        List<Quad> pendingQuads,
        List<Quad> completedQuads)
    {
        if (targets == null || targets.Count == 0)
            return;

        foreach (var cell in targets)
        {
            int terrainId = entitySystem.GetTerrain(cell);
            bool onWall = _config != null && _config.IsWall(terrainId);
            float y = (onWall ? _wallHeight : 0f) + _tagYOffset;
            var origin = new Vector3(cell.x * _cellSize, y, cell.y * _cellSize);
            bool hasBox = HasBoxOnCell(entitySystem, cell);
            var quads = completedQuads != null && hasBox ? completedQuads : pendingQuads;
            var usedColor = (completedQuads != null && hasBox) ? completedColor : color;
            AddTargetCellQuads(quads, cell, targets, origin, usedColor, beaconHeight);
        }
    }

    private void AddTargetCellQuads(
        List<Quad> quads,
        Vector2Int cell,
        HashSet<Vector2Int> sameKindTargets,
        Vector3 origin,
        Color color,
        float beaconHeight)
    {
        Color beamColor = color;
        beamColor.a = 1f;
        Color centerColor = color;
        centerColor.a = 0.49f;

        float s = _cellSize;
        float marker = _tagMarkerSize * s;
        float inset = (s - marker) * 0.5f;
        float min = inset;
        float max = inset + marker;
        float bracketLength = targetCornerBracketRatio * s;
        float halfBeaconWidth = Mathf.Max(0.01f, s * targetBeaconWidthRatio * 0.5f);
        float beaconTop = beaconHeight * s;

        AddQuad(quads, origin,
            new Vector3(min, 0f, min),
            new Vector3(min, 0f, max),
            new Vector3(max, 0f, min),
            new Vector3(max, 0f, max),
            Vector3.up,
            centerColor);

        AddCornerGlyph(quads, origin, new Vector3(0f, 0f, 0f), Vector3.right, Vector3.forward, cell, sameKindTargets, Vector2Int.left, Vector2Int.down, new Vector2Int(-1, -1), halfBeaconWidth, bracketLength, beaconTop, beamColor);
        AddCornerGlyph(quads, origin, new Vector3(s, 0f, 0f), Vector3.left, Vector3.forward, cell, sameKindTargets, Vector2Int.right, Vector2Int.down, new Vector2Int(1, -1), halfBeaconWidth, bracketLength, beaconTop, beamColor);
        AddCornerGlyph(quads, origin, new Vector3(0f, 0f, s), Vector3.right, Vector3.back, cell, sameKindTargets, Vector2Int.left, Vector2Int.up, new Vector2Int(-1, 1), halfBeaconWidth, bracketLength, beaconTop, beamColor);
        AddCornerGlyph(quads, origin, new Vector3(s, 0f, s), Vector3.left, Vector3.back, cell, sameKindTargets, Vector2Int.right, Vector2Int.up, new Vector2Int(1, 1), halfBeaconWidth, bracketLength, beaconTop, beamColor);
    }

    private void AddCornerGlyph(
        List<Quad> quads,
        Vector3 origin,
        Vector3 corner,
        Vector3 inwardHorizontal,
        Vector3 inwardVertical,
        Vector2Int cell,
        HashSet<Vector2Int> sameKindTargets,
        Vector2Int horizontalNeighbor,
        Vector2Int verticalNeighbor,
        Vector2Int diagonalNeighbor,
        float halfWidth,
        float lineLength,
        float height,
        Color color)
    {
        bool h = sameKindTargets.Contains(cell + horizontalNeighbor);
        bool v = sameKindTargets.Contains(cell + verticalNeighbor);
        bool d = sameKindTargets.Contains(cell + diagonalNeighbor);

        float edgeInset = halfWidth * 0.5f;
        corner += inwardHorizontal * edgeInset + inwardVertical * edgeInset;

        if (h && v && d)
        {
            Color weak = color;
            weak.a = 0.18f;
            AddCornerBeacon(quads, origin + corner, halfWidth * 0.75f, height * 0.28f, weak);
            return;
        }

        if (h && v)
        {
            AddVerticalShortLine(quads, origin, corner, (inwardHorizontal + inwardVertical).normalized, lineLength * 0.55f, halfWidth * 0.5f, height * 0.5f, color);
            return;
        }

        if (h)
        {
            AddVerticalShortLine(quads, origin, corner, inwardHorizontal, lineLength * 0.4f, halfWidth * 0.5f, height * 0.5f, color);
            return;
        }

        if (v)
        {
            AddVerticalShortLine(quads, origin, corner, inwardVertical, lineLength * 0.4f, halfWidth * 0.5f, height * 0.5f, color);
            return;
        }

        if (d)
        {
            Color weakDiagonal = color;
            weakDiagonal.a = 0.55f;
            AddVerticalShortLine(quads, origin, corner, (inwardHorizontal + inwardVertical).normalized, lineLength * 0.3f, halfWidth * 0.4f, height * 0.35f, weakDiagonal);
            return;
        }

        // 孤立角 → L 形小拐角（偏移避免侵犯邻格象限）
        float bracketArmLength = lineLength * 0.8f;
        float bracketThickness = halfWidth * 1.0f;
        Vector3 hOffset = inwardVertical * bracketThickness;   // 水平臂往 +Z 推
        Vector3 vOffset = inwardHorizontal * bracketThickness; // 垂直臂往 +X 推
        AddVerticalShortLine(quads, origin, corner + hOffset, inwardHorizontal, bracketArmLength, bracketThickness, height, color);
        AddVerticalShortLine(quads, origin, corner + vOffset, inwardVertical, bracketArmLength, bracketThickness, height, color);
    }

    private static void AddVerticalShortLine(List<Quad> quads, Vector3 origin, Vector3 start, Vector3 direction, float length, float halfWidth, float height, Color color)
    {
        Vector3 normalized = direction.normalized;
        Vector3 perp = new Vector3(normalized.z, 0f, -normalized.x).normalized * halfWidth;
        Vector3 end = start + normalized * length;
        Vector3 up = Vector3.up * height;

        // 顶面
        AddQuad(quads, origin,
            start - perp + up, start + perp + up,
            end - perp + up, end + perp + up,
            Vector3.up, color);

        // 左侧竖面（朝 -perp 方向）
        AddQuad(quads, origin,
            start - perp, end - perp,
            start - perp + up, end - perp + up,
            -perp.normalized, color);

        // 右侧竖面（朝 +perp 方向）
        AddQuad(quads, origin,
            start + perp, end + perp,
            start + perp + up, end + perp + up,
            perp.normalized, color);
    }

    private void DrawInstanced(Mesh mesh, List<Matrix4x4> matrices, Material material)
    {
        if (mesh == null || matrices == null || matrices.Count == 0 || material == null)
            return;

        for (int start = 0; start < matrices.Count; start += BatchSize)
        {
            int count = Mathf.Min(BatchSize, matrices.Count - start);
            matrices.CopyTo(start, _batch, 0, count);
            Graphics.DrawMeshInstanced(mesh, 0, material, _batch, count);
        }
    }

    private void DrawMesh(Mesh mesh, Material material)
    {
        if (mesh == null || mesh.vertexCount == 0 || material == null)
            return;

        Graphics.DrawMesh(mesh, Matrix4x4.identity, material, gameObject.layer);
    }

    private void RebuildMeshes()
    {
        DestroyGeneratedMesh(_floorMesh);
        DestroyGeneratedMesh(_wallMesh);
        DestroyGeneratedMesh(_targetMesh);
        DestroyGeneratedMesh(_completedTargetMesh);
        DestroyGeneratedMesh(_coreTargetMesh);
        DestroyGeneratedMesh(_completedCoreTargetMesh);
        DestroyGeneratedMesh(_enemyTargetMesh);

        _floorMesh = BuildFloorMesh();
        _wallMesh = BuildWallMesh();
        _targetMesh = BuildTargetMesh(ResolveTargetColor(), targetBeaconHeight);
        _completedTargetMesh = BuildTargetMesh(completedTargetColor, targetBeaconHeight);
        float coreBeaconHeight = targetBeaconHeight * coreTargetBeaconHeightMultiplier;
        _coreTargetMesh = BuildTargetMesh(ResolveTagColor("Target.Core"), coreBeaconHeight);
        _completedCoreTargetMesh = BuildTargetMesh(completedTargetColor, coreBeaconHeight);
        _enemyTargetMesh = BuildTargetMesh(ResolveTagColor("Target.Enemy"), targetBeaconHeight);
    }

    private Mesh BuildFloorMesh()
    {
        Color color = new Color(0f, 0f, 0f, 0f);
        float s = _cellSize;
        return BuildQuads("InstancedFloor", new[]
        {
            new Quad(
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, s),
                new Vector3(s, 0f, 0f),
                new Vector3(s, 0f, s),
                Vector3.up,
                color)
        });
    }

    private Mesh BuildWallMesh()
    {
        Color color = new Color(0f, 0f, 0f, 1f);
        float s = _cellSize;
        float h = _wallHeight;
        return BuildQuads("InstancedWall", new[]
        {
            new Quad(new Vector3(0f, h, 0f), new Vector3(0f, h, s), new Vector3(s, h, 0f), new Vector3(s, h, s), Vector3.up, color),
            new Quad(new Vector3(0f, 0f, s), new Vector3(s, 0f, s), new Vector3(0f, h, s), new Vector3(s, h, s), Vector3.forward, color),
            new Quad(new Vector3(s, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(s, h, 0f), new Vector3(0f, h, 0f), Vector3.back, color),
            new Quad(new Vector3(s, 0f, 0f), new Vector3(s, h, 0f), new Vector3(s, 0f, s), new Vector3(s, h, s), Vector3.right, color),
            new Quad(new Vector3(0f, 0f, s), new Vector3(0f, h, s), new Vector3(0f, 0f, 0f), new Vector3(0f, h, 0f), Vector3.left, color)
        });
    }

    private Mesh BuildTargetMesh(Color color, float beaconHeight)
    {
        Color beamColor = color;
        beamColor.a = 1f;
        Color centerColor = color;
        centerColor.a = 0.49f;

        float s = _cellSize;
        float marker = _tagMarkerSize * s;
        float inset = (s - marker) * 0.5f;
        float min = inset;
        float max = inset + marker;
        float bracketLength = targetCornerBracketRatio * s;
        float halfBeaconWidth = Mathf.Max(0.01f, s * targetBeaconWidthRatio * 0.5f);
        float beaconTop = beaconHeight * s;
        var quads = new List<Quad>
        {
            new Quad(
                new Vector3(min, 0f, min),
                new Vector3(min, 0f, max),
                new Vector3(max, 0f, min),
                new Vector3(max, 0f, max),
                Vector3.up,
                centerColor)
        };

        AddCornerBeacon(quads, new Vector3(0f, 0f, 0f), halfBeaconWidth, beaconTop, beamColor);
        AddCornerBeacon(quads, new Vector3(s, 0f, 0f), halfBeaconWidth, beaconTop, beamColor);
        AddCornerBeacon(quads, new Vector3(0f, 0f, s), halfBeaconWidth, beaconTop, beamColor);
        AddCornerBeacon(quads, new Vector3(s, 0f, s), halfBeaconWidth, beaconTop, beamColor);

        AddFloorBracket(quads, new Vector3(0f, 0f, 0f), Vector3.right, bracketLength, halfBeaconWidth, beamColor);
        AddFloorBracket(quads, new Vector3(0f, 0f, 0f), Vector3.forward, bracketLength, halfBeaconWidth, beamColor);
        AddFloorBracket(quads, new Vector3(s, 0f, 0f), Vector3.left, bracketLength, halfBeaconWidth, beamColor);
        AddFloorBracket(quads, new Vector3(s, 0f, 0f), Vector3.forward, bracketLength, halfBeaconWidth, beamColor);
        AddFloorBracket(quads, new Vector3(0f, 0f, s), Vector3.right, bracketLength, halfBeaconWidth, beamColor);
        AddFloorBracket(quads, new Vector3(0f, 0f, s), Vector3.back, bracketLength, halfBeaconWidth, beamColor);
        AddFloorBracket(quads, new Vector3(s, 0f, s), Vector3.left, bracketLength, halfBeaconWidth, beamColor);
        AddFloorBracket(quads, new Vector3(s, 0f, s), Vector3.back, bracketLength, halfBeaconWidth, beamColor);

        return BuildQuads("InstancedTargetMarker", quads);
    }

    private void EnsureTargetMaterial()
    {
        DestroyGeneratedMaterial(_targetMaterial);
        DestroyGeneratedMaterial(_coreTargetMaterial);

        var shader = Shader.Find("BlockingKing/TargetBeacon")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");

        if (shader == null)
            return;

        _targetMaterial = CreateTargetMaterial(shader, targetBeaconHeight);
        _coreTargetMaterial = CreateTargetMaterial(shader, targetBeaconHeight * coreTargetBeaconHeightMultiplier);
    }

    private Material CreateTargetMaterial(Shader shader, float beaconHeight)
    {
        var material = new Material(shader)
        {
            name = "BlockingKing_TargetBeacon_Runtime",
            enableInstancing = true,
            renderQueue = 2950
        };

        material.SetFloat("_BeamHeight", Mathf.Max(0.01f, beaconHeight * _cellSize));
        return material;
    }

    private static void AddCornerBeacon(List<Quad> quads, Vector3 center, float halfWidth, float height, Color color)
    {
        float x0 = center.x - halfWidth;
        float x1 = center.x + halfWidth;
        float z0 = center.z - halfWidth;
        float z1 = center.z + halfWidth;

        quads.Add(new Quad(new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x0, height, z1), new Vector3(x1, height, z1), Vector3.forward, color));
        quads.Add(new Quad(new Vector3(x1, 0f, z0), new Vector3(x0, 0f, z0), new Vector3(x1, height, z0), new Vector3(x0, height, z0), Vector3.back, color));
        quads.Add(new Quad(new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0), new Vector3(x1, height, z1), new Vector3(x1, height, z0), Vector3.right, color));
        quads.Add(new Quad(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x0, height, z0), new Vector3(x0, height, z1), Vector3.left, color));
    }

    private static void AddFloorBracket(List<Quad> quads, Vector3 origin, Vector3 direction, float length, float halfWidth, Color color)
    {
        Vector3 right = new Vector3(direction.z, 0f, -direction.x).normalized * halfWidth;
        Vector3 start = origin;
        Vector3 end = origin + direction.normalized * length;

        quads.Add(new Quad(
            start - right,
            start + right,
            end - right,
            end + right,
            Vector3.up,
            color));
    }

    private Color ResolveTagColor(string tagName)
    {
        if (_config == null)
            return Color.white;

        foreach (int tagId in _config.AllTagIDs)
        {
            if (string.Equals(_config.GetTagName(tagId), tagName, System.StringComparison.OrdinalIgnoreCase))
                return _config.GetTagColor(tagId);
        }

        return Color.white;
    }

    private Color ResolveTargetColor()
    {
        return ResolveTagColor("Target");
    }

    private static void AddQuad(
        List<Quad> quads,
        Vector3 origin,
        Vector3 lb,
        Vector3 rb,
        Vector3 lt,
        Vector3 rt,
        Vector3 normal,
        Color color)
    {
        quads.Add(new Quad(origin + lb, origin + rb, origin + lt, origin + rt, normal, color));
    }

    private static void ApplyQuadsToMesh(ref Mesh mesh, string name, IReadOnlyList<Quad> quads)
    {
        if (mesh == null)
        {
            mesh = new Mesh
            {
                name = name
            };
        }
        else
        {
            mesh.Clear();
        }

        if (quads == null || quads.Count == 0)
            return;

        var verts = new List<Vector3>(quads.Count * 4);
        var normals = new List<Vector3>(quads.Count * 4);
        var colors = new List<Color>(quads.Count * 4);
        var tris = new List<int>(quads.Count * 6);

        foreach (var quad in quads)
        {
            int index = verts.Count;
            verts.Add(quad.Lb);
            verts.Add(quad.Rb);
            verts.Add(quad.Lt);
            verts.Add(quad.Rt);

            normals.Add(quad.Normal);
            normals.Add(quad.Normal);
            normals.Add(quad.Normal);
            normals.Add(quad.Normal);

            colors.Add(quad.Color);
            colors.Add(quad.Color);
            colors.Add(quad.Color);
            colors.Add(quad.Color);

            tris.Add(index);
            tris.Add(index + 1);
            tris.Add(index + 2);
            tris.Add(index + 1);
            tris.Add(index + 3);
            tris.Add(index + 2);
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    private static Mesh BuildQuads(string name, IReadOnlyList<Quad> quads)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();

        foreach (var quad in quads)
        {
            int index = verts.Count;
            verts.Add(quad.Lb);
            verts.Add(quad.Rb);
            verts.Add(quad.Lt);
            verts.Add(quad.Rt);

            normals.Add(quad.Normal);
            normals.Add(quad.Normal);
            normals.Add(quad.Normal);
            normals.Add(quad.Normal);

            colors.Add(quad.Color);
            colors.Add(quad.Color);
            colors.Add(quad.Color);
            colors.Add(quad.Color);

            tris.Add(index);
            tris.Add(index + 1);
            tris.Add(index + 2);
            tris.Add(index + 1);
            tris.Add(index + 3);
            tris.Add(index + 2);
        }

        var mesh = new Mesh
        {
            name = name
        };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DestroyGeneratedMesh(Mesh mesh)
    {
        if (mesh == null)
            return;

        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);
    }

    private static void DestroyGeneratedMaterial(Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }

    private readonly struct Quad
    {
        public readonly Vector3 Lb;
        public readonly Vector3 Rb;
        public readonly Vector3 Lt;
        public readonly Vector3 Rt;
        public readonly Vector3 Normal;
        public readonly Color Color;

        public Quad(Vector3 lb, Vector3 rb, Vector3 lt, Vector3 rt, Vector3 normal, Color color)
        {
            Lb = lb;
            Rb = rb;
            Lt = lt;
            Rt = rt;
            Normal = normal;
            Color = color;
        }
    }
}
