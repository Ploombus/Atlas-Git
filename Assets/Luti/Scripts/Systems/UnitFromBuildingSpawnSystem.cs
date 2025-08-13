using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct BuildingUnitSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLukas>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetServerWorld()))
            return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var unitReferences = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        const float timeToSpawnUnit = 1.0f;

        // Step 1: Handle new spawn unit from building requests (add to queue)
        foreach (var (rpc, request, rpcEntity)
        in SystemAPI.Query<RefRO<SpawnUnitFromBuildingRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            var connection = request.ValueRO.SourceConnection;
            var requesterNetId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            var buildingEntity = rpc.ValueRO.buildingEntity;

            // Check if the building exists and get its information
            if (SystemAPI.Exists(buildingEntity))
            {
                // Get building position to spawn unit nearby
                var buildingTransform = SystemAPI.GetComponent<LocalTransform>(buildingEntity);
                var buildingPosition = buildingTransform.Position;

                // Get building owner
                int buildingOwnerId = -1;
                if (SystemAPI.HasComponent<GhostOwner>(buildingEntity))
                {
                    buildingOwnerId = SystemAPI.GetComponent<GhostOwner>(buildingEntity).NetworkId;
                }

                // Security check
                if (buildingOwnerId != requesterNetId)
                {
                    Debug.LogWarning($"Player {requesterNetId} tried to spawn unit from building owned by {buildingOwnerId}");
                    buffer.DestroyEntity(rpcEntity);
                    continue;
                }

                // Initialize or update building spawn queue
                if (SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity))
                {
                    // Update existing queue
                    var spawnQueue = SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity);
                    spawnQueue.unitsInQueue++;
                    buffer.SetComponent(buildingEntity, spawnQueue);
                }
                else
                {
                    // Initialize new queue
                    buffer.AddComponent(buildingEntity, new BuildingSpawnQueue
                    {
                        unitsInQueue = 1,
                        isCurrentlySpawning = false,
                        timeToSpawnUnit = timeToSpawnUnit
                    });
                }

                // Calculate spawn position
                float3 spawnPosition = FindValidSpawnPosition(buildingPosition, ref state);

                // Add to queue
                var queuedSpawnEntity = buffer.CreateEntity();
                buffer.AddComponent(queuedSpawnEntity, new QueuedUnitSpawn
                {
                    buildingEntity = buildingEntity,
                    ownerNetworkId = buildingOwnerId,
                    spawnPosition = spawnPosition
                });

            }
            else
            {
                Debug.LogWarning("Tried to spawn unit from non-existent building");
            }

            // consume RPC
            buffer.DestroyEntity(rpcEntity);
        }

        // Step 2: Process buildings with spawn queues - start spawning if not already spawning
        foreach (var (spawnQueue, buildingEntity) in
                 SystemAPI.Query<RefRW<BuildingSpawnQueue>>().WithEntityAccess())
        {
            // If building has queued units and is not currently spawning, start spawning
            if (spawnQueue.ValueRO.unitsInQueue > 0 && !spawnQueue.ValueRO.isCurrentlySpawning)
            {
                // Find the first queued spawn for this building
                foreach (var (queuedSpawn, queuedEntity) in
                         SystemAPI.Query<RefRO<QueuedUnitSpawn>>().WithEntityAccess())
                {
                    if (queuedSpawn.ValueRO.buildingEntity == buildingEntity)
                    {
                        // Convert queued spawn to active pending spawn
                        var pendingSpawnEntity = buffer.CreateEntity();
                        buffer.AddComponent(pendingSpawnEntity, new PendingUnitSpawn
                        {
                            buildingEntity = queuedSpawn.ValueRO.buildingEntity,
                            ownerNetworkId = queuedSpawn.ValueRO.ownerNetworkId,
                            spawnPosition = queuedSpawn.ValueRO.spawnPosition,
                            remainingTime = timeToSpawnUnit,
                            totalSpawnTime = timeToSpawnUnit
                        });

                        // Mark building as currently spawning and decrement queue
                        spawnQueue.ValueRW.isCurrentlySpawning = true;
                        spawnQueue.ValueRW.unitsInQueue--;

                        // Remove from queue
                        buffer.DestroyEntity(queuedEntity);

                        break; // Only process one unit at a time
                    }
                }
            }
        }

        // Step 3: Process active pending spawns (countdown timers and spawn when ready)
        foreach (var (pendingSpawn, entity) in
                 SystemAPI.Query<RefRW<PendingUnitSpawn>>().WithEntityAccess())
        {
            // Countdown the timer
            pendingSpawn.ValueRW.remainingTime -= deltaTime;

            // Check if it's time to spawn the unit
            if (pendingSpawn.ValueRO.remainingTime <= 0f)
            {
                var buildingEntity = pendingSpawn.ValueRO.buildingEntity;

                // Verify the building still exists
                if (SystemAPI.Exists(buildingEntity))
                {
                    // Spawn the unit
                    var unitEntity = buffer.Instantiate(unitReferences.unitPrefabEntity);
                    buffer.SetComponent(unitEntity, LocalTransform.FromPosition(pendingSpawn.ValueRO.spawnPosition));
                    buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = pendingSpawn.ValueRO.ownerNetworkId });

                    buffer.SetComponent(unitEntity, new UnitMover
                    {
                        targetPosition = pendingSpawn.ValueRO.spawnPosition,
                        activeTarget = false
                    });

                    // Set player color to match building owner
                    var rgba = PlayerColorUtil.FromId(pendingSpawn.ValueRO.ownerNetworkId);
                    buffer.SetComponent(unitEntity, new Player { PlayerColor = rgba });

                    // Mark building as no longer spawning (safely check if component exists)
                    if (SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity))
                    {
                        var spawnQueue = SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity);
                        spawnQueue.isCurrentlySpawning = false;
                        buffer.SetComponent(buildingEntity, spawnQueue);
                    }

                }
                else
                {
                    Debug.LogWarning("Building was destroyed before unit could spawn");
                }

                // Remove the pending spawn entity
                buffer.DestroyEntity(entity);
            }
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }

    // Helper method to find a valid spawn position around the building
    private float3 FindValidSpawnPosition(float3 buildingPosition, ref SystemState state)
    {
        float3[] offsets = new float3[]
        {
            new float3(3f, 0f, 0f),   // Right
            new float3(-3f, 0f, 0f),  // Left
            new float3(0f, 0f, 3f),   // Front
            new float3(0f, 0f, -3f),  // Back
            new float3(3f, 0f, 3f),   // Front-right
            new float3(-3f, 0f, 3f),  // Front-left
            new float3(3f, 0f, -3f),  // Back-right
            new float3(-3f, 0f, -3f), // Back-left
        };

        return buildingPosition + offsets[0];
    }
}