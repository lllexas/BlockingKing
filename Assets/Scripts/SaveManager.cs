using System;
using System.IO;
using UnityEngine;

public static class SaveManager
{
    private const string SaveFileName = "user_model.json";

    public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    public static UserModel Load()
    {
        try
        {
            if (!File.Exists(SavePath))
                return UserModel.CreateDefault();

            string json = File.ReadAllText(SavePath);
            var model = JsonUtility.FromJson<UserModel>(json);
            if (model == null)
                return UserModel.CreateDefault();

            model.EnsureInitialized();
            return model;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Failed to load save: {e.Message}");
            return UserModel.CreateDefault();
        }
    }

    public static bool Save(UserModel model)
    {
        try
        {
            model ??= UserModel.CreateDefault();
            model.EnsureInitialized();

            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(model, true);
            File.WriteAllText(SavePath, json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Failed to save: {e.Message}");
            return false;
        }
    }
}
