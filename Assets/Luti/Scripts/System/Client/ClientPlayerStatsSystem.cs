using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// DEBUG VERSION: Client system with extensive logging to trace data flow
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientPlayerStatsSystem : ISystem
{
    private float lastDebugTime;
    private const float DEBUG_INTERVAL = 2.0f;

    public void OnUpdate(ref SystemState state)
    {
        var resourceManager = ResourceManager.Instance;
        float currentTime = (float)SystemAPI.Time.ElapsedTime;

        // Debug logging every 2 seconds
        bool shouldDebug = currentTime - lastDebugTime >= DEBUG_INTERVAL;
        if (shouldDebug)
        {
            lastDebugTime = currentTime;
            Debug.Log("=== ClientPlayerStatsSystem Debug ===");

            if (resourceManager == null)
            {
                Debug.LogError("[Client DEBUG] ResourceManager.Instance is NULL!");
            }
            else
            {
                Debug.Log($"[Client DEBUG] ResourceManager: R1:{resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource1)} R2:{resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource2)}");
            }
        }

        // Get local player network ID first
        int localPlayerNetworkId = GetLocalPlayerNetworkId(ref state);

        if (localPlayerNetworkId == -1)
        {
            if (shouldDebug) Debug.LogWarning("[Client DEBUG] No local player network ID found");
            return;
        }

        if (shouldDebug) Debug.Log($"[Client DEBUG] Local player network ID: {localPlayerNetworkId}");

        // Count all PlayerStats components
        int playerStatsCount = 0;
        bool hasLocalPlayerUpdate = false;
        PlayerStats localPlayerStats = default;

        // Query ALL entities with PlayerStats (not just NetworkStreamConnection)
        foreach (var (stats, entity) in
            SystemAPI.Query<RefRO<PlayerStats>>()
            .WithEntityAccess())
        {
            playerStatsCount++;

            if (shouldDebug)
            {
                Debug.Log($"[Client DEBUG] Found PlayerStats - PlayerId:{stats.ValueRO.playerId} R1:{stats.ValueRO.resource1} R2:{stats.ValueRO.resource2} Score:{stats.ValueRO.totalScore}");
            }

            // Check if this is our local player
            if (stats.ValueRO.playerId == localPlayerNetworkId)
            {
                localPlayerStats = stats.ValueRO;
                hasLocalPlayerUpdate = true;

                if (shouldDebug) Debug.Log($"[Client DEBUG] FOUND LOCAL PLAYER STATS!");

                // Update ResourceManager using existing methods
                if (resourceManager != null)
                {
                    SyncResourceManager(resourceManager, stats.ValueRO.resource1, stats.ValueRO.resource2);
                    if (shouldDebug) Debug.Log($"[Client DEBUG] Updated ResourceManager to R1:{stats.ValueRO.resource1} R2:{stats.ValueRO.resource2}");
                }
                else
                {
                    Debug.LogError("[Client DEBUG] ResourceManager is null, cannot sync!");
                }
            }
        }

        if (shouldDebug)
        {
            Debug.Log($"[Client DEBUG] Total PlayerStats found: {playerStatsCount}");
            Debug.Log($"[Client DEBUG] Has local player update: {hasLocalPlayerUpdate}");
        }

        // If we found local player stats, trigger UI updates
        if (hasLocalPlayerUpdate)
        {
            // Trigger local player UI update
            PlayerStatsUIEvents.OnLocalStatsChanged?.Invoke(
                localPlayerStats.resource1, localPlayerStats.resource2,
                localPlayerStats.totalScore, localPlayerStats.resource1Score, localPlayerStats.resource2Score);

            // Trigger general stats update for scoreboard
            PlayerStatsUIEvents.OnAllPlayerStatsUpdated?.Invoke();

            if (shouldDebug) Debug.Log($"[Client DEBUG] Triggered UI events");
        }
    }

    /// <summary>
    /// Get local player network ID using the same pattern as existing code
    /// </summary>
    private int GetLocalPlayerNetworkId(ref SystemState state)
    {
        // First try to find using GhostOwnerIsLocal (preferred method)
        foreach (var (ghostOwner, entity) in
            SystemAPI.Query<RefRO<GhostOwner>>()
            .WithAll<GhostOwnerIsLocal>()
            .WithEntityAccess())
        {
            return ghostOwner.ValueRO.NetworkId;
        }

        // Fallback: find the first NetworkStreamConnection (client connection)
        foreach (var (netId, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            return netId.ValueRO.Value;
        }

        return -1; // No local player found
    }

    /// <summary>
    /// Sync ResourceManager using existing AddResource/RemoveResource methods
    /// </summary>
    private void SyncResourceManager(ResourceManager resourceManager, int targetResource1, int targetResource2)
    {
        // Get current amounts
        int currentR1 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource1);
        int currentR2 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource2);

        // Sync Resource1
        if (currentR1 != targetResource1)
        {
            int diff = targetResource1 - currentR1;
            if (diff > 0)
                resourceManager.AddResource(ResourceManager.ResourceType.Resource1, diff);
            else if (diff < 0)
                resourceManager.RemoveResource(ResourceManager.ResourceType.Resource1, -diff);

            Debug.Log($"[Client DEBUG] Synced R1: {currentR1} -> {targetResource1} (diff: {diff})");
        }

        // Sync Resource2
        if (currentR2 != targetResource2)
        {
            int diff = targetResource2 - currentR2;
            if (diff > 0)
                resourceManager.AddResource(ResourceManager.ResourceType.Resource2, diff);
            else if (diff < 0)
                resourceManager.RemoveResource(ResourceManager.ResourceType.Resource2, -diff);

            Debug.Log($"[Client DEBUG] Synced R2: {currentR2} -> {targetResource2} (diff: {diff})");
        }
    }
}

/// <summary>
/// Static events for UI communication
/// </summary>
public static class PlayerStatsUIEvents
{
    public static System.Action<int, int, int, int, int> OnLocalStatsChanged; // r1, r2, totalScore, r1Score, r2Score
    public static System.Action OnAllPlayerStatsUpdated; // Signals scoreboard to refresh
}