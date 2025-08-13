using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Client-side system that manages spawn progress UI for buildings
/// Creates, updates, and destroys UI instances based on spawn progress data
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct SpawnProgressUISystem : ISystem
{
    private EntityQuery buildingsWithProgressQuery;
    private EntityQuery buildingsWithUIQuery;

    public void OnCreate(ref SystemState state)
    {
        // Query for buildings that have spawn progress but no UI instance
        buildingsWithProgressQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<BuildingSpawnProgress>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<SpawnProgressUIInstance>()
        );

        // Query for buildings that have UI instances
        buildingsWithUIQuery = state.GetEntityQuery(
            ComponentType.ReadWrite<SpawnProgressUIInstance>(),
            ComponentType.ReadOnly<LocalTransform>()
        );
    }

    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var progressUIManager = SpawnProgressUIManager.Instance;

        if (progressUIManager == null)
        {
            buffer.Playback(state.EntityManager);
            buffer.Dispose();
            return;
        }

        // Create UI instances for buildings that need them
        CreateUIInstances(ref state, buffer, progressUIManager);

        // Update existing UI instances
        UpdateUIInstances(ref state, buffer, progressUIManager);

        // Clean up UI instances that are no longer needed
        CleanupUIInstances(ref state, buffer, progressUIManager);

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }

    private void CreateUIInstances(ref SystemState state, EntityCommandBuffer buffer, SpawnProgressUIManager manager)
    {
        foreach (var (progress, transform, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnProgress>, RefRO<LocalTransform>>()
                 .WithEntityAccess().WithNone<SpawnProgressUIInstance>())
        {
            // Only create UI if building is actively spawning or has units in queue
            if (progress.ValueRO.totalUnitsInQueue > 0)
            {
                var uiInstanceId = manager.CreateProgressUI(entity);
                if (uiInstanceId != -1)
                {
                    buffer.AddComponent(entity, new SpawnProgressUIInstance
                    {
                        uiInstanceId = uiInstanceId,
                        isActive = true
                    });
                }
            }
        }
    }

    private void UpdateUIInstances(ref SystemState state, EntityCommandBuffer buffer, SpawnProgressUIManager manager)
    {
        foreach (var (uiInstance, transform, entity) in
                 SystemAPI.Query<RefRW<SpawnProgressUIInstance>, RefRO<LocalTransform>>()
                 .WithEntityAccess())
        {
            var uiGameObject = manager.GetUIInstance(uiInstance.ValueRO.uiInstanceId);
            if (uiGameObject == null)
            {
                buffer.RemoveComponent<SpawnProgressUIInstance>(entity);
                continue;
            }

            // Update UI if building has progress data
            if (SystemAPI.HasComponent<BuildingSpawnProgress>(entity))
            {
                var progress = SystemAPI.GetComponent<BuildingSpawnProgress>(entity);
                var spawnProgressUI = uiGameObject.GetComponent<SpawnProgressUI>();

                if (spawnProgressUI != null)
                {
                    // Update world position
                    spawnProgressUI.SetWorldPosition(transform.ValueRO.Position);

                    // Update progress and queue info
                    if (progress.isSpawning)
                    {
                        spawnProgressUI.UpdateProgress(progress.currentSpawnTime, progress.totalSpawnTime);
                        spawnProgressUI.UpdateQueueInfo(progress.currentUnitIndex, progress.totalUnitsInQueue);
                        spawnProgressUI.SetVisible(true);

                        if (!uiInstance.ValueRO.isActive)
                        {
                            uiInstance.ValueRW.isActive = true;
                        }
                    }
                    else if (progress.totalUnitsInQueue > 0)
                    {
                        // Show queue info even when not actively spawning
                        spawnProgressUI.UpdateProgress(0f, progress.totalSpawnTime);
                        spawnProgressUI.UpdateQueueInfo(0, progress.totalUnitsInQueue);
                        spawnProgressUI.SetVisible(true);

                        if (!uiInstance.ValueRO.isActive)
                        {
                            uiInstance.ValueRW.isActive = true;
                        }
                    }
                }
            }
            else
            {
                // No progress data, hide UI but keep instance for potential reuse
                var spawnProgressUI = uiGameObject.GetComponent<SpawnProgressUI>();
                if (spawnProgressUI != null)
                {
                    spawnProgressUI.SetVisible(false);
                }

                if (uiInstance.ValueRO.isActive)
                {
                    uiInstance.ValueRW.isActive = false;
                }
            }
        }
    }

    private void CleanupUIInstances(ref SystemState state, EntityCommandBuffer buffer, SpawnProgressUIManager manager)
    {
        foreach (var (uiInstance, entity) in
                 SystemAPI.Query<RefRO<SpawnProgressUIInstance>>()
                 .WithEntityAccess().WithNone<BuildingSpawnProgress>())
        {
            // Building no longer has progress data, clean up UI
            manager.DestroyProgressUI(uiInstance.ValueRO.uiInstanceId);
            buffer.RemoveComponent<SpawnProgressUIInstance>(entity);
        }
    }
}