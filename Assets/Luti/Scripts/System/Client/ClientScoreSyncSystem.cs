using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Client-side system that receives score updates from server
/// Handles score synchronization and triggers UI updates
/// Enhanced with direct UI integration support
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientScoreSyncSystem : ISystem
{
    // Singleton component to store current scores for UI access
    private Entity scoreDataEntity;

    public void OnCreate(ref SystemState state)
    {
        // Create singleton entity to store current score data
        scoreDataEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(scoreDataEntity, new CurrentScoreData
        {
            totalScore = 0,
            resource1Score = 0,
            resource2Score = 0,
            hasValidData = false
        });

        state.EntityManager.AddComponent<CurrentScoreDataSingleton>(scoreDataEntity);
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        bool scoreUpdated = false;

        // Process score sync RPCs from server
        foreach (var (scoreRpc, rpcEntity) in
            SystemAPI.Query<RefRO<SyncScoreRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            var totalScore = scoreRpc.ValueRO.totalScore;
            var r1Score = scoreRpc.ValueRO.resource1Score;
            var r2Score = scoreRpc.ValueRO.resource2Score;

            // Update singleton score data
            UpdateStoredScoreData(ref state, totalScore, r1Score, r2Score);

            // Notify UI systems about score update
            NotifyUIUpdate(totalScore, r1Score, r2Score);

            Debug.Log($"[Client] Score Updated - Total: {totalScore}, R1: {r1Score}, R2: {r2Score}");

            ecb.DestroyEntity(rpcEntity);
            scoreUpdated = true;
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();

        // Fire score update event if scores changed
        if (scoreUpdated)
        {
            ScoreUIEvents.OnScoreUpdated?.Invoke();
        }
    }

    private void UpdateStoredScoreData(ref SystemState state, int totalScore, int resource1Score, int resource2Score)
    {
        var currentData = SystemAPI.GetComponent<CurrentScoreData>(scoreDataEntity);
        currentData.totalScore = totalScore;
        currentData.resource1Score = resource1Score;
        currentData.resource2Score = resource2Score;
        currentData.hasValidData = true;

        SystemAPI.SetComponent(scoreDataEntity, currentData);
    }

    private void NotifyUIUpdate(int totalScore, int resource1Score, int resource2Score)
    {
        // Try to find and update ScoreboardManager directly
        var scoreboardManager = Object.FindFirstObjectByType<ScoreboardManager>();
        if (scoreboardManager != null)
        {
            scoreboardManager.UpdateLocalPlayerScore(totalScore, resource1Score, resource2Score);
        }

        // Also fire static events for loose coupling
        ScoreUIEvents.OnLocalScoreChanged?.Invoke(totalScore, resource1Score, resource2Score);
    }
}

/// <summary>
/// Singleton component to store current score data for UI access
/// </summary>
public struct CurrentScoreData : IComponentData
{
    public int totalScore;
    public int resource1Score;
    public int resource2Score;
    public bool hasValidData;
}

/// <summary>
/// Tag component to mark the singleton entity
/// </summary>
public struct CurrentScoreDataSingleton : IComponentData { }

/// <summary>
/// Static events for UI communication - simple and decoupled approach
/// </summary>
public static class ScoreUIEvents
{
    public static System.Action OnScoreUpdated;
    public static System.Action<int, int, int> OnLocalScoreChanged; // totalScore, resource1Score, resource2Score
    public static System.Action<int, int, int, int> OnPlayerScoreChanged; // playerId, totalScore, resource1Score, resource2Score
}

/// <summary>
/// Enhanced score query utilities for UI systems
/// </summary>
public static class ScoreQueryUtils
{
    /// <summary>
    /// Get current local player score from the stored singleton
    /// Returns false if no score data available
    /// </summary>
    public static bool TryGetLocalPlayerScore(World clientWorld, out int totalScore,
        out int resource1Score, out int resource2Score)
    {
        totalScore = 0;
        resource1Score = 0;
        resource2Score = 0;

        if (clientWorld == null || !clientWorld.IsCreated) return false;

        var entityManager = clientWorld.EntityManager;

        // Find the singleton entity with current score data
        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<CurrentScoreData>(),
            ComponentType.ReadOnly<CurrentScoreDataSingleton>()
        );

        if (query.CalculateEntityCount() == 0) return false;

        var scoreData = query.GetSingleton<CurrentScoreData>();

        if (!scoreData.hasValidData) return false;

        totalScore = scoreData.totalScore;
        resource1Score = scoreData.resource1Score;
        resource2Score = scoreData.resource2Score;

        return true;
    }

    /// <summary>
    /// Get the current score data directly (for UI systems)
    /// </summary>
    public static CurrentScoreData GetCurrentScoreData(World clientWorld)
    {
        if (clientWorld == null || !clientWorld.IsCreated)
            return new CurrentScoreData { hasValidData = false };

        var entityManager = clientWorld.EntityManager;

        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<CurrentScoreData>(),
            ComponentType.ReadOnly<CurrentScoreDataSingleton>()
        );

        if (query.CalculateEntityCount() == 0)
            return new CurrentScoreData { hasValidData = false };

        return query.GetSingleton<CurrentScoreData>();
    }
}