/*using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEditor.PackageManager;
using UnityEngine;


[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ServerPlayerResourceSystem))] // Run after resource changes
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerScoreTrackingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();

        // Create score config singleton if it doesn't exist
        if (!SystemAPI.HasSingleton<ScoreConfig>())
        {
            var configEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(configEntity, ScoreConfig.Default);
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var scoreConfig = SystemAPI.GetSingleton<ScoreConfig>();

        // Initialize scores for new players
        InitializeNewPlayerScores(ref state, ecb);

        // Process resource change events
        ProcessResourceChangeEvents(ref state, ecb, scoreConfig);

        // Process direct score events
        ProcessDirectScoreEvents(ref state, ecb);

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private void InitializeNewPlayerScores(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (netId, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<PlayerStats>()
            .WithNone<PlayerScore>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            // Initialize score tracking for new players
            ecb.AddComponent(entity, new PlayerScore
            {
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0
            });
        }
    }

    private void ProcessResourceChangeEvents(ref SystemState state, EntityCommandBuffer ecb, ScoreConfig scoreConfig)
    {
        foreach (var (resourceEvent, eventEntity) in
            SystemAPI.Query<RefRO<ResourceChangeEvent>>()
            .WithEntityAccess())
        {
            var playerConnection = resourceEvent.ValueRO.playerConnection;

            // Validate player connection still exists and has score component
            if (!state.EntityManager.Exists(playerConnection) ||
                !SystemAPI.HasComponent<PlayerScore>(playerConnection))
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            // Calculate score changes
            int resource1Delta = resourceEvent.ValueRO.resource1Delta;
            int resource2Delta = resourceEvent.ValueRO.resource2Delta;

            // Only award points for positive resource gains
            int scoreIncrease = 0;
            int r1ScoreIncrease = 0;
            int r2ScoreIncrease = 0;

            if (resource1Delta > 0)
            {
                r1ScoreIncrease = resource1Delta * scoreConfig.pointsPerResource1;
                scoreIncrease += r1ScoreIncrease;
            }

            if (resource2Delta > 0)
            {
                r2ScoreIncrease = resource2Delta * scoreConfig.pointsPerResource2;
                scoreIncrease += r2ScoreIncrease;
            }

            // Update player score
            if (scoreIncrease > 0)
            {
                var currentScore = SystemAPI.GetComponent<PlayerScore>(playerConnection);
                currentScore.totalScore += scoreIncrease;
                currentScore.resource1Score += r1ScoreIncrease;
                currentScore.resource2Score += r2ScoreIncrease;

                ecb.SetComponent(playerConnection, currentScore);

                // Send score sync to client
                var syncRpc = ecb.CreateEntity();
                ecb.AddComponent(syncRpc, new SyncScoreRpc
                {
                    totalScore = currentScore.totalScore,
                    resource1Score = currentScore.resource1Score,
                    resource2Score = currentScore.resource2Score
                });
                ecb.AddComponent(syncRpc, new SendRpcCommandRequest { TargetConnection = playerConnection });
            }

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

            // Validate player connection still exists and has score component
            if (!state.EntityManager.Exists(playerConnection) ||
                !SystemAPI.HasComponent<PlayerScore>(playerConnection))
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            // Apply direct score change
            var currentScore = SystemAPI.GetComponent<PlayerScore>(playerConnection);
            currentScore.totalScore += scoreEvent.ValueRO.scorePoints;

            ecb.SetComponent(playerConnection, currentScore);

            // Send score sync to client
            var syncRpc = ecb.CreateEntity();
            ecb.AddComponent(syncRpc, new SyncScoreRpc
            {
                totalScore = currentScore.totalScore,
                resource1Score = currentScore.resource1Score,
                resource2Score = currentScore.resource2Score
            });
            ecb.AddComponent(syncRpc, new SendRpcCommandRequest { TargetConnection = playerConnection });

            // Clean up event
            ecb.DestroyEntity(eventEntity);
        }
    }

    public static void TriggerScoreUpdate(EntityCommandBuffer ecb, Entity playerConnection, 
        int resource1Delta, int resource2Delta)
    {
        // Only create event if there are actual resource changes
        if (resource1Delta != 0 || resource2Delta != 0)
        {
            var eventEntity = ecb.CreateEntity();
            ecb.AddComponent(eventEntity, new ResourceChangeEvent
            {
                resource1Delta = resource1Delta,
                resource2Delta = resource2Delta,
                playerConnection = playerConnection
            });
        }
    }

    // Award points directly without resource changes
    public static void AwardPoints(EntityCommandBuffer ecb, Entity playerConnection,
        int points, ScoreReason reason = ScoreReason.Custom)
    {
        if (points != 0)
        {
            var eventEntity = ecb.CreateEntity();
            ecb.AddComponent(eventEntity, new DirectScoreEvent
            {
                scorePoints = points,
                playerConnection = playerConnection,
                reason = reason
            });
        }
    }
}*/