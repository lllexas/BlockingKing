using UnityEngine;

public abstract class PoolBaseSO : ScriptableObject
{
    [Header("Identity")]
    public string poolId;
    public string displayName;

    [Header("State")]
    public bool enabled = true;

    public virtual string GetResolvedDisplayName()
    {
        return string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }
}

public abstract class PoolEntryBase
{
    [Min(0)]
    public int weight = 1;

    public bool enabled = true;
}
