using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct SpawnQueueIndicatorSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        Debug.Log("SpawnQueueIndicatorSystem created");
    }

    public void OnUpdate(ref SystemState state)
    {
        var indicatorManager = SpawnQueueIndicatorManager.Instance;
        if (indicatorManager == null)
        {
            Debug.LogWarning("SpawnQueueIndicatorManager.Instance is null");
            return;
        }

        // Debug: Log how many buildings with spawn queues we found
        int buildingsWithQueues = 0;
        int totalQueueCount = 0;

        // Update indicators for buildings with spawn queues (using client component)
        foreach (var (spawnQueue, localTransform, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueueClient>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            buildingsWithQueues++;
            var position = localTransform.ValueRO.Position;
            var queueCount = spawnQueue.ValueRO.unitsInQueue;
            totalQueueCount += queueCount;

            // Add 1 if currently spawning (to show the unit being produced)
            if (spawnQueue.ValueRO.isCurrentlySpawning)
            {
                queueCount++;
            }

            Debug.Log($"Building {entity} has queue count: {queueCount} (queue: {spawnQueue.ValueRO.unitsInQueue}, spawning: {spawnQueue.ValueRO.isCurrentlySpawning})");

            // Update or create indicator for this building
            indicatorManager.UpdateIndicator(entity, position, queueCount);
        }

        // Log debug info every few seconds
        if (buildingsWithQueues > 0)
        {
            Debug.Log($"Found {buildingsWithQueues} buildings with spawn queues, total units queued: {totalQueueCount}");
        }

        // Clean up indicators for buildings without spawn queues (using client component)
        foreach (var (localTransform, entity) in
                 SystemAPI.Query<RefRO<LocalTransform>>().WithEntityAccess().WithAll<Building>().WithNone<BuildingSpawnQueueClient>())
        {
            indicatorManager.HideIndicator(entity);
        }

        // Also hide indicators for buildings that have empty queues and aren't spawning (using client component)
        foreach (var (spawnQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueueClient>>().WithEntityAccess())
        {
            if (spawnQueue.ValueRO.unitsInQueue == 0 && !spawnQueue.ValueRO.isCurrentlySpawning)
            {
                indicatorManager.HideIndicator(entity);
            }
        }
    }
}