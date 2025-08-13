using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Complete UnitFromBuildingSpawnSystem with progress tracking integration
/// Handles unit spawning from buildings with real-time progress updates for UI
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct UnitFromBuildingSpawnSystem : ISystem
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
        const float timeToSpawnUnit = 3.0f; // Increased for better progress visibility

        // Step 1: Handle new spawn unit from building requests (add to queue)
        HandleNewSpawnRequests(ref state, buffer, timeToSpawnUnit);

        // Step 2: Process buildings with spawn queues - start spawning if not already spawning
        StartSpawningFromQueues(ref state, buffer, timeToSpawnUnit);

        // Step 3: Process active pending spawns (countdown timers and spawn when ready)
        UpdateSpawnProgress(ref state, buffer, deltaTime, unitReferences);

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }

    /// <summary>
    /// Handles new spawn requests and adds them to building queues
    /// </summary>
    private void HandleNewSpawnRequests(ref SystemState state, EntityCommandBuffer buffer, float timeToSpawnUnit)
    {
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

                Debug.Log($"Added unit to spawn queue for building {buildingEntity}. Queue size will be: {(SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity) ? SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity).unitsInQueue + 1 : 1)}");
            }
            else
            {
                Debug.LogWarning("Tried to spawn unit from non-existent building");
            }

            // consume RPC
            buffer.DestroyEntity(rpcEntity);
        }
    }

    /// <summary>
    /// Starts spawning from queues for buildings that aren't currently spawning
    /// </summary>
    private void StartSpawningFromQueues(ref SystemState state, EntityCommandBuffer buffer, float timeToSpawnUnit)
    {
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

                        Debug.Log($"Started spawning unit for building {buildingEntity}. Remaining in queue: {spawnQueue.ValueRO.unitsInQueue}");
                        break; // Only process one unit at a time
                    }
                }
            }
        }
    }

    /// <summary>
    /// Updates spawn progress and spawns units when timers complete
    /// </summary>
    private void UpdateSpawnProgress(ref SystemState state, EntityCommandBuffer buffer, float deltaTime, EntitiesReferencesLukas unitReferences)
    {
        foreach (var (pendingSpawn, pendingEntity) in
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
                    SpawnUnit(ref state, buffer, pendingSpawn.ValueRO, unitReferences);

                    // Update building spawn queue state
                    if (SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity))
                    {
                        var spawnQueue = SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity);
                        spawnQueue.isCurrentlySpawning = false;

                        // If no more units in queue, remove the component entirely
                        if (spawnQueue.unitsInQueue <= 0)
                        {
                            buffer.RemoveComponent<BuildingSpawnQueue>(buildingEntity);
                            Debug.Log($"Spawn queue completed for building {buildingEntity}");
                        }
                        else
                        {
                            buffer.SetComponent(buildingEntity, spawnQueue);
                            Debug.Log($"Unit spawned for building {buildingEntity}. Units remaining in queue: {spawnQueue.unitsInQueue}");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("Building was destroyed before unit could spawn");
                }

                // Remove the pending spawn entity
                buffer.DestroyEntity(pendingEntity);
            }
        }
    }

    /// <summary>
    /// Spawns a unit with proper components and positioning
    /// </summary>
    private void SpawnUnit(ref SystemState state, EntityCommandBuffer buffer, PendingUnitSpawn spawnData, EntitiesReferencesLukas unitReferences)
    {
        // Create the unit entity
        var unitEntity = buffer.Instantiate(unitReferences.unitPrefabEntity);

        // Set unit position
        buffer.SetComponent(unitEntity, LocalTransform.FromPosition(spawnData.spawnPosition));

        // Set unit owner
        buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = spawnData.ownerNetworkId });

        // Set unit color based on owner
        var rgba = PlayerColorUtil.FromId(spawnData.ownerNetworkId);
        buffer.SetComponent(unitEntity, new Player { PlayerColor = rgba });

        // Set initial unit mover state
        buffer.SetComponent(unitEntity, new UnitMover
        {
            targetPosition = spawnData.spawnPosition,
            activeTarget = false
        });

        // Check for rally point and move unit there if set
        if (SystemAPI.HasComponent<BuildingRallyPoint>(spawnData.buildingEntity))
        {
            var rallyPoint = SystemAPI.GetComponent<BuildingRallyPoint>(spawnData.buildingEntity);
            if (rallyPoint.hasRallyPoint)
            {
                buffer.AddComponent(unitEntity, new MoveToRallyPoint
                {
                    targetPosition = rallyPoint.rallyPosition
                });
            }
        }

        Debug.Log($"Unit spawned successfully for building {spawnData.buildingEntity} at position {spawnData.spawnPosition}");
    }

    /// <summary>
    /// Helper method to find a valid spawn position around the building
    /// </summary>
    private float3 FindValidSpawnPosition(float3 buildingPosition, ref SystemState state)
    {
        // Array of potential spawn offsets around the building
        float3[] offsets = new float3[]
        {
            new float3(4f, 0f, 0f),   // Right
            new float3(-4f, 0f, 0f),  // Left
            new float3(0f, 0f, 4f),   // Front
            new float3(0f, 0f, -4f),  // Back
            new float3(4f, 0f, 4f),   // Front-right
            new float3(-4f, 0f, 4f),  // Front-left
            new float3(4f, 0f, -4f),  // Back-right
            new float3(-4f, 0f, -4f), // Back-left
        };

        // For now, just use the first position (right side of building)
        // You can enhance this with collision detection or random selection
        return buildingPosition + offsets[0];
    }
}