public static class MainModel
{
    private static UserModel _user;

    public static UserModel User
    {
        get
        {
            if (_user == null)
                _user = SaveManager.Load();

            _user.EnsureInitialized();
            return _user;
        }
    }

    public static void ReplaceUser(UserModel user)
    {
        _user = user ?? UserModel.CreateDefault();
        _user.EnsureInitialized();
    }

    public static void Save()
    {
        SaveManager.Save(User);
    }

    public static void Reload()
    {
        _user = SaveManager.Load();
    }
}
