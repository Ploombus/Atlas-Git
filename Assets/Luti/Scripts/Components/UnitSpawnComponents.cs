using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

// Server-side component (existing)
public struct BuildingSpawnQueue : IComponentData
{
    public int unitsInQueue;
    public bool isCurrentlySpawning;
    public float timeToSpawnUnit;
}

// Client-side networked component - FIXED VERSION
[GhostComponent(PrefabType = GhostPrefabType.All, OwnerSendType = SendToOwnerType.All)]
public struct BuildingSpawnQueueClient : IComponentData, IEnableableComponent
{
    [GhostField] public int unitsInQueue;
    [GhostField] public bool isCurrentlySpawning;
}

// Alternative: If the above doesn't work, try this simpler version
/*
public struct BuildingSpawnQueueClient : IComponentData
{
    [GhostField] public int unitsInQueue;
    [GhostField] public bool isCurrentlySpawning;
}
*/

// Other existing components (no changes needed)
public struct PendingUnitSpawn : IComponentData
{
    public Entity buildingEntity;
    public int ownerNetworkId;
    public float3 spawnPosition;
    public float remainingTime;
    public float totalSpawnTime;
}

public struct QueuedUnitSpawn : IComponentData
{
    public Entity buildingEntity;
    public int ownerNetworkId;
    public float3 spawnPosition;
}

public struct UnitSpawnTimer : IComponentData
{
    public float timeToSpawnUnit;
}