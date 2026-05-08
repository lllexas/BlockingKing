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
    Attack
}

// ─────────── CoreComponent ───────────

public struct CoreComponent
{
    public EntityType EntityType;
    public Vector2Int Position;
    public int Id;
    public int Health;
    public bool OccupiesGrid;
}

// ─────────── PropertyComponent ───────────

public struct PropertyComponent
{
    public int Attack;
    public int Parameter0;
    public int Parameter1;
    public bool IsCore;
}

// ─────────── IntentComponent ───────────

public struct IntentComponent
{
    public IntentType Type;
    public Intent Intent;
}

// ─────────── EntityComponents ───────────

public class EntityComponents
{
    public int entityCount;
    public int mapWidth;
    public int mapHeight;

    public CoreComponent[] coreComponents;
    public PropertyComponent[] propertyComponents;
    public IntentComponent[] intentComponents;

    public int[] groundMap;
    public int[] gridMap;
}
