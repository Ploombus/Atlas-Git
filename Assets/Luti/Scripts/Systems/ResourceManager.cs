using System.Collections.Generic;
using UnityEngine;

public class ResourceManager : MonoBehaviour 
{
    public static ResourceManager Instance { get; private set; }


    public enum ResourceType
    {
        Resource1,
        Resource2
    }

    private Dictionary<ResourceType, int> resources = new Dictionary<ResourceType, int>();
    public event System.Action<ResourceType, int> OnResourceChanged;

    private void Awake() 
    {
        Instance = this;

        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            resources[type] = 0;
        }
    }
    public void AddResource(ResourceType type, int amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning("Attempted to add a negative amount. Use RemoveResource instead.");
            return;
        }

        resources[type] += amount;
        OnResourceChanged?.Invoke(type, resources[type]);
    }

    public void RemoveResource(ResourceType type, int amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning("Attempted to remove a negative amount. Use AddResource instead.");
            return;
        }

        if (resources[type] < amount)
        {
            Debug.LogWarning($"Not enough {type} to remove. Current: {resources[type]}, Tried to remove: {amount}");
            return;
        }

        resources[type] -= amount;
        OnResourceChanged?.Invoke(type, resources[type]);
    }

    public int GetResourceAmount(ResourceType type) => resources[type];

    public bool HasResources(int resource1Amount, int resource2Amount)
    {
        return resources[ResourceType.Resource1] >= resource1Amount &&
               resources[ResourceType.Resource2] >= resource2Amount;
    }

    public bool TrySpendResources(int resource1Amount, int resource2Amount)
    {
        if (HasResources(resource1Amount, resource2Amount))
        {
            // Only deduct if we have enough of both
            if (resource1Amount > 0)
                RemoveResource(ResourceType.Resource1, resource1Amount);
            if (resource2Amount > 0)
                RemoveResource(ResourceType.Resource2, resource2Amount);
            return true;
        }
        return false;
    }

    public void GetMissingResources(int resource1Needed, int resource2Needed,
        out int missingResource1, out int missingResource2)
    {
        missingResource1 = Mathf.Max(0, resource1Needed - resources[ResourceType.Resource1]);
        missingResource2 = Mathf.Max(0, resource2Needed - resources[ResourceType.Resource2]);
    }
}



