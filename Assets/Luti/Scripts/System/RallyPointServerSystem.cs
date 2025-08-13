using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct RallyPointServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SetRallyPointRequest>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // Process rally point requests from clients
        foreach (var (request, entity) in
                 SystemAPI.Query<RefRO<SetRallyPointRequest>>()
                 .WithAll<ReceiveRpcCommandRequest>()
                 .WithEntityAccess())
        {
            Debug.Log($"[Server] Processing rally point request for building {request.ValueRO.buildingEntity}");

            // Validate the building exists and is owned by the requester
            if (!SystemAPI.Exists(request.ValueRO.buildingEntity))
            {
                Debug.LogWarning("[Server] Building entity doesn't exist");
                ecb.DestroyEntity(entity);
                continue;
            }

            // Verify ownership
            if (SystemAPI.HasComponent<GhostOwner>(request.ValueRO.buildingEntity))
            {
                var ghostOwner = SystemAPI.GetComponent<GhostOwner>(request.ValueRO.buildingEntity);
                if (ghostOwner.NetworkId != request.ValueRO.ownerNetworkId)
                {
                    Debug.LogWarning($"[Server] Player {request.ValueRO.ownerNetworkId} tried to set rally point for building owned by {ghostOwner.NetworkId}");
                    ecb.DestroyEntity(entity);
                    continue;
                }
            }

            // Set or update the rally point
            if (SystemAPI.HasComponent<BuildingRallyPoint>(request.ValueRO.buildingEntity))
            {
                SystemAPI.SetComponent(request.ValueRO.buildingEntity, new BuildingRallyPoint
                {
                    rallyPosition = request.ValueRO.rallyPosition,
                    hasRallyPoint = true
                });
                Debug.Log($"[Server] Updated rally point for building {request.ValueRO.buildingEntity} to {request.ValueRO.rallyPosition}");
            }
            else
            {
                ecb.AddComponent(request.ValueRO.buildingEntity, new BuildingRallyPoint
                {
                    rallyPosition = request.ValueRO.rallyPosition,
                    hasRallyPoint = true
                });
                Debug.Log($"[Server] Added rally point for building {request.ValueRO.buildingEntity} to {request.ValueRO.rallyPosition}");
            }

            ecb.DestroyEntity(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}