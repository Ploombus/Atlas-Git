using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// FIXED: Server system that creates proper ghosted player entities
/// Connection entities don't replicate - we need separate player entities
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerPlayerStatsSystem : ISystem
{
    private const int STARTING_RESOURCE1 = 100;
    private const int STARTING_RESOURCE2 = 100;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();

        // Create config entity if it doesn't exist
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

        // FIXED: Create ghosted player entities when players spawn
        ProcessPlayerSpawning(ref state, ecb);

        // Process stats change events
        ProcessStatsChangeEvents(ref state, ecb, config);

        // Process direct score events
        ProcessDirectScoreEvents(ref state, ecb);

        // Process resource addition requests
        ProcessResourceRequests(ref state, ecb);

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    /// <summary>
    /// FIXED: Create a separate ghosted entity for each player's stats
    /// This entity will replicate to all clients via Ghost system
    /// </summary>
    private void ProcessPlayerSpawning(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (netId, connectionEntity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<PendingPlayerSpawn>()
            .WithNone<PlayerStatsEntity>() // Prevent duplicate creation
            .WithEntityAccess())
        {
            // Create a separate ghosted entity for player stats
            var playerStatsEntity = ecb.CreateEntity();

            // Add PlayerStats component (this will replicate via Ghost)
            ecb.AddComponent(playerStatsEntity, new PlayerStats
            {
                resource1 = STARTING_RESOURCE1,
                resource2 = STARTING_RESOURCE2,
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0,
                playerId = netId.ValueRO.Value
            });

            // Make it owned by this player so it replicates
            ecb.AddComponent(playerStatsEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });

            // Link the connection entity to the stats entity
            ecb.AddComponent(connectionEntity, new PlayerStatsEntity { Entity = playerStatsEntity });

            Debug.Log($"[Server DEBUG] Created PlayerStats entity {playerStatsEntity} for player {netId.ValueRO.Value} with {STARTING_RESOURCE1}/{STARTING_RESOURCE2} resources");
        }

        // DEBUG: Log all existing PlayerStats
        LogExistingPlayerStats(ref state);
    }

    private float lastServerDebugTime;
    private const float SERVER_DEBUG_INTERVAL = 3.0f;

    private void LogExistingPlayerStats(ref SystemState state)
    {
        float currentTime = (float)SystemAPI.Time.ElapsedTime;
        if (currentTime - lastServerDebugTime >= SERVER_DEBUG_INTERVAL)
        {
            lastServerDebugTime = currentTime;

            Debug.Log("=== Server PlayerStats Debug ===");
            int statsCount = 0;

            foreach (var (stats, entity) in
                SystemAPI.Query<RefRO<PlayerStats>>()
                .WithEntityAccess())
            {
                statsCount++;
                Debug.Log($"[Server DEBUG] PlayerStats {statsCount}: PlayerId:{stats.ValueRO.playerId} R1:{stats.ValueRO.resource1} R2:{stats.ValueRO.resource2} Score:{stats.ValueRO.totalScore} Entity:{entity}");
            }

            if (statsCount == 0)
            {
                Debug.LogWarning("[Server DEBUG] NO PlayerStats found on server!");
            }
        }
    }

    private void ProcessStatsChangeEvents(ref SystemState state, EntityCommandBuffer ecb, StatsConfig config)
    {
        foreach (var (changeEvent, eventEntity) in
            SystemAPI.Query<RefRO<StatsChangeEvent>>()
            .WithEntityAccess())
        {
            var playerConnection = changeEvent.ValueRO.playerConnection;

            // FIXED: Find the stats entity linked to this connection
            var playerStatsEntity = FindPlayerStatsEntity(ref state, playerConnection);
            if (playerStatsEntity == Entity.Null)
            {
                Debug.LogWarning($"No PlayerStats entity found for connection {playerConnection}");
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = state.EntityManager.GetComponentData<PlayerStats>(playerStatsEntity);
            int resource1Delta = changeEvent.ValueRO.resource1Delta;
            int resource2Delta = changeEvent.ValueRO.resource2Delta;

            // Update resources
            stats.resource1 += resource1Delta;
            stats.resource2 += resource2Delta;
            stats.resource1 = math.max(0, stats.resource1);
            stats.resource2 = math.max(0, stats.resource2);

            // Award score points if specified
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

            // Update the stats entity - Ghost system handles replication
            ecb.SetComponent(playerStatsEntity, stats);
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

            // FIXED: Find the stats entity linked to this connection
            var playerStatsEntity = FindPlayerStatsEntity(ref state, playerConnection);
            if (playerStatsEntity == Entity.Null)
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = state.EntityManager.GetComponentData<PlayerStats>(playerStatsEntity);
            stats.totalScore += scoreEvent.ValueRO.scorePoints;

            // Update the stats entity - Ghost system handles replication
            ecb.SetComponent(playerStatsEntity, stats);
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

            if (FindPlayerStatsEntity(ref state, connection) != Entity.Null)
            {
                TriggerStatsChange(ecb, connection,
                    request.ValueRO.resource1ToAdd,
                    request.ValueRO.resource2ToAdd,
                    awardScorePoints: true);
            }

            ecb.DestroyEntity(rpcEntity);
        }
    }

    /// <summary>
    /// FIXED: Find the PlayerStats entity linked to a connection
    /// </summary>
    private Entity FindPlayerStatsEntity(ref SystemState state, Entity connectionEntity)
    {
        if (state.EntityManager.HasComponent<PlayerStatsEntity>(connectionEntity))
        {
            return state.EntityManager.GetComponentData<PlayerStatsEntity>(connectionEntity).Entity;
        }
        return Entity.Null;
    }

    /// <summary>
    /// FIXED: Helper method to find player connection entity by NetworkId
    /// </summary>
    public static Entity FindPlayerConnectionByNetworkId(ref SystemState state, int networkId)
    {
        var entityManager = state.EntityManager;

        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkId>(),
            ComponentType.ReadOnly<NetworkStreamConnection>(),
            ComponentType.ReadOnly<PlayerStatsEntity>()
        );

        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        var networkIds = query.ToComponentDataArray<NetworkId>(Unity.Collections.Allocator.Temp);

        Entity result = Entity.Null;
        for (int i = 0; i < networkIds.Length; i++)
        {
            if (networkIds[i].Value == networkId)
            {
                result = entities[i];
                break;
            }
        }

        entities.Dispose();
        networkIds.Dispose();
        return result;
    }

    public static bool TrySpendResources(ref SystemState state, EntityCommandBuffer ecb,
        Entity playerConnection, int resource1Cost, int resource2Cost)
    {
        // FIXED: Find PlayerStats entity directly without system reference
        Entity playerStatsEntity = Entity.Null;

        if (state.EntityManager.HasComponent<PlayerStatsEntity>(playerConnection))
        {
            playerStatsEntity = state.EntityManager.GetComponentData<PlayerStatsEntity>(playerConnection).Entity;
        }

        if (playerStatsEntity == Entity.Null)
        {
            Debug.LogWarning($"TrySpendResources: No PlayerStats entity found for connection {playerConnection}");
            return false;
        }

        if (!state.EntityManager.HasComponent<PlayerStats>(playerStatsEntity))
        {
            Debug.LogWarning($"TrySpendResources: PlayerStats entity {playerStatsEntity} missing PlayerStats component");
            return false;
        }

        var stats = state.EntityManager.GetComponentData<PlayerStats>(playerStatsEntity);

        Debug.Log($"TrySpendResources: Player {stats.playerId} has {stats.resource1}/{stats.resource2} resources, needs {resource1Cost}/{resource2Cost}");

        if (stats.resource1 >= resource1Cost && stats.resource2 >= resource2Cost)
        {
            TriggerStatsChange(ecb, playerConnection, -resource1Cost, -resource2Cost, false);
            Debug.Log($"TrySpendResources: SUCCESS - Spending {resource1Cost}/{resource2Cost} resources");
            return true;
        }

        Debug.Log($"TrySpendResources: FAILED - Insufficient resources");
        return false;
    }

    // Public utility methods
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
        int scorePoints, ScoreReason reason)
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

/// <summary>
/// NEW: Component to link connection entities to their PlayerStats entities
/// </summary>
public struct PlayerStatsEntity : IComponentData
{
    public Entity Entity;
}