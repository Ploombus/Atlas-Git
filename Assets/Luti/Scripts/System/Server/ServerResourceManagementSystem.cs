using Managers;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Server-side system that manages player resources and syncs them with clients
/// Each player connection entity has a PlayerResources component
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerResourceManagementSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetServerWorld()))
            return; 
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // Initialize resources for new player connections
        foreach (var (netId, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithNone<PlayerResources>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            // Give new players starting resources
            buffer.AddComponent(entity, new PlayerResources
            {
                resource1 = 100, // Starting resources
                resource2 = 100
            });

        }

        /* Sync resource changes to clients periodically
        foreach (var (resources, netId, entity) in
            SystemAPI.Query<RefRO<PlayerResources>, RefRO<NetworkId>>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            // Send resource sync RPC to the client (implement if needed)
            // This can be done periodically or when resources change
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();*/
    }
}
