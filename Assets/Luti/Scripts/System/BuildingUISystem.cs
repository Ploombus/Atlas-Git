using Unity.Entities;
using Unity.NetCode;
using UnityEngine;


[UpdateInGroup(typeof(PresentationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct BuildingUISystem : ISystem
{
    private Entity lastSelectedBuilding;
    private int lastResource1;
    private int lastResource2;
    private bool hasLoggedResourceWarning;

    public void OnCreate(ref SystemState state)
    {
        lastSelectedBuilding = Entity.Null;
        lastResource1 = -1;
        lastResource2 = -1;
        hasLoggedResourceWarning = false;
    }

    public void OnUpdate(ref SystemState state)
    {
        // Get ResourceManager but don't spam if it's missing
        var resourceManager = ResourceManager.Instance;
        if (resourceManager == null)
        {
            if (!hasLoggedResourceWarning)
            {
                Debug.LogWarning("BuildingUISystem: ResourceManager.Instance is null. Will retry silently.");
                hasLoggedResourceWarning = true;
            }
            return;
        }

        // Reset warning flag if we found the manager
        hasLoggedResourceWarning = false;

        int currentResource1 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource1);
        int currentResource2 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource2);

        // Check for resource changes
        if (currentResource1 != lastResource1 || currentResource2 != lastResource2)
        {
            lastResource1 = currentResource1;
            lastResource2 = currentResource2;

            // If we have a selected building, update affordability
            if (lastSelectedBuilding != Entity.Null && state.EntityManager.Exists(lastSelectedBuilding))
            {
                UpdateBuildingAffordability(ref state, lastSelectedBuilding, currentResource1, currentResource2);
            }
        }

        // Monitor for buildings with ENABLED Selected component
        Entity currentlySelectedBuilding = Entity.Null;
        foreach (var (building, entity) in
            SystemAPI.Query<RefRO<Building>>()
            .WithAll<Selected>()  // Only matches if Selected is enabled
            .WithEntityAccess())
        {
            currentlySelectedBuilding = entity;

            if (entity != lastSelectedBuilding)
            {
                lastSelectedBuilding = entity;
                HandleBuildingSelection(ref state, entity, currentResource1, currentResource2);
            }
        }

        // If we had a selected building but don't anymore, it was deselected
        if (lastSelectedBuilding != Entity.Null && currentlySelectedBuilding == Entity.Null)
        {
            lastSelectedBuilding = Entity.Null;
            BuildingUIEvents.RaiseBuildingDeselected();
        }
    }

    private void HandleBuildingSelection(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2)
    {
        // Get building owner
        int buildingOwnerNetworkId = -1;
        if (state.EntityManager.HasComponent<GhostOwner>(buildingEntity))
        {
            var owner = state.EntityManager.GetComponentData<GhostOwner>(buildingEntity);
            buildingOwnerNetworkId = owner.NetworkId;
        }

        int localPlayerNetworkId = -1;

        foreach (var (netId, entity) in
            SystemAPI.Query<RefRO<GhostOwner>>()
            .WithAll<GhostOwnerIsLocal>()
            .WithEntityAccess())
        {
            localPlayerNetworkId = netId.ValueRO.NetworkId;
            break;
        }

        if (localPlayerNetworkId == -1)
        {
            foreach (var (netId, entity) in
                SystemAPI.Query<RefRO<NetworkId>>()
                .WithAll<NetworkStreamConnection>()
                .WithEntityAccess())
            {
                localPlayerNetworkId = netId.ValueRO.Value;
                break;
            }
        }


        // Only show UI if the local player owns the building
        if (buildingOwnerNetworkId != localPlayerNetworkId)
        {
            return;
        }

        // Owner - proceed with showing UI
        var eventData = new BuildingSelectedEventData
        {
            BuildingEntity = buildingEntity,
            HasSpawnCapability = state.EntityManager.HasComponent<BuildingSpawnQueue>(buildingEntity)
        };

        // Get spawn cost if available
        if (state.EntityManager.HasComponent<UnitSpawnCost>(buildingEntity))
        {
            var cost = state.EntityManager.GetComponentData<UnitSpawnCost>(buildingEntity);
            eventData.Resource1Cost = cost.unitResource1Cost;
            eventData.Resource2Cost = cost.unitResource2Cost;
        }

        BuildingUIEvents.RaiseBuildingSelected(eventData);

        // Also send cost/affordability update
        UpdateBuildingAffordability(ref state, buildingEntity, currentResource1, currentResource2);
    }

    private void UpdateBuildingAffordability(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2)
    {
        if (!state.EntityManager.HasComponent<UnitSpawnCost>(buildingEntity))
            return;

        var cost = state.EntityManager.GetComponentData<UnitSpawnCost>(buildingEntity);

        var costData = new SpawnCostUIData
        {
            BuildingEntity = buildingEntity,
            Resource1Cost = cost.unitResource1Cost,
            Resource2Cost = cost.unitResource2Cost,
            CanAfford = currentResource1 >= cost.unitResource1Cost &&
                       currentResource2 >= cost.unitResource2Cost
        };

        BuildingUIEvents.RaiseSpawnCostUpdated(costData);

        // Also update general resource UI
        var resourceData = new ResourceUIData
        {
            CurrentResource1 = currentResource1,
            CurrentResource2 = currentResource2,
            RequiredResource1 = cost.unitResource1Cost,
            RequiredResource2 = cost.unitResource2Cost,
            CanAffordCurrent = costData.CanAfford
        };

        BuildingUIEvents.RaiseResourcesUpdated(resourceData);
    }
}