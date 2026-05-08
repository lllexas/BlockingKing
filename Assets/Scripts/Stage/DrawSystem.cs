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

    [Header("Material")]
    [SerializeField] private Material playerMaterial;
    [SerializeField] private Material boxMaterial;

    [Header("Transform")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector3 playerScale = new(0.7f, 1f, 0.7f);
    [SerializeField] private Vector3 boxScale = new(0.85f, 0.85f, 0.85f);

    private readonly Matrix4x4[] _playerMatrices = new Matrix4x4[BatchSize];
    private readonly Matrix4x4[] _boxMatrices = new Matrix4x4[BatchSize];
    private int _playerCount;
    private int _boxCount;

    private void Awake()
    {
        Instance = this;
        EnsureResources();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        DrawEntities();
    }

    private void DrawEntities()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        _playerCount = 0;
        _boxCount = 0;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            switch (core.EntityType)
            {
                case EntityType.Player:
                    AddPlayer(core.Position);
                    break;
                case EntityType.Box:
                    AddBox(core.Position);
                    break;
            }
        }

        FlushPlayers();
        FlushBoxes();
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

    private void FlushPlayers()
    {
        if (_playerCount == 0)
            return;

        Graphics.DrawMeshInstanced(playerMesh, 0, playerMaterial, _playerMatrices, _playerCount);
        _playerCount = 0;
    }

    private void FlushBoxes()
    {
        if (_boxCount == 0)
            return;

        Graphics.DrawMeshInstanced(boxMesh, 0, boxMaterial, _boxMatrices, _boxCount);
        _boxCount = 0;
    }

    private Vector3 ToWorld(Vector2Int gridPos)
    {
        return new Vector3((gridPos.x + 0.5f) * cellSize, 0.5f * cellSize, (gridPos.y + 0.5f) * cellSize);
    }

    private void EnsureResources()
    {
        playerMesh ??= CreatePrimitiveMesh(PrimitiveType.Capsule);
        boxMesh ??= CreatePrimitiveMesh(PrimitiveType.Cube);
        playerMaterial ??= CreateMaterial(new Color(0.2f, 0.55f, 1f));
        boxMaterial ??= CreateMaterial(new Color(0.9f, 0.65f, 0.25f));

        playerMaterial.enableInstancing = true;
        boxMaterial.enableInstancing = true;
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
}
