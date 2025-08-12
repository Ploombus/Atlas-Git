using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

// Server-side debug system
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerDebugSystem : ISystem
{
    private float lastLogTime;

    public void OnUpdate(ref SystemState state)
    {
        // Log every 2 seconds to avoid spam
        if (UnityEngine.Time.time - lastLogTime < 2f) return;
        lastLogTime = UnityEngine.Time.time;

        int serverQueueCount = 0;
        int clientQueueCount = 0;

        // Count server-side BuildingSpawnQueue components
        foreach (var (spawnQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueue>>().WithEntityAccess())
        {
            serverQueueCount++;
            Debug.Log($"[SERVER] Entity {entity} has BuildingSpawnQueue: queue={spawnQueue.ValueRO.unitsInQueue}, spawning={spawnQueue.ValueRO.isCurrentlySpawning}");
        }

        // Count server-side BuildingSpawnQueueClient components
        foreach (var (clientQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueueClient>>().WithEntityAccess())
        {
            clientQueueCount++;
            Debug.Log($"[SERVER] Entity {entity} has BuildingSpawnQueueClient: queue={clientQueue.ValueRO.unitsInQueue}, spawning={clientQueue.ValueRO.isCurrentlySpawning}");
        }

        if (serverQueueCount > 0 || clientQueueCount > 0)
        {
            Debug.Log($"[SERVER] Total BuildingSpawnQueue: {serverQueueCount}, BuildingSpawnQueueClient: {clientQueueCount}");
        }
    }
}

// Client-side debug system
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientDebugSystem : ISystem
{
    private float lastLogTime;

    public void OnUpdate(ref SystemState state)
    {
        // Log every 2 seconds to avoid spam
        if (UnityEngine.Time.time - lastLogTime < 2f) return;
        lastLogTime = UnityEngine.Time.time;

        int clientQueueCount = 0;
        int buildingCount = 0;

        // Count client-side BuildingSpawnQueueClient components
        foreach (var (clientQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueueClient>>().WithEntityAccess())
        {
            clientQueueCount++;
            Debug.Log($"[CLIENT] Entity {entity} has BuildingSpawnQueueClient: queue={clientQueue.ValueRO.unitsInQueue}, spawning={clientQueue.ValueRO.isCurrentlySpawning}");
        }

        // Count buildings
        foreach (var (building, entity) in
                 SystemAPI.Query<RefRO<Building>>().WithEntityAccess())
        {
            buildingCount++;
        }

        if (clientQueueCount > 0 || buildingCount > 0)
        {
            Debug.Log($"[CLIENT] Total Buildings: {buildingCount}, BuildingSpawnQueueClient: {clientQueueCount}");
        }

        // Check if indicator manager exists
        var indicatorManager = SpawnQueueIndicatorManager.Instance;
        if (indicatorManager == null && buildingCount > 0)
        {
            Debug.LogWarning("[CLIENT] SpawnQueueIndicatorManager.Instance is null but buildings exist!");
        }
    }
}