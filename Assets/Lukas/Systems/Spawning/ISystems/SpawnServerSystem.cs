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
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetServerWorld()))
            return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var references = SystemAPI.GetSingleton<EntitiesReferencesLukas>();

        //Start game spawner
        foreach ((RefRO<NetworkId> netId, Entity entity)
                 in SystemAPI.Query<RefRO<NetworkId>>()
                             .WithAll<PendingPlayerSpawn>()
                             .WithEntityAccess())
        {
            for (int i = 0; i < 3; i++)
            {
                var unitEntity = buffer.Instantiate(references.unitPrefabEntity);
                float3 position = new float3(UnityEngine.Random.Range(-10, 10), 0, UnityEngine.Random.Range(-10, 10));
                buffer.SetComponent(unitEntity, LocalTransform.FromPosition(position));

                //Setting without changing speed etc.
                buffer.SetComponent(unitEntity, new UnitMover
                {
                    moveSpeed = 5f,
                    rotationSpeed = 5f,
                    targetPosition = position,
                    activeTarget = false
                });

                buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });

                buffer.AppendToBuffer(entity, new LinkedEntityGroup { Value = unitEntity });

                buffer.RemoveComponent<PendingPlayerSpawn>(entity); // prevent re-spawning
            }
        }

        //Button spawners
        foreach (var (rpc, request, rpcEntity)
        in SystemAPI.Query<RefRO<SpawnUnitRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            // Find who sent it
            var connection = request.ValueRO.SourceConnection;
            var netId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            float3 position = rpc.ValueRO.position;

            Entity unit = buffer.Instantiate(references.unitPrefabEntity);

            buffer.SetComponent(unit, LocalTransform.FromPosition(position));
            buffer.SetComponent(unit, new UnitMover
            {
                moveSpeed = 5f,
                rotationSpeed = 5f,
                targetPosition = position,
                activeTarget = false
            });
            buffer.AddComponent(unit, new GhostOwner { NetworkId = netId });

            // consume RPC
            buffer.DestroyEntity(rpcEntity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}