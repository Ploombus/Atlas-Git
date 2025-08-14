using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Client-side system that monitors building selection and raises UI events
/// Runs in ECS, communicates with UI via events only
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct BuildingUISystem : ISystem
{
    private Entity lastSelectedBuilding;
    private int lastResource1;
    private int lastResource2;

    public void OnCreate(ref SystemState state)
    {
        lastSelectedBuilding = Entity.Null;
        lastResource1 = -1;
        lastResource2 = -1;
    }

    public void OnUpdate(ref SystemState state)
    {
        // Get the local player's resources from TesterUI's ResourceManager
        ResourceManager resourceManager = null;
        if (TesterUI.Instance != null)
        {
            var testerUI = TesterUI.Instance;
            resourceManager = ResourceManager.Instance;
        }

        if (resourceManager == null)
        {
            return;
        }

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
        // WithAll only matches enabled components by default
        Entity currentlySelectedBuilding = Entity.Null;
        int buildingCount = 0;
        int selectedCount = 0;

        // First, let's see how many buildings exist
        foreach (var (building, entity) in
            SystemAPI.Query<RefRO<Building>>()
            .WithEntityAccess())
        {
            buildingCount++;
        }

        // Now check for selected buildings
        foreach (var (building, entity) in
            SystemAPI.Query<RefRO<Building>>()
            .WithAll<Selected>()  // Only matches if Selected is enabled
            .WithEntityAccess())
        {
            selectedCount++;
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

        // Process spawn validation responses from server
        ProcessSpawnValidationResponses(ref state);
    }

    private void HandleBuildingSelection(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2)
    {
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

    private void ProcessSpawnValidationResponses(ref SystemState state)
    {
        // Check for resource refund RPCs from server
        foreach (var (refund, entity) in
            SystemAPI.Query<RefRO<ResourceRefundRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            var validationData = new SpawnValidationData
            {
                Success = false,
                RefundResource1 = refund.ValueRO.resource1Amount,
                RefundResource2 = refund.ValueRO.resource2Amount,
                Message = "Insufficient resources on server"
            };

            BuildingUIEvents.RaiseSpawnValidated(validationData);

            // Destroy the RPC entity
            state.EntityManager.DestroyEntity(entity);
        }
    }
}
