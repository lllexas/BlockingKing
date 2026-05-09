using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum GridOverlayStyle
{
    SolidTint,
    SoftGlow,
    Danger,
    ValidTarget,
    InvalidTarget,
    Path,
    LineBeam,
    AreaPulse,
    SelectionRing,
    PreviewAfterimage
}

/// <summary>
/// 格子叠加层绘制服务。接受任意系统注册的半透明格子叠加层。
/// 当前后端使用 GPU instancing，和实体 DrawSystem 保持同一类绘制模型。
/// </summary>
public class GridOverlayDrawSystem : MonoBehaviour
{
    public static GridOverlayDrawSystem Instance { get; private set; }

    private const int BatchSize = 1023;

    [Header("Instancing Backend")]
    [SerializeField] private Mesh cellMesh;
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Vector2 cellSize = Vector2.one;
    [SerializeField] private float priorityHeightStep = 0.0005f;

    [Header("Styles")]
    [TableList(AlwaysExpanded = true)]
    [SerializeField] private GridOverlayMaterialSlot[] materialSlots;

    private readonly Dictionary<string, OverlayEntry> _overlays = new();
    private readonly List<Vector2Int> _scratchCells = new();
    private readonly Matrix4x4[] _matrices = new Matrix4x4[BatchSize];
    private Mesh _quadMesh;
    private readonly Dictionary<MaterialCacheKey, Material> _materialCache = new();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        foreach (var kvp in _materialCache)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }

        if (_quadMesh != null)
            Destroy(_quadMesh);

        _materialCache.Clear();
        _overlays.Clear();

        if (Instance == this)
            Instance = null;
    }

    // ──────── 公开 API ────────

    /// <summary>设置一组叠加层格子。同 ID 重复调用会覆盖。</summary>
    public void SetOverlay(
        string id,
        IReadOnlyList<Vector2Int> cells,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        SetOverlay(id, cells, GridOverlayStyle.SolidTint, color, height, priority);
    }

    /// <summary>设置一组叠加层格子。同 ID 重复调用会覆盖。</summary>
    public void SetOverlay(
        string id,
        IReadOnlyList<Vector2Int> cells,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        if (!_overlays.TryGetValue(id, out var entry))
        {
            entry = new OverlayEntry { Id = id };
            _overlays[id] = entry;
        }

        entry.Cells.Clear();
        if (cells != null)
            entry.Cells.AddRange(cells);

        entry.Color = color;
        entry.Style = style;
        entry.Height = height;
        entry.Priority = priority;
    }

    /// <summary>设置单个格子。</summary>
    public void SetCell(
        string id,
        Vector2Int cell,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        SetCell(id, cell, GridOverlayStyle.SolidTint, color, height, priority);
    }

    /// <summary>设置单个格子。</summary>
    public void SetCell(
        string id,
        Vector2Int cell,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        _scratchCells.Clear();
        AddCellIfValid(_scratchCells, cell);
        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>设置一组相对 origin 的偏移格子。</summary>
    public void SetOffsets(
        string id,
        Vector2Int origin,
        IReadOnlyList<Vector2Int> offsets,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        SetOffsets(id, origin, offsets, GridOverlayStyle.SolidTint, color, height, priority);
    }

    /// <summary>设置一组相对 origin 的偏移格子。</summary>
    public void SetOffsets(
        string id,
        Vector2Int origin,
        IReadOnlyList<Vector2Int> offsets,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        _scratchCells.Clear();

        if (offsets != null)
        {
            for (int i = 0; i < offsets.Count; i++)
                AddCellIfValid(_scratchCells, origin + offsets[i]);
        }

        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>设置从 start 到 end 的离散格子线。水平、垂直、斜线和一般整数斜率都可用。</summary>
    public void SetLine(
        string id,
        Vector2Int start,
        Vector2Int end,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool includeStart = true)
    {
        SetLine(id, start, end, GridOverlayStyle.SolidTint, color, height, priority, includeStart);
    }

    /// <summary>设置从 start 到 end 的离散格子线。水平、垂直、斜线和一般整数斜率都可用。</summary>
    public void SetLine(
        string id,
        Vector2Int start,
        Vector2Int end,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool includeStart = true)
    {
        _scratchCells.Clear();
        CollectLine(_scratchCells, start, end, includeStart);
        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>设置一条射线，length 为最大格数，不包含 origin。</summary>
    public void SetRay(
        string id,
        Vector2Int origin,
        Vector2Int direction,
        int length,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        SetRay(id, origin, direction, length, GridOverlayStyle.SolidTint, color, height, priority);
    }

    /// <summary>设置一条射线，length 为最大格数，不包含 origin。</summary>
    public void SetRay(
        string id,
        Vector2Int origin,
        Vector2Int direction,
        int length,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        _scratchCells.Clear();
        CollectRay(_scratchCells, origin, direction, length);
        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>设置多条射线，适合十字线、八方向线等。</summary>
    public void SetRays(
        string id,
        Vector2Int origin,
        IReadOnlyList<Vector2Int> directions,
        int length,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        SetRays(id, origin, directions, length, GridOverlayStyle.SolidTint, color, height, priority);
    }

    /// <summary>设置多条射线，适合十字线、八方向线等。</summary>
    public void SetRays(
        string id,
        Vector2Int origin,
        IReadOnlyList<Vector2Int> directions,
        int length,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0)
    {
        _scratchCells.Clear();

        if (directions != null)
        {
            for (int i = 0; i < directions.Count; i++)
                CollectRay(_scratchCells, origin, directions[i], length);
        }

        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>设置轴对齐矩形。</summary>
    public void SetRectangle(
        string id,
        Vector2Int min,
        Vector2Int max,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        SetRectangle(id, min, max, GridOverlayStyle.SolidTint, color, height, priority, hollow);
    }

    /// <summary>设置轴对齐矩形。</summary>
    public void SetRectangle(
        string id,
        Vector2Int min,
        Vector2Int max,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        _scratchCells.Clear();

        int minX = Mathf.Min(min.x, max.x);
        int maxX = Mathf.Max(min.x, max.x);
        int minY = Mathf.Min(min.y, max.y);
        int maxY = Mathf.Max(min.y, max.y);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (hollow && x > minX && x < maxX && y > minY && y < maxY)
                    continue;

                AddCellIfValid(_scratchCells, new Vector2Int(x, y));
            }
        }

        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>设置方形范围，radius=1 表示 3x3。</summary>
    public void SetSquare(
        string id,
        Vector2Int center,
        int radius,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        SetSquare(id, center, radius, GridOverlayStyle.SolidTint, color, height, priority, hollow);
    }

    /// <summary>设置方形范围，radius=1 表示 3x3。</summary>
    public void SetSquare(
        string id,
        Vector2Int center,
        int radius,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        radius = Mathf.Max(0, radius);
        SetRectangle(
            id,
            center - new Vector2Int(radius, radius),
            center + new Vector2Int(radius, radius),
            style,
            color,
            height,
            priority,
            hollow);
    }

    /// <summary>设置菱形范围，使用曼哈顿距离。</summary>
    public void SetDiamond(
        string id,
        Vector2Int center,
        int radius,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        SetDiamond(id, center, radius, GridOverlayStyle.SolidTint, color, height, priority, hollow);
    }

    /// <summary>设置菱形范围，使用曼哈顿距离。</summary>
    public void SetDiamond(
        string id,
        Vector2Int center,
        int radius,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        _scratchCells.Clear();
        radius = Mathf.Max(0, radius);

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int distance = Mathf.Abs(x) + Mathf.Abs(y);
                if (distance > radius)
                    continue;

                if (hollow && distance < radius)
                    continue;

                AddCellIfValid(_scratchCells, center + new Vector2Int(x, y));
            }
        }

        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>设置近似圆形范围，使用欧氏距离。</summary>
    public void SetCircle(
        string id,
        Vector2Int center,
        int radius,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        SetCircle(id, center, radius, GridOverlayStyle.SolidTint, color, height, priority, hollow);
    }

    /// <summary>设置近似圆形范围，使用欧氏距离。</summary>
    public void SetCircle(
        string id,
        Vector2Int center,
        int radius,
        GridOverlayStyle style,
        Color color,
        float height = 0.006f,
        int priority = 0,
        bool hollow = false)
    {
        _scratchCells.Clear();
        radius = Mathf.Max(0, radius);
        int radiusSq = radius * radius;
        int innerSq = Mathf.Max(0, radius - 1) * Mathf.Max(0, radius - 1);

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int distanceSq = x * x + y * y;
                if (distanceSq > radiusSq)
                    continue;

                if (hollow && distanceSq < innerSq)
                    continue;

                AddCellIfValid(_scratchCells, center + new Vector2Int(x, y));
            }
        }

        SetOverlay(id, _scratchCells, style, color, height, priority);
    }

    /// <summary>移除一个叠加层。</summary>
    public void RemoveOverlay(string id)
    {
        _overlays.Remove(id);
    }

    /// <summary>清除全部叠加层。</summary>
    public void ClearAll()
    {
        _overlays.Clear();
    }

    private void LateUpdate()
    {
        if (_overlays.Count == 0)
            return;

        Mesh mesh = ResolveCellMesh();
        if (mesh == null)
            return;

        foreach (var kvp in _overlays)
            DrawOverlay(kvp.Value, mesh);
    }

    // ──────── 资源 ────────

    private Mesh ResolveCellMesh()
    {
        if (cellMesh != null)
            return cellMesh;

        EnsureQuadMesh();
        return _quadMesh;
    }

    private void EnsureQuadMesh()
    {
        if (_quadMesh != null)
            return;

        _quadMesh = new Mesh { name = "GridOverlayQuad" };
        _quadMesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3(-0.5f, 0f,  0.5f)
        };
        _quadMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        _quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _quadMesh.RecalculateBounds();
    }

    private Material GetOrCreateMaterial(GridOverlayStyle style, Color color)
    {
        var key = new MaterialCacheKey(style, color);
        if (_materialCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        Material sourceMaterial = FindMaterialForStyle(style);

        Material material;
        if (sourceMaterial != null)
        {
            material = new Material(sourceMaterial);
            material.color = color;
        }
        else
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");

            material = new Material(shader) { color = color, renderQueue = 3000 };
        }

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = Mathf.Max(material.renderQueue, 3000);
        material.enableInstancing = true;

        _materialCache[key] = material;
        return material;
    }

    private Material FindMaterialForStyle(GridOverlayStyle style)
    {
        if (materialSlots != null)
        {
            for (int i = 0; i < materialSlots.Length; i++)
            {
                if (materialSlots[i].Style == style && materialSlots[i].Material != null)
                    return materialSlots[i].Material;
            }
        }

        return defaultMaterial;
    }

    private void DrawOverlay(OverlayEntry overlay, Mesh mesh)
    {
        if (overlay == null || overlay.Cells.Count == 0)
            return;

        Material material = GetOrCreateMaterial(overlay.Style, overlay.Color);
        if (material == null)
            return;

        float y = overlay.Height + overlay.Priority * priorityHeightStep;
        int count = 0;

        for (int i = 0; i < overlay.Cells.Count; i++)
        {
            Vector2Int cell = overlay.Cells[i];
            var position = new Vector3(cell.x + 0.5f, y, cell.y + 0.5f);
            var scale = new Vector3(cellSize.x, 1f, cellSize.y);
            _matrices[count++] = Matrix4x4.TRS(position, Quaternion.identity, scale);

            if (count == BatchSize)
            {
                Graphics.DrawMeshInstanced(mesh, 0, material, _matrices, count);
                count = 0;
            }
        }

        if (count > 0)
            Graphics.DrawMeshInstanced(mesh, 0, material, _matrices, count);
    }

    // ──────── 几何工具 ────────

    private static void CollectLine(List<Vector2Int> results, Vector2Int start, Vector2Int end, bool includeStart)
    {
        Vector2Int delta = end - start;
        int steps = GreatestCommonDivisor(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        if (steps <= 0)
        {
            if (includeStart)
                AddCellIfValid(results, start);

            return;
        }

        Vector2Int step = new(delta.x / steps, delta.y / steps);
        int startIndex = includeStart ? 0 : 1;

        for (int i = startIndex; i <= steps; i++)
            AddCellIfValid(results, start + step * i);
    }

    private static void CollectRay(List<Vector2Int> results, Vector2Int origin, Vector2Int direction, int length)
    {
        if (direction == Vector2Int.zero || length <= 0)
            return;

        Vector2Int normalized = NormalizeGridDirection(direction);
        for (int i = 1; i <= length; i++)
            AddCellIfValid(results, origin + normalized * i);
    }

    private static Vector2Int NormalizeGridDirection(Vector2Int direction)
    {
        int divisor = GreatestCommonDivisor(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
        if (divisor <= 0)
            return Vector2Int.zero;

        return new Vector2Int(direction.x / divisor, direction.y / divisor);
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        if (a == 0)
            return b;

        if (b == 0)
            return a;

        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }

        return Mathf.Abs(a);
    }

    private static void AddCellIfValid(List<Vector2Int> results, Vector2Int cell)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem != null && entitySystem.IsInitialized && !entitySystem.IsInsideMap(cell))
            return;

        if (!results.Contains(cell))
            results.Add(cell);
    }

    // ──────── 内部结构 ────────

    private class OverlayEntry
    {
        public string Id;
        public readonly List<Vector2Int> Cells = new();
        public GridOverlayStyle Style;
        public Color Color;
        public float Height;
        public int Priority;
    }

    [System.Serializable]
#pragma warning disable 0649
    private struct GridOverlayMaterialSlot
    {
        [TableColumnWidth(150, Resizable = false)]
        public GridOverlayStyle Style;

        [AssetsOnly]
        public Material Material;
    }
#pragma warning restore 0649

    private readonly struct MaterialCacheKey : System.IEquatable<MaterialCacheKey>
    {
        private readonly GridOverlayStyle _style;
        private readonly Color _color;

        public MaterialCacheKey(GridOverlayStyle style, Color color)
        {
            _style = style;
            _color = color;
        }

        public bool Equals(MaterialCacheKey other)
        {
            return _style == other._style && _color.Equals(other._color);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)_style * 397) ^ _color.GetHashCode();
        }
    }
}
