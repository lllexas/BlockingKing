using UnityEngine;
using System.Collections;

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

    [Header("关卡开场")]
    [SerializeField, Min(0f)] private float introOverviewHoldBeats = 1f;
    [SerializeField, Min(0.01f)] private float introFocusBeats = 2f;
    [SerializeField, Min(0f)] private float introOverviewPadding = 1.5f;
    [SerializeField, Min(0f)] private float introFocusHeight = 8f;

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
    private bool _flowPaused;
    private Coroutine _introRoutine;
    private readonly Vector3[] _frustumCorners = new Vector3[4];
    private readonly Vector2[] _groundFrustum = new Vector2[4];

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

        if (isPaused || _flowPaused)
        {
            _isDragging = false;
            return;
        }

        if (_introRoutine == null)
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
        float scroll = HandZone.HasAssistSelection ? 0f : Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _targetHeight -= scroll * zoomSensitivity;
            _targetHeight = Mathf.Clamp(_targetHeight, minHeight, maxHeight);
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
        Vector3 targetPos = CalculateCameraPosition(_targetFlatPos, _targetHeight);

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

    /// <summary>根据世界坐标中的矩形设置边界。</summary>
    public void SetWorldBounds(Vector2 centerWorldXZ, Vector2 sizeWorldXZ)
    {
        mapCenter = centerWorldXZ;
        mapSize = new Vector2(Mathf.Max(0.01f, sizeWorldXZ.x), Mathf.Max(0.01f, sizeWorldXZ.y));
        UpdateMovementBounds();
    }

    /// <summary>强制聚焦到某个 XZ 世界坐标</summary>
    public void FocusOn(Vector2 worldXZ)
    {
        _targetFlatPos = new Vector3(worldXZ.x, 0f, worldXZ.y);
    }

    public void SetFlowPaused(bool paused)
    {
        _flowPaused = paused;
        if (paused)
            _isDragging = false;
    }

    public void PlayLevelIntro(Vector2 playerWorldXZ)
    {
        if (_introRoutine != null)
            StopCoroutine(_introRoutine);

        _flowPaused = false;
        _introRoutine = StartCoroutine(LevelIntroRoutine(playerWorldXZ));
    }

    /// <summary>回到地图中心 + 默认高度</summary>
    public void ResetView()
    {
        _targetFlatPos = new Vector3(mapCenter.x, 0f, mapCenter.y);
        _targetHeight = defaultHeight;
        UpdateMovementBounds();
    }

    private IEnumerator LevelIntroRoutine(Vector2 playerWorldXZ)
    {
        _isDragging = false;

        float overviewHeight = ResolveOverviewHeight();
        float playerHeight = ResolvePlayerHeight(overviewHeight);
        Vector3 overviewFlat = new(mapCenter.x, 0f, mapCenter.y);
        Vector3 playerFlat = new(playerWorldXZ.x, 0f, playerWorldXZ.y);

        _targetFlatPos = overviewFlat;
        _targetHeight = overviewHeight;

        float beatDuration = ResolveBeatDuration();
        float holdUntil = Time.unscaledTime + introOverviewHoldBeats * beatDuration;
        while (Time.unscaledTime < holdUntil)
            yield return null;

        Vector3 fromFlat = _targetFlatPos;
        float fromHeight = _targetHeight;
        float duration = Mathf.Max(0.01f, introFocusBeats * beatDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = Mathf.SmoothStep(0f, 1f, t);
            _targetFlatPos = Vector3.Lerp(fromFlat, playerFlat, t);
            _targetHeight = Mathf.Lerp(fromHeight, playerHeight, t);
            ClampTargetToBounds();
            yield return null;
        }

        _targetFlatPos = playerFlat;
        _targetHeight = playerHeight;
        ClampTargetToBounds();
        _introRoutine = null;
    }

    private float ResolveOverviewHeight()
    {
        float low = minHeight;
        float high = maxHeight;
        if (!DoesHeightContainMap(high))
            return high;

        for (int i = 0; i < 24; i++)
        {
            float mid = (low + high) * 0.5f;
            if (DoesHeightContainMap(mid))
                high = mid;
            else
                low = mid;
        }

        return Mathf.Clamp(high, minHeight, maxHeight);
    }

    private float ResolvePlayerHeight(float overviewHeight)
    {
        return Mathf.Clamp(introFocusHeight, minHeight, Mathf.Min(maxHeight, overviewHeight));
    }

    private static float ResolveBeatDuration()
    {
        return BeatTiming.GetBeatDuration();
    }

    private bool DoesHeightContainMap(float height)
    {
        if (_cam == null)
            return false;

        if (!TryBuildGroundFrustum(new Vector3(mapCenter.x, 0f, mapCenter.y), height, _groundFrustum))
            return false;

        float pad = Mathf.Max(0f, introOverviewPadding);
        Vector2 min = new(-pad, -pad);
        Vector2 max = new(mapSize.x + pad, mapSize.y + pad);

        return IsPointInConvexPolygon(new Vector2(min.x, min.y), _groundFrustum) &&
               IsPointInConvexPolygon(new Vector2(min.x, max.y), _groundFrustum) &&
               IsPointInConvexPolygon(new Vector2(max.x, max.y), _groundFrustum) &&
               IsPointInConvexPolygon(new Vector2(max.x, min.y), _groundFrustum);
    }

    private bool TryBuildGroundFrustum(Vector3 targetFlatPos, float height, Vector2[] result)
    {
        if (result == null || result.Length < 4)
            return false;

        Vector3 cameraPosition = CalculateCameraPosition(targetFlatPos, height);
        _cam.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), _cam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, _frustumCorners);

        for (int i = 0; i < 4; i++)
        {
            Vector3 direction = transform.TransformVector(_frustumCorners[i]).normalized;
            if (Mathf.Abs(direction.y) < 0.0001f)
                return false;

            float distance = -cameraPosition.y / direction.y;
            if (distance <= 0f)
                return false;

            Vector3 hit = cameraPosition + direction * distance;
            result[i] = new Vector2(hit.x, hit.z);
        }

        return true;
    }

    // ─────────── 内部 ───────────

    void UpdateMovementBounds()
    {
        float halfW = mapSize.x * 0.5f + boundsPadding;
        float halfH = mapSize.y * 0.5f + boundsPadding;
        _movementBounds = new Rect(
            mapCenter.x - halfW,
            mapCenter.y - halfH,
            halfW * 2f,
            halfH * 2f);
    }

    private void ClampTargetToBounds()
    {
        _targetFlatPos.x = Mathf.Clamp(_targetFlatPos.x, _movementBounds.xMin, _movementBounds.xMax);
        _targetFlatPos.z = Mathf.Clamp(_targetFlatPos.z, _movementBounds.yMin, _movementBounds.yMax);
        _targetHeight = Mathf.Clamp(_targetHeight, minHeight, maxHeight);
    }

    private Vector3 CalculateCameraPosition(Vector3 targetFlatPos, float height)
    {
        float pitch = transform.eulerAngles.x;
        float zOffset = height / Mathf.Tan(pitch * Mathf.Deg2Rad);
        return new Vector3(targetFlatPos.x, height, targetFlatPos.z - zOffset);
    }

    private static bool IsPointInConvexPolygon(Vector2 point, Vector2[] polygon)
    {
        bool hasPositive = false;
        bool hasNegative = false;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            float cross = Cross(b - a, point - a);
            if (cross > 0.0001f)
                hasPositive = true;
            else if (cross < -0.0001f)
                hasNegative = true;

            if (hasPositive && hasNegative)
                return false;
        }

        return true;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
