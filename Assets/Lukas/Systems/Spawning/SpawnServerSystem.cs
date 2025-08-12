using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;
using System.Numerics;


[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct SpawnServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLukas>();
        state.RequireForUpdate<EntitiesReferencesLuti>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetServerWorld()))
            return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var unitRef = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        var buildingReferences = SystemAPI.GetSingleton<EntitiesReferencesLuti>();

        //Start game spawner
        foreach ((RefRO<NetworkId> netId, Entity entity)
                 in SystemAPI.Query<RefRO<NetworkId>>()
                             .WithAll<PendingPlayerSpawn>()
                             .WithEntityAccess())
        {
            float3 basePosition = new float3(UnityEngine.Random.Range(-10, 10), 0, UnityEngine.Random.Range(-10, 10));
            float spacing = 1f;

            for (int i = 0; i < 5; i++)
            {
                float3 position = basePosition + new float3(spacing * i, 0f, 0f);
                var unitEntity = buffer.Instantiate(unitRef.unitPrefabEntity);
                buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });
                buffer.SetComponent(unitEntity, LocalTransform.FromPosition(position));

                //Setting without changing speed etc.
                buffer.SetComponent(unitEntity, new UnitMover
                {
                    targetPosition = position,
                    activeTarget = false
                });

                int ownerId = netId.ValueRO.Value;
                var rgba = PlayerColorUtil.FromId(ownerId);
                buffer.SetComponent(unitEntity, new Player { PlayerColor = rgba });

                buffer.AppendToBuffer(entity, new LinkedEntityGroup { Value = unitEntity });
            }
            // Spawn Barracks building for the player
            float3 barracksPosition = basePosition + new float3(0f, 0f, 5f); // Position barracks 5 units behind the units
            var barracksEntity = buffer.Instantiate(buildingReferences.buildingPrefabEntity);

            buffer.SetComponent(barracksEntity, LocalTransform.FromPosition(barracksPosition));
            buffer.AddComponent(barracksEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });

            // Set player color for the barracks (same as units)
            int ownerId2 = netId.ValueRO.Value;
            var rgba2 = PlayerColorUtil.FromId(ownerId2);
            buffer.SetComponent(barracksEntity, new Player { PlayerColor = rgba2 });

            buffer.AppendToBuffer(entity, new LinkedEntityGroup { Value = barracksEntity });

            buffer.RemoveComponent<PendingPlayerSpawn>(entity); // prevent re-spawning
        }

        //Button spawners
        foreach (var (rpc, request, rpcEntity)
        in SystemAPI.Query<RefRO<SpawnUnitRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            // Find who sent it
            var connection = request.ValueRO.SourceConnection;
            var owner = rpc.ValueRO.owner;
            var netId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            float3 position = rpc.ValueRO.position;

            var unitEntity = buffer.Instantiate(unitRef.unitPrefabEntity);

            buffer.SetComponent(unitEntity, LocalTransform.FromPosition(position));
            buffer.SetComponent(unitEntity, new UnitMover
            {
                targetPosition = position,
                activeTarget = false
            });

            if (owner == 1)
            {
                buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = netId });
            }
            if (owner == -1)
            {
                buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = -1 });
            }

            var colorId = (owner == -1) ? -1 : netId;
            var rgba = PlayerColorUtil.FromId(colorId);
            buffer.SetComponent(unitEntity, new Player { PlayerColor = rgba });

            // consume RPC
            buffer.DestroyEntity(rpcEntity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}