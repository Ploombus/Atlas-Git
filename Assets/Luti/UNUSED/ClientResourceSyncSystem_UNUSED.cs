/*using Unity.Entities;
using Unity.NetCode;
using UnityEngine;


// gets resource status from Server and updates the local ResourceManager

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientResourceSyncSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var resourceManager = ResourceManager.Instance;
        if (resourceManager == null) return;

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // Process resource sync RPCs from server
        foreach (var (syncRpc, rpcEntity) in
            SystemAPI.Query<RefRO<SyncResourcesRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            int currentR1 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource1);
            int currentR2 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource2);

            int newR1 = syncRpc.ValueRO.resource1;
            int newR2 = syncRpc.ValueRO.resource2;

            // Set the resources to match server
            if (currentR1 != newR1)
            {
                int diff = newR1 - currentR1;
                if (diff > 0)
                    resourceManager.AddResource(ResourceManager.ResourceType.Resource1, diff);
                else
                    resourceManager.RemoveResource(ResourceManager.ResourceType.Resource1, -diff);
            }

            if (currentR2 != newR2)
            {
                int diff = newR2 - currentR2;
                if (diff > 0)
                    resourceManager.AddResource(ResourceManager.ResourceType.Resource2, diff);
                else
                    resourceManager.RemoveResource(ResourceManager.ResourceType.Resource2, -diff);
            }

            ecb.DestroyEntity(rpcEntity);
        }

        // Process resource refund RPCs (when spawn fails)
        foreach (var (refund, rpcEntity) in
            SystemAPI.Query<RefRO<ResourceRefundRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            // This is a refund, just add the resources back
            resourceManager.AddResource(ResourceManager.ResourceType.Resource1, refund.ValueRO.resource1Amount);
            resourceManager.AddResource(ResourceManager.ResourceType.Resource2, refund.ValueRO.resource2Amount);

            Debug.Log($"[Client] Resources refunded: R1:{refund.ValueRO.resource1Amount}, R2:{refund.ValueRO.resource2Amount}");

            ecb.DestroyEntity(rpcEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}*/