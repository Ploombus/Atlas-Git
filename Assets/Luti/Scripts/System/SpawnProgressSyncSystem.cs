using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Server-side system that syncs spawn progress from PendingUnitSpawn to BuildingSpawnProgress
/// This ensures clients receive real-time spawn progress updates
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct SpawnProgressSyncSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // Sync active spawning progress
        foreach (var (pendingSpawn, buildingEntity) in
                 SystemAPI.Query<RefRO<PendingUnitSpawn>>().WithEntityAccess())
        {
            var pending = pendingSpawn.ValueRO;

            // Create or update BuildingSpawnProgress component on the building
            var progressData = new BuildingSpawnProgress
            {
                currentSpawnTime = pending.totalSpawnTime - pending.remainingTime,
                totalSpawnTime = pending.totalSpawnTime,
                currentUnitIndex = 1, // Currently spawning unit
                totalUnitsInQueue = GetTotalQueueCount(pending.buildingEntity, ref state),
                isSpawning = true
            };

            if (SystemAPI.HasComponent<BuildingSpawnProgress>(pending.buildingEntity))
            {
                buffer.SetComponent(pending.buildingEntity, progressData);
            }
            else
            {
                buffer.AddComponent(pending.buildingEntity, progressData);
            }
        }

        // Handle buildings with queues but no active spawning
        foreach (var (spawnQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueue>>().WithEntityAccess())
        {
            // If building has units in queue but no active spawn, show queue info
            if (spawnQueue.ValueRO.unitsInQueue > 0 && !spawnQueue.ValueRO.isCurrentlySpawning)
            {
                var progressData = new BuildingSpawnProgress
                {
                    currentSpawnTime = 0f,
                    totalSpawnTime = spawnQueue.ValueRO.timeToSpawnUnit,
                    currentUnitIndex = 0,
                    totalUnitsInQueue = spawnQueue.ValueRO.unitsInQueue,
                    isSpawning = false
                };

                if (SystemAPI.HasComponent<BuildingSpawnProgress>(entity))
                {
                    buffer.SetComponent(entity, progressData);
                }
                else
                {
                    buffer.AddComponent(entity, progressData);
                }
            }
            // If no units in queue, remove progress component
            else if (spawnQueue.ValueRO.unitsInQueue == 0)
            {
                if (SystemAPI.HasComponent<BuildingSpawnProgress>(entity))
                {
                    buffer.RemoveComponent<BuildingSpawnProgress>(entity);
                }
            }
        }

        // Clean up progress components for buildings that no longer have spawn queues
        foreach (var (progress, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnProgress>>().WithEntityAccess().WithNone<BuildingSpawnQueue>())
        {
            buffer.RemoveComponent<BuildingSpawnProgress>(entity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }

    /// <summary>
    /// Helper method to get the total queue count for a building
    /// </summary>
    private int GetTotalQueueCount(Entity buildingEntity, ref SystemState state)
    {
        if (SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity))
        {
            var queue = SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity);
            return queue.unitsInQueue;
        }
        return 0;
    }
}