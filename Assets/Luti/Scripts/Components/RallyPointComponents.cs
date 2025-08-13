using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

// Component to store the rally point for a building
[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct BuildingRallyPoint : IComponentData
{
    [GhostField] public float3 rallyPosition;
    [GhostField] public bool hasRallyPoint;
}

// Request component for setting a rally point (client to server)
public struct SetRallyPointRequest : IRpcCommand
{
    public Entity buildingEntity;
    public float3 rallyPosition;
    public int ownerNetworkId;
}

// Tag component for units that need to move to rally point after spawning
public struct MoveToRallyPoint : IComponentData
{
    public float3 targetPosition;
}