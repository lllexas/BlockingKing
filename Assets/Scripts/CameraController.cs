using UnityEngine;

/// <summary>
/// 推箱子透视相机控制器，适配 XZ 平面地图。
/// 支持：方向键平移、鼠标推屏、中键拖拽、滚轮缩放、边界限制。
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("移动")]
    public float moveSpeed = 20f;
    [Tooltip("中键拖拽灵敏度，1=1:1 跟随")]
    public float dragSpeed = 1f;
    public float edgeSize = 10f;
    public bool useEdgeScrolling = true;

    [Header("缩放")]
    public float defaultHeight = 15f;
    public float minHeight = 5f;
    public float maxHeight = 30f;
    public float zoomSensitivity = 5f;

    [Header("暂停")]
    public bool isPaused = false;

    [Header("平滑")]
    public float lerpSpeed = 8f;

    [Header("边界")]
    public Vector2 mapCenter = Vector2.zero;
    public Vector2 mapSize = new Vector2(10f, 10f);
    public float boundsPadding = 2f;

    // 内部状态
    private Camera _cam;
    private Vector3 _targetFlatPos;      // XZ 平面上的目标注视点
    private float _targetHeight;
    private Vector3 _lastMouseScreenPos;
    private Rect _movementBounds;
    private bool _isDragging;

    // ─────────── 生命周期 ───────────

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = GetComponentInParent<Camera>();

        _targetFlatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        _targetHeight = Mathf.Max(transform.position.y, minHeight);

        UpdateMovementBounds();
    }

    void Update()
    {
        // F10 切换暂停
        if (Input.GetKeyDown(KeyCode.F10))
        {
            isPaused = !isPaused;
            Debug.Log($"[CameraController] 暂停状态切换: {(isPaused ? "已暂停" : "已恢复")}");
        }

        if (isPaused) return;

        HandleInput();
        ApplyTransform();
    }

    // ─────────── 输入处理 ───────────

    void HandleInput()
    {
        Vector2 moveInput = Vector2.zero;

        // ── 1. 方向键 ──
        if (Input.GetKey(KeyCode.UpArrow))    moveInput.y += 1;
        if (Input.GetKey(KeyCode.DownArrow))  moveInput.y -= 1;
        if (Input.GetKey(KeyCode.LeftArrow))  moveInput.x -= 1;
        if (Input.GetKey(KeyCode.RightArrow)) moveInput.x += 1;

        // ── 2. 鼠标推屏 ──
        if (useEdgeScrolling && !_isDragging)
        {
            Vector3 mouse = Input.mousePosition;
            if (mouse.x <= edgeSize) moveInput.x -= 1;
            if (mouse.x >= Screen.width - edgeSize) moveInput.x += 1;
            if (mouse.y <= edgeSize) moveInput.y -= 1;
            if (mouse.y >= Screen.height - edgeSize) moveInput.y += 1;
        }

        if (moveInput != Vector2.zero)
        {
            float speed = moveSpeed * (_targetHeight / defaultHeight) * Time.unscaledDeltaTime;
            // 将相机局部轴向投影到 XZ 平面
            Vector3 rightOnPlane = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 forwardOnPlane = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            _targetFlatPos += rightOnPlane * (moveInput.x * speed)
                            + forwardOnPlane * (moveInput.y * speed);
        }

        // ── 3. 中键拖拽 ──
        if (Input.GetMouseButtonDown(2))
        {
            _isDragging = true;
            _lastMouseScreenPos = Input.mousePosition;
        }
        if (Input.GetMouseButton(2))
        {
            Vector3 currentMouse = Input.mousePosition;
            // 屏幕像素增量 → XZ 平面世界位移
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            Ray rayPrev = _cam.ScreenPointToRay(_lastMouseScreenPos);
            Ray rayCurr = _cam.ScreenPointToRay(currentMouse);

            if (ground.Raycast(rayPrev, out float tPrev) &&
                ground.Raycast(rayCurr, out float tCurr))
            {
                Vector3 worldDelta = rayCurr.GetPoint(tCurr) - rayPrev.GetPoint(tPrev);
                _targetFlatPos -= new Vector3(worldDelta.x, 0f, worldDelta.z) * dragSpeed;
            }
            _lastMouseScreenPos = currentMouse;
        }
        if (Input.GetMouseButtonUp(2))
        {
            _isDragging = false;
        }

        // ── 4. 滚轮缩放 ──
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _targetHeight -= scroll * zoomSensitivity;
            _targetHeight = Mathf.Clamp(_targetHeight, minHeight, maxHeight);
            UpdateMovementBounds();
        }

        // ── 5. 边界限制 ──
        _targetFlatPos.x = Mathf.Clamp(_targetFlatPos.x,
            _movementBounds.xMin, _movementBounds.xMax);
        _targetFlatPos.z = Mathf.Clamp(_targetFlatPos.z,
            _movementBounds.yMin, _movementBounds.yMax);
    }

    // ─────────── 应用变换 ───────────

    void ApplyTransform()
    {
        // 根据相机俯仰角计算 Z 偏移，使视线落在注视点正下方
        float pitch = transform.eulerAngles.x;
        float zOffset = _targetHeight / Mathf.Tan(pitch * Mathf.Deg2Rad);
        Vector3 targetPos = new Vector3(
            _targetFlatPos.x,
            _targetHeight,
            _targetFlatPos.z - zOffset);

        transform.position = Vector3.Lerp(
            transform.position, targetPos,
            Time.unscaledDeltaTime * lerpSpeed);
    }

    // ─────────── 公共 API ───────────

    /// <summary>根据地图尺寸设置边界</summary>
    public void SetMapBounds(float width, float height, float cellSize = 1f)
    {
        mapSize = new Vector2(width * cellSize, height * cellSize);
        mapCenter = mapSize * 0.5f;
        UpdateMovementBounds();
    }

    /// <summary>强制聚焦到某个 XZ 世界坐标</summary>
    public void FocusOn(Vector2 worldXZ)
    {
        _targetFlatPos = new Vector3(worldXZ.x, 0f, worldXZ.y);
    }

    /// <summary>回到地图中心 + 默认高度</summary>
    public void ResetView()
    {
        _targetFlatPos = new Vector3(mapCenter.x, 0f, mapCenter.y);
        _targetHeight = defaultHeight;
        UpdateMovementBounds();
    }

    // ─────────── 内部 ───────────

    void UpdateMovementBounds()
    {
        Rect mapRect = new Rect(
            mapCenter.x - mapSize.x * 0.5f,
            mapCenter.y - mapSize.y * 0.5f,
            mapSize.x,
            mapSize.y);

        if (!TryBuildPerspectiveMovementBounds(mapRect, out _movementBounds))
        {
            // 回退：简单矩形 + padding
            float halfW = mapSize.x * 0.5f + boundsPadding;
            float halfH = mapSize.y * 0.5f + boundsPadding;
            _movementBounds = new Rect(
                mapCenter.x - halfW,
                mapCenter.y - halfH,
                halfW * 2f,
                halfH * 2f);
        }
    }

    /// <summary>
    /// 精密几何计算：将视口四角投影到 Y=0 地面，
    /// 反算注视点 _targetFlatPos 的合法活动矩形，
    /// 保证视锥边缘绝不超出地图。
    /// </summary>
    bool TryBuildPerspectiveMovementBounds(Rect mapRect, out Rect bounds)
    {
        bounds = mapRect;

        if (_cam == null) return false;

        float pitch = transform.eulerAngles.x;
        if (Mathf.Abs(pitch) < 0.01f || Mathf.Abs(pitch - 180f) < 0.01f)
            return false; // 俯仰角平行于地面

        float zOffset = _targetHeight / Mathf.Tan(pitch * Mathf.Deg2Rad);
        Vector3 camPos = new Vector3(
            _targetFlatPos.x, _targetHeight, _targetFlatPos.z - zOffset);

        // 临时摆放相机到目标位置以正确计算视口射线
        Vector3 savedPos = _cam.transform.position;
        _cam.transform.position = camPos;

        Vector2[] viewportCorners = {
            new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(1f, 0f), new Vector2(1f, 1f)
        };

        bool hasOffset = false;
        float minOffsetX = 0f, maxOffsetX = 0f;
        float minOffsetZ = 0f, maxOffsetZ = 0f;

        for (int i = 0; i < viewportCorners.Length; i++)
        {
            Ray ray = _cam.ViewportPointToRay(viewportCorners[i]);

            if (Mathf.Abs(ray.direction.y) < 0.0001f)
            {
                _cam.transform.position = savedPos;
                return false; // 射线平行于地面
            }

            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f)
            {
                _cam.transform.position = savedPos;
                return false; // 交点在相机后方
            }

            Vector3 worldPoint = ray.origin + ray.direction * t;
            Vector2 offset = new Vector2(
                worldPoint.x - _targetFlatPos.x,
                worldPoint.z - _targetFlatPos.z);

            if (!hasOffset)
            {
                minOffsetX = maxOffsetX = offset.x;
                minOffsetZ = maxOffsetZ = offset.y;
                hasOffset = true;
            }
            else
            {
                minOffsetX = Mathf.Min(minOffsetX, offset.x);
                maxOffsetX = Mathf.Max(maxOffsetX, offset.x);
                minOffsetZ = Mathf.Min(minOffsetZ, offset.y);
                maxOffsetZ = Mathf.Max(maxOffsetZ, offset.y);
            }
        }

        _cam.transform.position = savedPos;

        // 应用安全边距
        float margin = Mathf.Max(0f, boundsPadding);
        Rect safeRect = new Rect(
            mapRect.xMin + margin,
            mapRect.yMin + margin,
            Mathf.Max(0f, mapRect.width - margin * 2f),
            Mathf.Max(0f, mapRect.height - margin * 2f));

        float minX = safeRect.xMin - minOffsetX;
        float maxX = safeRect.xMax - maxOffsetX;
        float minZ = safeRect.yMin - minOffsetZ;
        float maxZ = safeRect.yMax - maxOffsetZ;

        // 如果地图比当前视野还小，锁定在地图中心
        if (minX > maxX) minX = maxX = safeRect.center.x;
        if (minZ > maxZ) minZ = maxZ = safeRect.center.y;

        bounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        return true;
    }
}
