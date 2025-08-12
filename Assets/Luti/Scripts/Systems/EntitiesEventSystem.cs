using Managers;
using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.InputSystem.Processors;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct EntitiesEventSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLuti>();
        state.RequireForUpdate<UnitTargetNetcode>();

    }

    public void OnUpdate(ref SystemState state)
    {

        if (ComponentRequestQueue.BuildingModeEnd.Count == 0)
            return;

        var prefab = SystemAPI.GetSingleton<EntitiesReferencesLuti>();
        var spawnEntityBuffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach ((RefRO<NetworkId> netId, Entity entity)          //netID = Player number
                 in SystemAPI.Query<RefRO<NetworkId>>()
                             .WithEntityAccess())
        {
            Entity spawnedEntity = spawnEntityBuffer.Instantiate(prefab.buildingPrefabEntity);
            Vector3 spawnPosition = MouseWorldPosition.Instance.GetPosition();
            spawnEntityBuffer.SetComponent(spawnedEntity, LocalTransform.FromPosition(spawnPosition));
            spawnEntityBuffer.AddComponent(spawnedEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });
            spawnEntityBuffer.AppendToBuffer(entity, new LinkedEntityGroup { Value = spawnedEntity });
        }

        spawnEntityBuffer.Playback(state.EntityManager);
        spawnEntityBuffer.Dispose();
        ComponentRequestQueue.BuildingModeEnd.Clear();
        ComponentRequestQueue.BuildingModeStart.Clear();
    }
}

/*public struct PendingBuildingSpawn : IComponentData 
{

}*/

/* use if another system is checking BuildingModeEnd
        ComponentRequestQueue.BuildingModeEnd.Add(new AddComponentRequest()); luti*/













/*foreach ((RefRW<Building> building,
    RefRW<LocalTransform> localTransform) 
    in SystemAPI.Query<RefRW<Building>, RefRW<LocalTransform>>().WithAll<Player, Simulate>())
{

    if (building.ValueRO.inBuildMode)
    {

        Entity buildingPrefabEntity = state.EntityManager.Instantiate(entitiesReferences.buildingPrefabEntity);

        SystemAPI.SetComponent(buildingPrefabEntity, LocalTransform.FromPosition(MouseWorldPosition.Instance.GetPosition()));
        building.ValueRW.inBuildMode = false;
        Debug.Log("Building spawned!");
    }
    else
    {
        Debug.Log("No Building to spawn, nothing executed");
    }
}


}*/
