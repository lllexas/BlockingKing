using System.IO;
using UnityEditor;
using UnityEngine;

public static class BlockingKingUnitMeshGeneratorEditor
{
    private const string OutputFolder = "Assets/Resources/GeneratedMeshes/Units";

    [MenuItem("Tools/BlockingKing/Rendering/Generate Unit Meshes")]
    public static void GenerateUnitMeshes()
    {
        EnsureFolder(OutputFolder);

        foreach (BlockingKingUnitMeshKind kind in System.Enum.GetValues(typeof(BlockingKingUnitMeshKind)))
        {
            string assetName = BlockingKingUnitMeshFactory.GetAssetName(kind);
            string path = $"{OutputFolder}/{assetName}.asset";
            Mesh mesh = BlockingKingUnitMeshFactory.Create(kind);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = assetName;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Unit Meshes Generated", $"Generated unit meshes in:\n{OutputFolder}", "OK");
    }

    private static void EnsureFolder(string folder)
    {
        string normalized = folder.Replace("\\", "/").Trim('/');
        string[] parts = normalized.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
    }
}
