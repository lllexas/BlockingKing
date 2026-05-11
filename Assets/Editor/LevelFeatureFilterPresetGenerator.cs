using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class LevelFeatureFilterPresetGenerator
{
    private const string OutputFolder = "Assets/Settings/LevelFeatureFilters";

    private sealed class Preset
    {
        public string fileName;
        public Vector2 width;
        public Vector2 height;
        public Vector2 area;
        public Vector2 wallRate;
        public Vector2 effectiveBoxes;
    }

    [MenuItem("Tools/BlockingKing/Generate Level Feature Filter Presets")]
    public static void GeneratePresets()
    {
        EnsureFolder("Assets/Settings");
        EnsureFolder(OutputFolder);

        foreach (var preset in BuildPresets())
        {
            string path = $"{OutputFolder}/{preset.fileName}.asset";
            var filter = AssetDatabase.LoadAssetAtPath<LevelFeatureFilterSO>(path);
            if (filter == null)
            {
                filter = ScriptableObject.CreateInstance<LevelFeatureFilterSO>();
                AssetDatabase.CreateAsset(filter, path);
            }

            filter.widthRange = preset.width;
            filter.heightRange = preset.height;
            filter.areaRange = preset.area;
            filter.wallRateRange = preset.wallRate;
            filter.effectiveBoxRange = preset.effectiveBoxes;
            EditorUtility.SetDirty(filter);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[LevelFeatureFilterPresetGenerator] Generated presets at {OutputFolder}");
    }

    private static List<Preset> BuildPresets()
    {
        return new List<Preset>
        {
            new Preset
            {
                fileName = "LFF_01_Tiny_Intro",
                width = new Vector2(5, 13),
                height = new Vector2(3, 11),
                area = new Vector2(15, 120),
                wallRate = new Vector2(0.35f, 0.55f),
                effectiveBoxes = new Vector2(1, 3)
            },
            new Preset
            {
                fileName = "LFF_02_Small_Easy",
                width = new Vector2(7, 15),
                height = new Vector2(7, 13),
                area = new Vector2(61, 160),
                wallRate = new Vector2(0.35f, 0.53f),
                effectiveBoxes = new Vector2(2, 5)
            },
            new Preset
            {
                fileName = "LFF_03_Standard_Mid",
                width = new Vector2(9, 18),
                height = new Vector2(9, 15),
                area = new Vector2(85, 253),
                wallRate = new Vector2(0.35f, 0.47f),
                effectiveBoxes = new Vector2(4, 8)
            },
            new Preset
            {
                fileName = "LFF_04_Transition_Hard",
                width = new Vector2(10, 22),
                height = new Vector2(9, 18),
                area = new Vector2(121, 350),
                wallRate = new Vector2(0.32f, 0.46f),
                effectiveBoxes = new Vector2(9, 20)
            },
            new Preset
            {
                fileName = "LFF_05_Large_Hard",
                width = new Vector2(13, 28),
                height = new Vector2(11, 21),
                area = new Vector2(201, 526),
                wallRate = new Vector2(0.25f, 0.43f),
                effectiveBoxes = new Vector2(21, 50)
            },
            new Preset
            {
                fileName = "LFF_06_Climax_Extreme",
                width = new Vector2(15, 35),
                height = new Vector2(13, 26),
                area = new Vector2(270, 800),
                wallRate = new Vector2(0.25f, 0.40f),
                effectiveBoxes = new Vector2(33, 100)
            },
            new Preset
            {
                fileName = "LFF_07_Breather_Open",
                width = new Vector2(10, 24),
                height = new Vector2(9, 20),
                area = new Vector2(120, 420),
                wallRate = new Vector2(0.20f, 0.35f),
                effectiveBoxes = new Vector2(4, 16)
            },
            new Preset
            {
                fileName = "LFF_08_Dense_Short",
                width = new Vector2(5, 15),
                height = new Vector2(5, 14),
                area = new Vector2(30, 180),
                wallRate = new Vector2(0.45f, 0.60f),
                effectiveBoxes = new Vector2(1, 8)
            },
            new Preset
            {
                fileName = "LFF_09_Hell_Optional",
                width = new Vector2(18, 49),
                height = new Vector2(16, 44),
                area = new Vector2(351, 2156),
                wallRate = new Vector2(0.20f, 0.45f),
                effectiveBoxes = new Vector2(51, 480)
            }
        };
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        int separator = folder.LastIndexOf('/');
        if (separator < 0)
            return;

        string parent = folder.Substring(0, separator);
        string name = folder.Substring(separator + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
