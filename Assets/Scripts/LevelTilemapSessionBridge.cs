#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public struct LevelTilemapSession
{
    public string LevelDataGUID;
    public string ReturnScenePath;
    public SceneSetup[] SceneSetup;
}

public static class LevelTilemapSessionBridge
{
    private const string Prefix = "BlockingKing_TilemapEditor_";

    public static bool IsReturning
        => EditorPrefs.GetBool(Prefix + "IsReturning", false);

    public static void SetSession(string levelDataGUID, string returnScenePath, SceneSetup[] sceneSetup)
    {
        EditorPrefs.SetString(Prefix + "LevelDataGUID", levelDataGUID ?? string.Empty);
        EditorPrefs.SetString(Prefix + "ReturnScenePath", returnScenePath ?? string.Empty);
        EditorPrefs.SetString(Prefix + "SceneSetupJson", SceneSetupUtil.Serialize(sceneSetup));
        EditorPrefs.SetBool(Prefix + "IsReturning", false);
    }

    public static void MarkReturning()
    {
        EditorPrefs.SetBool(Prefix + "IsReturning", true);
    }

    public static bool TryGetSession(out LevelTilemapSession session)
    {
        session = new LevelTilemapSession
        {
            LevelDataGUID = EditorPrefs.GetString(Prefix + "LevelDataGUID", string.Empty),
            ReturnScenePath = EditorPrefs.GetString(Prefix + "ReturnScenePath", string.Empty),
            SceneSetup = SceneSetupUtil.Deserialize(
                EditorPrefs.GetString(Prefix + "SceneSetupJson", string.Empty))
        };

        return !string.IsNullOrEmpty(session.LevelDataGUID);
    }

    public static void ClearSession()
    {
        EditorPrefs.DeleteKey(Prefix + "LevelDataGUID");
        EditorPrefs.DeleteKey(Prefix + "ReturnScenePath");
        EditorPrefs.DeleteKey(Prefix + "SceneSetupJson");
        EditorPrefs.DeleteKey(Prefix + "IsReturning");
    }
}

public static class SceneSetupUtil
{
    [Serializable]
    private class StateCollection
    {
        public List<SceneState> scenes = new List<SceneState>();
    }

    [Serializable]
    private struct SceneState
    {
        public string path;
        public bool isLoaded;
        public bool isActive;
    }

    public static string Serialize(SceneSetup[] setup)
    {
        var collection = new StateCollection();
        if (setup != null)
        {
            foreach (var s in setup)
            {
                collection.scenes.Add(new SceneState
                {
                    path = s.path,
                    isLoaded = s.isLoaded,
                    isActive = s.isActive
                });
            }
        }
        return JsonUtility.ToJson(collection);
    }

    public static SceneSetup[] Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var collection = JsonUtility.FromJson<StateCollection>(json);
            if (collection?.scenes == null || collection.scenes.Count == 0)
                return null;

            var setup = new SceneSetup[collection.scenes.Count];
            for (int i = 0; i < collection.scenes.Count; i++)
            {
                var s = collection.scenes[i];
                setup[i] = new SceneSetup
                {
                    path = s.path,
                    isLoaded = s.isLoaded,
                    isActive = s.isActive
                };
            }
            return setup;
        }
        catch
        {
            return null;
        }
    }
}
#endif
