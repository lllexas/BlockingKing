using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BlockingKingShaderRegistryEditor
{
    private const string RegistryPath = "Assets/Settings/Rendering/BlockingKingShaderRegistry.asset";
    private const string DrawSystemFallbackMaterialPath = "Assets/Resources/DrawSystemLitFallback.mat";
    private const string DrawSystemGlassFallbackMaterialPath = "Assets/Resources/DrawSystemBoxGlassFallback.mat";
    private static readonly string[] GlobalFallbackShadersToRemove =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Unlit",
        "Standard",
        "Unlit/Color",
        "Sprites/Default"
    };

    [MenuItem("Tools/BlockingKing/Rendering/Create or Update Shader Registry")]
    public static void CreateOrUpdateDefaultRegistry()
    {
        EnsureFolder("Assets/Settings");
        EnsureFolder("Assets/Settings/Rendering");

        var registry = AssetDatabase.LoadAssetAtPath<BlockingKingShaderRegistrySO>(RegistryPath);
        if (registry == null)
        {
            registry = ScriptableObject.CreateInstance<BlockingKingShaderRegistrySO>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }

        EnsureDrawSystemFallbackMaterial();
        EnsureDrawSystemGlassFallbackMaterial();

        registry.alwaysIncludedShaders = FindProjectShaders();
        registry.runtimeMaterials = FindProjectMaterials();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BlockingKingShaderRegistry] Updated registry: {RegistryPath}");
        SyncAlwaysIncludedShaders();
    }

    [MenuItem("Tools/BlockingKing/Rendering/Sync Always Included Shaders")]
    public static void SyncAlwaysIncludedShaders()
    {
        var registries = FindRegistries();
        if (registries.Count == 0)
        {
            Debug.LogWarning("[BlockingKingShaderRegistry] No registry found. Use Tools/BlockingKing/Rendering/Create or Update Shader Registry first.");
            return;
        }

        var shaders = new HashSet<Shader>();
        foreach (var registry in registries)
        {
            if (registry == null)
                continue;

            foreach (var shader in registry.EnumerateShaders())
                shaders.Add(shader);
        }

        if (shaders.Count == 0)
        {
            Debug.LogWarning("[BlockingKingShaderRegistry] Registry contains no shaders.");
            return;
        }

        var graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
        if (graphicsSettings == null)
        {
            Debug.LogError("[BlockingKingShaderRegistry] Could not load ProjectSettings/GraphicsSettings.asset.");
            return;
        }

        var serialized = new SerializedObject(graphicsSettings);
        var array = serialized.FindProperty("m_AlwaysIncludedShaders");
        if (array == null || !array.isArray)
        {
            Debug.LogError("[BlockingKingShaderRegistry] GraphicsSettings.m_AlwaysIncludedShaders not found.");
            return;
        }

        int removed = RemoveShadersByName(array, GlobalFallbackShadersToRemove);
        int added = 0;
        foreach (var shader in shaders)
        {
            if (ContainsShader(array, shader))
                continue;

            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            array.GetArrayElementAtIndex(index).objectReferenceValue = shader;
            added++;
        }

        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"[BlockingKingShaderRegistry] Synced Always Included Shaders. Registries={registries.Count}, shaders={shaders.Count}, added={added}, removedGlobalFallbacks={removed}.");
    }

    private static List<BlockingKingShaderRegistrySO> FindRegistries()
    {
        return AssetDatabase.FindAssets("t:BlockingKingShaderRegistrySO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<BlockingKingShaderRegistrySO>)
            .Where(registry => registry != null)
            .ToList();
    }

    private static List<Shader> FindProjectShaders()
    {
        return AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Shader>)
            .Where(shader => shader != null && shader.name.StartsWith("BlockingKing/"))
            .Distinct()
            .OrderBy(shader => shader.name)
            .ToList();
    }

    private static List<Material> FindProjectMaterials()
    {
        return AssetDatabase.FindAssets("t:Material", new[] { "Assets/Shaders" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(material => material != null && material.shader != null && material.shader.name.StartsWith("BlockingKing/"))
            .Distinct()
            .OrderBy(material => material.name)
            .ToList();
    }

    private static void EnsureDrawSystemFallbackMaterial()
    {
        EnsureFolder("Assets/Resources");

        var material = AssetDatabase.LoadAssetAtPath<Material>(DrawSystemFallbackMaterialPath);
        if (material != null)
            return;

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("[BlockingKingShaderRegistry] Could not create DrawSystem fallback material because no lit shader was found.");
            return;
        }

        material = new Material(shader)
        {
            name = "DrawSystemLitFallback",
            color = Color.white,
            enableInstancing = true
        };
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);

        AssetDatabase.CreateAsset(material, DrawSystemFallbackMaterialPath);
        Debug.Log($"[BlockingKingShaderRegistry] Created DrawSystem fallback material: {DrawSystemFallbackMaterialPath}");
    }

    private static void EnsureDrawSystemGlassFallbackMaterial()
    {
        EnsureFolder("Assets/Resources");

        var material = AssetDatabase.LoadAssetAtPath<Material>(DrawSystemGlassFallbackMaterialPath);
        if (material != null)
            return;

        var shader = Shader.Find("BlockingKing/BoxGlass");
        if (shader == null)
        {
            Debug.LogError("[BlockingKingShaderRegistry] Could not create DrawSystem glass fallback material because BlockingKing/BoxGlass was not found.");
            return;
        }

        material = new Material(shader)
        {
            name = "DrawSystemBoxGlassFallback",
            color = Color.white,
            renderQueue = 3050,
            enableInstancing = true
        };
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);

        AssetDatabase.CreateAsset(material, DrawSystemGlassFallbackMaterialPath);
        Debug.Log($"[BlockingKingShaderRegistry] Created DrawSystem glass fallback material: {DrawSystemGlassFallbackMaterialPath}");
    }

    private static bool ContainsShader(SerializedProperty array, Shader shader)
    {
        for (int i = 0; i < array.arraySize; i++)
        {
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                return true;
        }

        return false;
    }

    private static int RemoveShadersByName(SerializedProperty array, IEnumerable<string> shaderNames)
    {
        var names = new HashSet<string>(shaderNames);
        int removed = 0;

        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            var shader = array.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (shader == null || !names.Contains(shader.name))
                continue;

            array.DeleteArrayElementAtIndex(i);
            removed++;
        }

        return removed;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folder = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folder);
    }
}
