using UnityEngine;

/// <summary>
/// 监听玩家输入，写入玩家单位 Intent，并推进 Tick。
/// </summary>
public class UserInputReader : MonoBehaviour
{
    private const int MoveDistance = 1;

    [Header("Attack Select")]
    [SerializeField] private Color attackRangeColor = new(1f, 0.15f, 0.08f, 0.24f);
    [SerializeField] private Color attackHoverColor = new(1f, 0.9f, 0.15f, 0.45f);
    [SerializeField] private float attackHighlightHeight = 0.006f;

    private EntityHandle _playerHandle = EntityHandle.None;
    private bool _attackPending;
    private Mesh _attackHighlightMesh;
    private Material _attackRangeMaterial;
    private Material _attackHoverMaterial;

    private static readonly Vector2Int[] CrossDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private void OnDestroy()
    {
        if (_attackHighlightMesh != null)
        {
            Destroy(_attackHighlightMesh);
            _attackHighlightMesh = null;
        }

        if (_attackRangeMaterial != null)
        {
            Destroy(_attackRangeMaterial);
            _attackRangeMaterial = null;
        }

        if (_attackHoverMaterial != null)
        {
            Destroy(_attackHoverMaterial);
            _attackHoverMaterial = null;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
            _attackPending = true;

        if (_attackPending)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                _attackPending = false;

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
            return;

        if (!TryResolvePlayer(out var playerHandle))
            return;

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
            return;

        Vector2Int playerPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        TryGetMouseGridPosition(out var hoverPosition);

        for (int i = 0; i < CrossDirections.Length; i++)
        {
            Vector2Int targetPosition = playerPosition + CrossDirections[i];
            if (!entitySystem.IsInsideMap(targetPosition))
                continue;

            bool isHover = targetPosition == hoverPosition;
            DrawAttackHighlight(targetPosition, isHover);
        }
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

    private void DrawAttackHighlight(Vector2Int gridPosition, bool isHover)
    {
        EnsureAttackHighlightResources();
        if (_attackHighlightMesh == null)
            return;

        var material = isHover ? _attackHoverMaterial : _attackRangeMaterial;
        if (material == null)
            return;

        var position = new Vector3(gridPosition.x + 0.5f, attackHighlightHeight, gridPosition.y + 0.5f);
        Graphics.DrawMesh(_attackHighlightMesh, Matrix4x4.TRS(position, Quaternion.identity, Vector3.one), material, 0);
    }

    private void EnsureAttackHighlightResources()
    {
        if (_attackHighlightMesh == null)
            _attackHighlightMesh = CreateGroundQuadMesh();

        if (_attackRangeMaterial == null)
            _attackRangeMaterial = CreateTransparentMaterial(attackRangeColor);

        if (_attackHoverMaterial == null)
            _attackHoverMaterial = CreateTransparentMaterial(attackHoverColor);
    }

    private static Mesh CreateGroundQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "AttackSelectGroundQuad"
        };

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateTransparentMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
        var material = new Material(shader)
        {
            color = color,
            renderQueue = 3000
        };

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
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
            playerHandle = _playerHandle;
            return true;
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
