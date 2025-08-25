using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Client-side system that receives unified player stats from server
/// Updates both resource manager and scoreboard UI
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientPlayerStatsSystem : ISystem
{
    private Entity statsDataEntity;

    public void OnCreate(ref SystemState state)
    {
        // Create singleton entity to store current stats data for UI access
        statsDataEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(statsDataEntity, new CurrentPlayerStatsData
        {
            resource1 = 0,
            resource2 = 0,
            totalScore = 0,
            resource1Score = 0,
            resource2Score = 0,
            hasValidData = false
        });
        state.EntityManager.AddComponent<CurrentPlayerStatsDataSingleton>(statsDataEntity);
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var resourceManager = ResourceManager.Instance;

        // Process unified stats sync RPCs from server
        foreach (var (statsRpc, rpcEntity) in
            SystemAPI.Query<RefRO<SyncPlayerStatsRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            var newResource1 = statsRpc.ValueRO.resource1;
            var newResource2 = statsRpc.ValueRO.resource2;
            var totalScore = statsRpc.ValueRO.totalScore;
            var resource1Score = statsRpc.ValueRO.resource1Score;
            var resource2Score = statsRpc.ValueRO.resource2Score;

            // Update ResourceManager if it exists (backwards compatibility)
            if (resourceManager != null)
            {
                int currentR1 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource1);
                int currentR2 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource2);

                if (currentR1 != newResource1)
                {
                    int diff = newResource1 - currentR1;
                    if (diff > 0)
                        resourceManager.AddResource(ResourceManager.ResourceType.Resource1, diff);
                    else
                        resourceManager.RemoveResource(ResourceManager.ResourceType.Resource1, -diff);
                }

                if (currentR2 != newResource2)
                {
                    int diff = newResource2 - currentR2;
                    if (diff > 0)
                        resourceManager.AddResource(ResourceManager.ResourceType.Resource2, diff);
                    else
                        resourceManager.RemoveResource(ResourceManager.ResourceType.Resource2, -diff);
                }
            }

            // Update singleton data for UI access
            var statsData = new CurrentPlayerStatsData
            {
                resource1 = newResource1,
                resource2 = newResource2,
                totalScore = totalScore,
                resource1Score = resource1Score,
                resource2Score = resource2Score,
                hasValidData = true
            };

            state.EntityManager.SetComponentData(statsDataEntity, statsData);

            // Update scoreboard manager if it exists
            var scoreboardManager = ScoreboardManager.Instance;
            if (scoreboardManager != null)
            {
                scoreboardManager.UpdateLocalPlayerStats(totalScore, resource1Score, resource2Score,
                    newResource1, newResource2);
            }

            // Fire static events for loose coupling
            PlayerStatsUIEvents.OnLocalStatsChanged?.Invoke(newResource1, newResource2,
                totalScore, resource1Score, resource2Score);

            ecb.DestroyEntity(rpcEntity);
        }

        // Process resource refund RPCs (maintain backward compatibility)
        foreach (var (refund, rpcEntity) in
            SystemAPI.Query<RefRO<ResourceRefundRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (resourceManager != null)
            {
                resourceManager.AddResource(ResourceManager.ResourceType.Resource1, refund.ValueRO.resource1Amount);
                resourceManager.AddResource(ResourceManager.ResourceType.Resource2, refund.ValueRO.resource2Amount);
            }

            Debug.Log($"[Client] Resources refunded: R1:{refund.ValueRO.resource1Amount}, R2:{refund.ValueRO.resource2Amount}");
            ecb.DestroyEntity(rpcEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

/// <summary>
/// Singleton component to store current player stats for UI access
/// </summary>
public struct CurrentPlayerStatsData : IComponentData
{
    public int resource1;
    public int resource2;
    public int totalScore;
    public int resource1Score;
    public int resource2Score;
    public bool hasValidData;
}

/// <summary>
/// Tag component to mark the singleton entity
/// </summary>
public struct CurrentPlayerStatsDataSingleton : IComponentData { }

/// <summary>
/// Static events for UI communication - unified approach
/// </summary>
public static class PlayerStatsUIEvents
{
    public static System.Action OnStatsUpdated;
    public static System.Action<int, int, int, int, int> OnLocalStatsChanged; // r1, r2, totalScore, r1Score, r2Score
    public static System.Action<int, int, int, int, int, int> OnPlayerStatsChanged; // playerId, r1, r2, totalScore, r1Score, r2Score
}

/// <summary>
/// Unified query utilities for UI systems
/// </summary>
public static class PlayerStatsQueryUtils
{
    public static bool TryGetLocalPlayerStats(World clientWorld,
        out int resource1, out int resource2,
        out int totalScore, out int resource1Score, out int resource2Score)
    {
        resource1 = resource2 = totalScore = resource1Score = resource2Score = 0;

        if (clientWorld == null || !clientWorld.IsCreated) return false;

        var entityManager = clientWorld.EntityManager;

        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<CurrentPlayerStatsData>(),
            ComponentType.ReadOnly<CurrentPlayerStatsDataSingleton>()
        );

        if (query.CalculateEntityCount() == 0) return false;

        var statsData = query.GetSingleton<CurrentPlayerStatsData>();

        if (!statsData.hasValidData) return false;

        resource1 = statsData.resource1;
        resource2 = statsData.resource2;
        totalScore = statsData.totalScore;
        resource1Score = statsData.resource1Score;
        resource2Score = statsData.resource2Score;

        return true;
    }

    public static CurrentPlayerStatsData GetCurrentPlayerStats(World clientWorld)
    {
        if (clientWorld == null || !clientWorld.IsCreated)
            return new CurrentPlayerStatsData { hasValidData = false };

        var entityManager = clientWorld.EntityManager;

        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<CurrentPlayerStatsData>(),
            ComponentType.ReadOnly<CurrentPlayerStatsDataSingleton>()
        );

        if (query.CalculateEntityCount() == 0)
            return new CurrentPlayerStatsData { hasValidData = false };

        return query.GetSingleton<CurrentPlayerStatsData>();
    }
}