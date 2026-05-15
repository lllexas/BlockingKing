using System.Text;

public static class LinearLevelValidation
{
    public static bool CanPlay(LinearLevelEntry entry, out string reason)
    {
        reason = string.Empty;

        if (entry == null)
        {
            reason = "Level entry is missing.";
            return false;
        }

        var level = entry.levelData;
        if (level == null)
        {
            reason = "LevelData is missing.";
            return false;
        }

        var issues = new StringBuilder();

        if (level.width <= 0 || level.height <= 0)
            AppendIssue(issues, "size is not initialized");

        int expectedTileCount = level.width * level.height;
        if (expectedTileCount <= 0 || level.tiles == null || level.tiles.Length != expectedTileCount)
            AppendIssue(issues, "tile data is invalid");

        if (!level.TryGetTagPosition(1, out _, out _))
            AppendIssue(issues, "player tag is missing");

        if (level.GetAllTagsOfType(2).Count <= 0)
            AppendIssue(issues, "box tag is missing");

        if (level.GetAllTagsOfType(3).Count <= 0 && level.GetAllTagsOfType(7).Count <= 0)
            AppendIssue(issues, "target tag is missing");

        reason = issues.ToString();
        return reason.Length == 0;
    }

    private static void AppendIssue(StringBuilder builder, string issue)
    {
        if (builder.Length > 0)
            builder.Append("; ");

        builder.Append(issue);
    }
}
