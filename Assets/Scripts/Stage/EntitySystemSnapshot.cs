using System.Collections.Generic;

public sealed class EntitySystemSnapshot
{
    public bool Initialized;
    public int VersionEpoch;
    public int MaxEntityCount;
    public int EntityCount;
    public int MapWidth;
    public int MapHeight;
    public int TerrainVersion;
    public int GlobalTick;
    public int DefaultFloorTerrainId;
    public List<int> WallTerrainIds;
    public CoreComponent[] CoreComponents;
    public StatusComponent[] StatusComponents;
    public PropertyComponent[] PropertyComponents;
    public CounterComponent[] CounterComponents;
    public IntentComponent[] IntentComponents;
    public VisualMotionComponent[] VisualMotionComponents;
    public VisualImpulseComponent[] VisualImpulseComponents;
    public int[] GroundMap;
    public int[] GridMap;
    public int[] IdToDataIndex;
    public int[] DataIndexToId;
    public int[] IdVersions;
    public int[] FreeIds;
}
