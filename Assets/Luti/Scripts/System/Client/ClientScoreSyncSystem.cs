using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Client-side system that receives score updates from server
/// Handles score synchronization and can trigger UI updates
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientScoreSyncSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // Process score sync RPCs from server
        foreach (var (scoreRpc, rpcEntity) in
            SystemAPI.Query<RefRO<SyncScoreRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            // Update local score display
            // You can integrate this with your UI system
            var totalScore = scoreRpc.ValueRO.totalScore;
            var r1Score = scoreRpc.ValueRO.resource1Score;
            var r2Score = scoreRpc.ValueRO.resource2Score;

            // Example: Update UI through events or direct calls
            // ScoreUIManager.Instance?.UpdateScore(totalScore, r1Score, r2Score);

            // Or fire a score update event for other systems to consume
            // EventManager.TriggerScoreUpdate(totalScore, r1Score, r2Score);

            Debug.Log($"[Client] Score Updated - Total: {totalScore}, R1: {r1Score}, R2: {r2Score}");

            ecb.DestroyEntity(rpcEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

/// <summary>
/// Optional: Score query utilities for UI systems
/// </summary>
public static class ScoreQueryUtils
{
    /// <summary>
    /// Get current local player score from the latest RPC
    /// Returns false if no score data available
    /// </summary>
    public static bool TryGetLocalPlayerScore(World clientWorld, out int totalScore,
        out int resource1Score, out int resource2Score)
    {
        totalScore = 0;
        resource1Score = 0;
        resource2Score = 0;

        if (clientWorld == null) return false;

        // This would need to be implemented based on how you store the latest score data
        // Option 1: Store in a singleton component
        // Option 2: Cache in a static/singleton manager
        // Option 3: Query the last received score RPC (more complex)

        return false; // Placeholder
    }
}