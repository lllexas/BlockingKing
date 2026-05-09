using System;
using UnityEngine;

[Serializable]
public sealed class CardHandState
{
    [SerializeField] private int targetHandCount = 10;
    [SerializeField] private int maxHandCount = 10;
    [SerializeField] private bool autoRefill = true;
    [SerializeField] private int revision;

    public int TargetHandCount => targetHandCount;
    public int MaxHandCount => maxHandCount;
    public bool AutoRefill => autoRefill;
    public int Revision => revision;

    public void SetTargetHandCount(int value)
    {
        int clamped = Mathf.Max(0, Mathf.Min(value, maxHandCount));
        if (targetHandCount == clamped)
            return;

        targetHandCount = clamped;
        revision++;
    }

    public void SetMaxHandCount(int value)
    {
        int clamped = Mathf.Max(0, value);
        if (maxHandCount == clamped)
            return;

        maxHandCount = clamped;
        if (targetHandCount > maxHandCount)
            targetHandCount = maxHandCount;

        revision++;
    }

    public void SetAutoRefill(bool value)
    {
        if (autoRefill == value)
            return;

        autoRefill = value;
        revision++;
    }

    public void MarkDirty()
    {
        revision++;
    }
}
