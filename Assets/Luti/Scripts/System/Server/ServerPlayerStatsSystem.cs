using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;


/// <summary>
/// Server-side system that manages unified player stats (resources + scores)
/// Simple, modular approach combining both resource and score management
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerPlayerStatsSystem : ISystem
{
    private const int STARTING_RESOURCE1 = 50;
    private const int STARTING_RESOURCE2 = 0;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();

        // Create config singleton if it doesn't exist
        if (!SystemAPI.HasSingleton<StatsConfig>())
        {
            var configEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(configEntity, StatsConfig.Default);
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var config = SystemAPI.GetSingleton<StatsConfig>();

        // Initialize stats for new players
        InitializeNewPlayers(ref state, ecb);

        // Process stats change events (resource changes with optional score awards)
        ProcessStatsChangeEvents(ref state, ecb, config);

        // Process direct score events (score without resource changes)
        ProcessDirectScoreEvents(ref state, ecb);

        // Process resource requests from clients (for testing/cheats)
        ProcessResourceRequests(ref state, ecb);

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private void InitializeNewPlayers(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (netId, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithNone<PlayerStats>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            // Initialize player stats
            ecb.AddComponent(entity, new PlayerStats
            {
                resource1 = STARTING_RESOURCE1,
                resource2 = STARTING_RESOURCE2,
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0
            });

            // Send initial sync to client
            SyncStatsToClient(ecb, entity, STARTING_RESOURCE1, STARTING_RESOURCE2, 0, 0, 0);
        }
    }

    private void ProcessStatsChangeEvents(ref SystemState state, EntityCommandBuffer ecb, StatsConfig config)
    {
        foreach (var (changeEvent, eventEntity) in
            SystemAPI.Query<RefRO<StatsChangeEvent>>()
            .WithEntityAccess())
        {
            var playerConnection = changeEvent.ValueRO.playerConnection;

            // Validate player connection
            if (!state.EntityManager.Exists(playerConnection) ||
                !SystemAPI.HasComponent<PlayerStats>(playerConnection))
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = SystemAPI.GetComponent<PlayerStats>(playerConnection);
            int resource1Delta = changeEvent.ValueRO.resource1Delta;
            int resource2Delta = changeEvent.ValueRO.resource2Delta;

            // Update resources
            stats.resource1 += resource1Delta;
            stats.resource2 += resource2Delta;

            // Ensure resources don't go negative
            stats.resource1 = math.max(0, stats.resource1);
            stats.resource2 = math.max(0, stats.resource2);

            // Award score points if specified (typically for resource gains)
            if (changeEvent.ValueRO.awardScorePoints)
            {
                int scoreIncrease = 0;
                int r1ScoreIncrease = 0;
                int r2ScoreIncrease = 0;

                if (resource1Delta > 0)
                {
                    r1ScoreIncrease = resource1Delta * config.pointsPerResource1;
                    scoreIncrease += r1ScoreIncrease;
                }

                if (resource2Delta > 0)
                {
                    r2ScoreIncrease = resource2Delta * config.pointsPerResource2;
                    scoreIncrease += r2ScoreIncrease;
                }

                if (scoreIncrease > 0)
                {
                    stats.totalScore += scoreIncrease;
                    stats.resource1Score += r1ScoreIncrease;
                    stats.resource2Score += r2ScoreIncrease;
                }
            }

            // Update the component
            ecb.SetComponent(playerConnection, stats);

            // Sync to client
            SyncStatsToClient(ecb, playerConnection,
                stats.resource1, stats.resource2,
                stats.totalScore, stats.resource1Score, stats.resource2Score);

            // Clean up event
            ecb.DestroyEntity(eventEntity);
        }
    }

    private void ProcessDirectScoreEvents(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (scoreEvent, eventEntity) in
            SystemAPI.Query<RefRO<DirectScoreEvent>>()
            .WithEntityAccess())
        {
            var playerConnection = scoreEvent.ValueRO.playerConnection;

            if (!state.EntityManager.Exists(playerConnection) ||
                !SystemAPI.HasComponent<PlayerStats>(playerConnection))
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = SystemAPI.GetComponent<PlayerStats>(playerConnection);
            stats.totalScore += scoreEvent.ValueRO.scorePoints;

            ecb.SetComponent(playerConnection, stats);

            // Sync to client
            SyncStatsToClient(ecb, playerConnection,
                stats.resource1, stats.resource2,
                stats.totalScore, stats.resource1Score, stats.resource2Score);

            ecb.DestroyEntity(eventEntity);
        }
    }

    private void ProcessResourceRequests(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (request, receiveRequest, rpcEntity) in
            SystemAPI.Query<RefRO<AddResourcesRpc>, RefRO<ReceiveRpcCommandRequest>>()
            .WithEntityAccess())
        {
            var connection = receiveRequest.ValueRO.SourceConnection;

            if (SystemAPI.HasComponent<PlayerStats>(connection))
            {
                // Create a stats change event for this request
                TriggerStatsChange(ecb, connection,
                    request.ValueRO.resource1ToAdd,
                    request.ValueRO.resource2ToAdd,
                    awardScorePoints: true); // Award points for manually added resources
            }

            ecb.DestroyEntity(rpcEntity);
        }
    }

    private static void SyncStatsToClient(EntityCommandBuffer ecb, Entity playerConnection,
        int resource1, int resource2, int totalScore, int resource1Score, int resource2Score)
    {
        var syncRpc = ecb.CreateEntity();
        ecb.AddComponent(syncRpc, new SyncPlayerStatsRpc
        {
            resource1 = resource1,
            resource2 = resource2,
            totalScore = totalScore,
            resource1Score = resource1Score,
            resource2Score = resource2Score
        });
        ecb.AddComponent(syncRpc, new SendRpcCommandRequest { TargetConnection = playerConnection });
    }

    // Public utility methods for other systems to use
    public static void TriggerStatsChange(EntityCommandBuffer ecb, Entity playerConnection,
        int resource1Delta, int resource2Delta, bool awardScorePoints = false)
    {
        if (resource1Delta != 0 || resource2Delta != 0)
        {
            var eventEntity = ecb.CreateEntity();
            ecb.AddComponent(eventEntity, new StatsChangeEvent
            {
                resource1Delta = resource1Delta,
                resource2Delta = resource2Delta,
                playerConnection = playerConnection,
                awardScorePoints = awardScorePoints
            });
        }
    }

    public static void AwardDirectScore(EntityCommandBuffer ecb, Entity playerConnection,
        int scorePoints, ScoreReason reason = ScoreReason.Custom)
    {
        if (scorePoints != 0)
        {
            var eventEntity = ecb.CreateEntity();
            ecb.AddComponent(eventEntity, new DirectScoreEvent
            {
                scorePoints = scorePoints,
                playerConnection = playerConnection,
                reason = reason
            });
        }
    }

    public static bool TrySpendResources(ref SystemState state, EntityCommandBuffer ecb,
        Entity connectionEntity, int resource1Cost, int resource2Cost)
    {
        if (!state.EntityManager.HasComponent<PlayerStats>(connectionEntity))
            return false;

        var stats = state.EntityManager.GetComponentData<PlayerStats>(connectionEntity);

        if (stats.resource1 >= resource1Cost && stats.resource2 >= resource2Cost)
        {
            // Spend resources (negative delta, no score award)
            TriggerStatsChange(ecb, connectionEntity, -resource1Cost, -resource2Cost, awardScorePoints: false);
            return true;
        }

        return false;
    }

    public static bool TryGetPlayerStats(ref SystemState state, Entity connectionEntity, out PlayerStats stats)
    {
        if (state.EntityManager.HasComponent<PlayerStats>(connectionEntity))
        {
            stats = state.EntityManager.GetComponentData<PlayerStats>(connectionEntity);
            return true;
        }

        stats = default;
        return false;
    }
}