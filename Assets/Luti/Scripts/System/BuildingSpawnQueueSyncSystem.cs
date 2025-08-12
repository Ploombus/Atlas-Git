using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

// Server-side system to sync BuildingSpawnQueue to BuildingSpawnQueueClient
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct BuildingSpawnQueueSyncSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        Debug.Log("[SERVER] BuildingSpawnQueueSyncSystem created");
    }

    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        int syncedCount = 0;
        int addedCount = 0;
        int updatedCount = 0;

        // Sync server BuildingSpawnQueue to client BuildingSpawnQueueClient
        foreach (var (spawnQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueue>>().WithEntityAccess())
        {
            syncedCount++;

            var clientData = new BuildingSpawnQueueClient
            {
                unitsInQueue = spawnQueue.ValueRO.unitsInQueue,
                isCurrentlySpawning = spawnQueue.ValueRO.isCurrentlySpawning
            };

            // Add or update the client component
            if (SystemAPI.HasComponent<BuildingSpawnQueueClient>(entity))
            {
                // Update existing client component
                buffer.SetComponent(entity, clientData);
                updatedCount++;
                Debug.Log($"[SERVER SYNC] Updated entity {entity} with queue={clientData.unitsInQueue}, spawning={clientData.isCurrentlySpawning}");
            }
            else
            {
                // Add new client component
                buffer.AddComponent(entity, clientData);
                addedCount++;
                Debug.Log($"[SERVER SYNC] Added client component to entity {entity} with queue={clientData.unitsInQueue}, spawning={clientData.isCurrentlySpawning}");
            }
        }

        // Remove client components for entities that no longer have server components
        int removedCount = 0;
        foreach (var (clientQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueueClient>>().WithEntityAccess().WithNone<BuildingSpawnQueue>())
        {
            buffer.RemoveComponent<BuildingSpawnQueueClient>(entity);
            removedCount++;
            Debug.Log($"[SERVER SYNC] Removed client component from entity {entity}");
        }

        if (syncedCount > 0 || removedCount > 0)
        {
            Debug.Log($"[SERVER SYNC] Processed {syncedCount} entities (added: {addedCount}, updated: {updatedCount}, removed: {removedCount})");
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}