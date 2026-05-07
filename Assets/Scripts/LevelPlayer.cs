using UnityEngine;

/// <summary>
/// 播放场景入口：Start 时读 QuickPlaySession → 构建 Mesh → 挂上。
/// 不做任何其他事。
/// </summary>
public class LevelPlayer : MonoBehaviour
{
    private void Start()
    {
        var session = Resources.Load<QuickPlaySession>("QuickPlaySession");
        if (session == null || !session.active || session.targetLevel == null)
            return;

        var level = session.targetLevel;
        var config = session.config;
        config?.RebuildCache();

        var builder = new LevelMeshBuilder();
        Mesh mesh = builder.Build(level.GetMap2D(), config, level.tags);
        if (mesh == null) return;

        var go = new GameObject("LevelMesh");
        go.transform.SetParent(transform);
        go.AddComponent<MeshFilter>().mesh = mesh;

        var mat = new Material(Shader.Find("BlockingKing/LevelGeometric")
                            ?? Shader.Find("Universal Render Pipeline/Lit"));
        go.AddComponent<MeshRenderer>().material = mat;

        session.active = false;
        Debug.Log($"[LevelPlayer] {level.levelName} 已构建。");
    }
}
