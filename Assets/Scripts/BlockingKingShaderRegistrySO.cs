using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BlockingKingShaderRegistry", menuName = "BlockingKing/Rendering/Shader Registry")]
public sealed class BlockingKingShaderRegistrySO : ScriptableObject
{
    [Tooltip("Shaders that must be available in Player builds, especially shaders only reached through Shader.Find or runtime-created materials.")]
    public List<Shader> alwaysIncludedShaders = new();

    [Tooltip("Materials used by runtime systems. Their shaders are also synchronized into Always Included Shaders.")]
    public List<Material> runtimeMaterials = new();

    public IEnumerable<Shader> EnumerateShaders()
    {
        if (alwaysIncludedShaders != null)
        {
            foreach (var shader in alwaysIncludedShaders)
            {
                if (shader != null)
                    yield return shader;
            }
        }

        if (runtimeMaterials == null)
            yield break;

        foreach (var material in runtimeMaterials)
        {
            if (material != null && material.shader != null)
                yield return material.shader;
        }
    }
}
