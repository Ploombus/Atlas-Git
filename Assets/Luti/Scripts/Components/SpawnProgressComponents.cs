using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// Client-side component to track spawn progress for UI display
/// Synced from server-side PendingUnitSpawn component
/// </summary>
[GhostComponent(PrefabType = GhostPrefabType.All, OwnerSendType = SendToOwnerType.All)]
public struct BuildingSpawnProgress : IComponentData, IEnableableComponent
{
    [GhostField] public float currentSpawnTime;
    [GhostField] public float totalSpawnTime;
    [GhostField] public int currentUnitIndex;
    [GhostField] public int totalUnitsInQueue;
    [GhostField] public bool isSpawning;
}

/// <summary>
/// Component to mark buildings that should show spawn progress UI
/// Note: Prefab and parent references are managed by SpawnProgressUIManager singleton
/// This component just marks entities that need UI
/// </summary>
public struct SpawnProgressUIReference : IComponentData
{
    public bool enableProgressUI;
}

/// <summary>
/// Component to link building entities with their UI instances
/// Uses an ID instead of direct GameObject reference for ECS compatibility
/// </summary>
public struct SpawnProgressUIInstance : IComponentData
{
    public int uiInstanceId;  // ID to lookup UI instance in manager
    public bool isActive;
}