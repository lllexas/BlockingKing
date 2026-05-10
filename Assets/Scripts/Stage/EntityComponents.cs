using UnityEngine;

// ─────────── EntityType ───────────

public enum EntityType : byte
{
    None,
    Player,
    Box,
    Enemy,
    Target,
    Wall
}

// ─────────── IntentType ───────────

public enum IntentType : byte
{
    None,
    Move,
    Attack,
    Card,
    Spawn
}

// ─────────── CoreComponent ───────────

public struct CoreComponent
{
    public EntityType EntityType;
    public Vector2Int Position;
    public int Id;
    public bool OccupiesGrid;
}

// ─────────── StatusComponent ───────────

public struct StatusComponent
{
    public int BaseAttack;
    public int BaseMaxHealth;
    public int AttackModifier;
    public int MaxHealthModifier;
    public int DamageTaken;
    public int Block;
}

public static class CombatStats
{
    public static int GetAttack(in StatusComponent status)
    {
        return Mathf.Max(0, status.BaseAttack + status.AttackModifier);
    }

    public static int GetMaxHealth(in StatusComponent status)
    {
        return Mathf.Max(1, status.BaseMaxHealth + status.MaxHealthModifier);
    }

    public static int GetCurrentHealth(in StatusComponent status)
    {
        return GetMaxHealth(status) - Mathf.Max(0, status.DamageTaken);
    }

    public static void DealDamage(ref StatusComponent status, int damage)
    {
        if (damage <= 0)
            return;

        if (status.Block > 0)
        {
            int absorbed = Mathf.Min(status.Block, damage);
            status.Block -= absorbed;
            damage -= absorbed;
        }

        status.DamageTaken += Mathf.Max(0, damage);
    }
}

// ─────────── PropertyComponent ───────────

public struct PropertyComponent
{
    public int Attack;
    public int Parameter0;
    public int Parameter1;
    public int SourceTagId;
    public EntityBP SourceBP;
    public bool IsCore;
    public int SpawnInterval;
    public EntityBP SpawnEntityBP;
}

// ─────────── CounterComponent ───────────

/// <summary>
/// 通用计数器组件。NextTick 为触发时间戳，与 EntitySystem.GlobalTick 比较。
/// NextTick = 0 表示未启用。
/// </summary>
public struct CounterComponent
{
    public int NextTick;
}

// ─────────── IntentComponent ───────────

public struct IntentComponent
{
    public IntentType Type;
    public Intent Intent;
}

// ─────────── VisualMotionComponent ───────────

public enum VisualMotionType : byte
{
    None,
    Move,
    Rise
}

public struct VisualMotionComponent
{
    public VisualMotionType Type;
    public int SlotId;
    public Vector3 From;
    public Vector3 To;
    public float Start;
    public float End;

    public void Clear()
    {
        this = default;
    }

    public void Schedule(VisualMotionType type, int slotId, Vector3 from, Vector3 to, float start, float end)
    {
        Type = type;
        SlotId = slotId;
        From = from;
        To = to;
        Start = start;
        End = Mathf.Max(start + 0.001f, end);
    }

    public Vector3 Evaluate(float time, Vector3 logicalPosition)
    {
        if (Type == VisualMotionType.None)
            return logicalPosition;

        if (time < Start)
            return From;

        if (time >= End)
            return To;

        float t = Mathf.InverseLerp(Start, End, time);
        switch (Type)
        {
            case VisualMotionType.Move:
            case VisualMotionType.Rise:
                return Vector3.LerpUnclamped(From, To, EaseOutCubic(t));
            default:
                return logicalPosition;
        }
    }

    public bool IsComplete(float time)
    {
        return Type == VisualMotionType.None || time >= End;
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }
}

// ─────────── VisualImpulseComponent ───────────

public enum VisualImpulseType : byte
{
    None,
    Lunge
}

public struct VisualImpulseComponent
{
    public VisualImpulseType Type;
    public int SlotId;
    public Vector3 Offset;
    public float Start;
    public float End;

    public void Clear()
    {
        this = default;
    }

    public void Schedule(VisualImpulseType type, int slotId, Vector3 offset, float start, float end)
    {
        Type = type;
        SlotId = slotId;
        Offset = offset;
        Start = start;
        End = Mathf.Max(start + 0.001f, end);
    }

    public Vector3 Evaluate(float time)
    {
        if (Type == VisualImpulseType.None || time < Start || time >= End)
            return Vector3.zero;

        float t = Mathf.InverseLerp(Start, End, time);
        return Type switch
        {
            VisualImpulseType.Lunge => Offset * Mathf.Sin(t * Mathf.PI),
            _ => Vector3.zero
        };
    }

    public bool IsComplete(float time)
    {
        return Type == VisualImpulseType.None || time >= End;
    }
}

// ─────────── EntityComponents ───────────

public class EntityComponents
{
    public int entityCount;
    public int mapWidth;
    public int mapHeight;

    public CoreComponent[] coreComponents;
    public StatusComponent[] statusComponents;
    public PropertyComponent[] propertyComponents;
    public CounterComponent[] counterComponents;
    public IntentComponent[] intentComponents;
    public VisualMotionComponent[] visualMotionComponents;
    public VisualImpulseComponent[] visualImpulseComponents;

    public int[] groundMap;
    public int[] gridMap;
}
