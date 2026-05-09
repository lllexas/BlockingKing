using UnityEngine;

/// <summary>
/// 监听玩家输入，写入玩家单位 Intent，并推进 Tick。
/// </summary>
public class UserInputReader : MonoBehaviour
{
    private const int MoveDistance = 1;
    private const string AttackRangeOverlayId = "player_attack_range";
    private const string AttackHoverOverlayId = "player_attack_hover";

    [Header("Attack Select")]
    [SerializeField] private Color attackRangeColor = new(1f, 0.15f, 0.08f, 0.32f);
    [SerializeField] private Color attackHoverColor = new(1f, 0.9f, 0.15f, 0.58f);
    [SerializeField] private float attackHighlightHeight = 0.012f;

    private EntityHandle _playerHandle = EntityHandle.None;
    private bool _attackPending;
    private readonly System.Collections.Generic.List<Vector2Int> _attackRangeCells = new();
    private readonly System.Collections.Generic.List<Vector2Int> _attackHoverCells = new();

    private static readonly Vector2Int[] CrossDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
            _attackPending = true;

        if (_attackPending)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                _attackPending = false;
                ClearAttackOverlay();
            }

            if (Input.GetMouseButtonDown(0))
                TrySubmitAttackAtMouse();

            return;
        }

        if (!TryReadMoveDirection(out var direction))
            return;

        if (!TryResolvePlayer(out var playerHandle))
            return;

        var intent = IntentSystem.Instance.Request<MoveIntent>();
        intent.Setup(direction, MoveDistance);

        if (IntentSystem.Instance.SetPlayerIntent(playerHandle, IntentType.Move, intent))
            TickSystem.PushTick();
    }

    private void LateUpdate()
    {
        if (!_attackPending)
        {
            ClearAttackOverlay();
            return;
        }

        if (!TryResolvePlayer(out var playerHandle))
        {
            ClearAttackOverlay();
            return;
        }

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
        {
            ClearAttackOverlay();
            return;
        }

        Vector2Int playerPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        TryGetMouseGridPosition(out var hoverPosition);
        _attackRangeCells.Clear();
        _attackHoverCells.Clear();

        for (int i = 0; i < CrossDirections.Length; i++)
        {
            Vector2Int targetPosition = playerPosition + CrossDirections[i];
            if (!entitySystem.IsInsideMap(targetPosition))
                continue;

            bool isHover = targetPosition == hoverPosition;
            if (isHover)
                _attackHoverCells.Add(targetPosition);
            else
                _attackRangeCells.Add(targetPosition);
        }

        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.SetOverlay(
            AttackRangeOverlayId,
            _attackRangeCells,
            GridOverlayStyle.Danger,
            attackRangeColor,
            attackHighlightHeight,
            10);

        overlay.SetOverlay(
            AttackHoverOverlayId,
            _attackHoverCells,
            GridOverlayStyle.SoftGlow,
            attackHoverColor,
            attackHighlightHeight,
            11);
    }

    private void OnGUI()
    {
        var buttonRect = new Rect(12f, Screen.height - 52f, 96f, 36f);
        if (GUI.Button(buttonRect, "[Q] 攻击"))
            _attackPending = true;

        if (!_attackPending)
            return;

        var mouse = Event.current.mousePosition;
        GUI.Label(new Rect(mouse.x + 14f, mouse.y + 12f, 80f, 22f), "+ 攻击");
    }

    private void OnDisable()
    {
        ClearAttackOverlay();
    }

    private static bool TryReadMoveDirection(out Vector2Int direction)
    {
        if (Input.GetKeyDown(KeyCode.W))
        {
            direction = Vector2Int.up;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            direction = Vector2Int.down;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.A))
        {
            direction = Vector2Int.left;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            direction = Vector2Int.right;
            return true;
        }

        direction = Vector2Int.zero;
        return false;
    }

    private bool TrySubmitAttackAtMouse()
    {
        if (!TryResolvePlayer(out var playerHandle))
            return false;

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
            return false;

        if (!TryGetMouseGridPosition(out var targetPosition))
            return false;

        Vector2Int playerPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        Vector2Int offset = targetPosition - playerPosition;
        if (Mathf.Abs(offset.x) + Mathf.Abs(offset.y) != 1)
            return false;

        if (!entitySystem.IsInsideMap(targetPosition))
            return false;

        var intent = IntentSystem.Instance.Request<AttackIntent>();
        intent.Setup(targetPosition);

        if (!IntentSystem.Instance.SetPlayerIntent(playerHandle, IntentType.Attack, intent))
            return false;

        _attackPending = false;
        ClearAttackOverlay();
        TickSystem.PushTick();
        return true;
    }

    private bool TryGetMouseGridPosition(out Vector2Int gridPosition)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            gridPosition = default;
            return false;
        }

        var ray = camera.ScreenPointToRay(Input.mousePosition);
        var floorPlane = new Plane(Vector3.up, Vector3.zero);
        if (!floorPlane.Raycast(ray, out float distance))
        {
            gridPosition = default;
            return false;
        }

        Vector3 world = ray.GetPoint(distance);
        gridPosition = new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z));
        return true;
    }

    private void ClearAttackOverlay()
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(AttackRangeOverlayId);
        overlay.RemoveOverlay(AttackHoverOverlayId);
    }

    private bool TryResolvePlayer(out EntityHandle playerHandle)
    {
        if (EntitySystem.Instance == null || !EntitySystem.Instance.IsInitialized)
        {
            playerHandle = EntityHandle.None;
            return false;
        }

        if (EntitySystem.Instance.IsValid(_playerHandle))
        {
            int idx = EntitySystem.Instance.GetIndex(_playerHandle);
            if (idx >= 0 && EntitySystem.Instance.entities.coreComponents[idx].EntityType == EntityType.Player)
            {
                playerHandle = _playerHandle;
                return true;
            }
            // 句柄有效但指向了非玩家实体 → 缓存失效，重新遍历
            _playerHandle = EntityHandle.None;
        }

        var entities = EntitySystem.Instance.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            _playerHandle = EntitySystem.Instance.GetHandleFromId(entities.coreComponents[i].Id);
            playerHandle = _playerHandle;
            return true;
        }

        playerHandle = EntityHandle.None;
        return false;
    }
}
