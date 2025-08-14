using System;
using Unity.Entities;

/// <summary>
/// Static event manager for building UI communication between ECS and MonoBehaviours
/// </summary>
public static class BuildingUIEvents
{
    public static event Action<BuildingSelectedEventData> OnBuildingSelected;

    public static event Action OnBuildingDeselected;

    // Event fired when building spawn cost data needs to be displayed
    public static event Action<SpawnCostUIData> OnSpawnCostUpdated;

    // Event fired when player resources change (affects affordability)
    public static event Action<ResourceUIData> OnResourcesUpdated;

    // Event fired when a spawn is validated/rejected by server
    public static event Action<SpawnValidationData> OnSpawnValidated;

    // Methods to fire events (called from ECS systems)
    public static void RaiseBuildingSelected(BuildingSelectedEventData data)
    {
        OnBuildingSelected?.Invoke(data);
    }

    public static void RaiseBuildingDeselected()
    {
        OnBuildingDeselected?.Invoke();
    }

    public static void RaiseSpawnCostUpdated(SpawnCostUIData data)
    {
        OnSpawnCostUpdated?.Invoke(data);
    }

    public static void RaiseResourcesUpdated(ResourceUIData data)
    {
        OnResourcesUpdated?.Invoke(data);
    }

    public static void RaiseSpawnValidated(SpawnValidationData data)
    {
        OnSpawnValidated?.Invoke(data);
    }
}

// Data structures for events
public struct BuildingSelectedEventData
{
    public Entity BuildingEntity;
    public bool HasSpawnCapability;
    public int Resource1Cost;
    public int Resource2Cost;
}

public struct SpawnCostUIData
{
    public Entity BuildingEntity;
    public int Resource1Cost;
    public int Resource2Cost;
    public bool CanAfford;
}

public struct ResourceUIData
{
    public int CurrentResource1;
    public int CurrentResource2;
    public int RequiredResource1;  // For current selection
    public int RequiredResource2;
    public bool CanAffordCurrent;
}

public struct SpawnValidationData
{
    public bool Success;
    public int RefundResource1;
    public int RefundResource2;
    public string Message;
}