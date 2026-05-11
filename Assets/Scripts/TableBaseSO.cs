using Sirenix.OdinInspector;
using UnityEngine;

public abstract class TableBaseSO : ScriptableObject
{
    [Title("Table")]
    public string tableId;

    public string displayName;

    [TextArea]
    public string description;

    public bool enabled = true;

    public string GetResolvedDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        return !string.IsNullOrWhiteSpace(tableId) ? tableId : name;
    }
}
