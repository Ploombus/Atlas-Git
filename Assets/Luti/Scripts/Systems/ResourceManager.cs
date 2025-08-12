using System.Collections.Generic;
using UnityEngine;

public class ResourceManager : MonoBehaviour 
{
    public enum ResourceType
    {
        Resource1,
        Resource2
    }

    private Dictionary<ResourceType, int> resources = new Dictionary<ResourceType, int>();
    public event System.Action<ResourceType, int> OnResourceChanged;

    private void Awake() 
    { 
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
        Debug.Log($"Added {amount} of {type}. New total: {resources[type]}");
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
        //Debug.Log($"Removed {amount} of {type}. New total: {resources[type]}");
        OnResourceChanged?.Invoke(type, resources[type]);
    }

    public int GetResourceAmount(ResourceType type) => resources[type];
}