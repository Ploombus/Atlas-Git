using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Server-side system that manages player resources
/// Initializes new players, handles resource changes, and syncs with clients
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerPlayerResourceSystem : ISystem
{
    private float resourceGenerationTimer;
    private const float RESOURCE_GENERATION_INTERVAL = 5f; // Generate resources every 5 seconds
    private const int STARTING_RESOURCES = 100; // Starting resources for new players
    private const int RESOURCE_GENERATION_AMOUNT = 0; // Resources generated per interval

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();
        resourceGenerationTimer = 0f;
    }

    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var deltaTime = SystemAPI.Time.DeltaTime;

        // Initialize resources for new player connections
        InitializeNewPlayers(ref state, buffer);

        // Process resource addition requests from clients (for testing)
        ProcessResourceRequests(ref state, buffer);

        // Generate resources periodically for all players
        GenerateResourcesPeriodically(ref state, buffer, deltaTime);

        // Sync resources to clients
        SyncResourcesToClients(ref state, buffer);

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }

    private void InitializeNewPlayers(ref SystemState state, EntityCommandBuffer buffer)
    {
        foreach (var (netId, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithNone<PlayerResources>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            // Give new players starting resources
            buffer.AddComponent(entity, new PlayerResources
            {
                resource1 = STARTING_RESOURCES,
                resource2 = STARTING_RESOURCES
            });

            Debug.Log($"[Server] Initialized resources for player {netId.ValueRO.Value}: R1:{STARTING_RESOURCES}, R2:{STARTING_RESOURCES}");

            // Send initial sync to the client
            var syncRpc = buffer.CreateEntity();
            buffer.AddComponent(syncRpc, new SyncResourcesRpc
            {
                resource1 = STARTING_RESOURCES,
                resource2 = STARTING_RESOURCES
            });
            buffer.AddComponent(syncRpc, new SendRpcCommandRequest { TargetConnection = entity });
        }
    }

    private void ProcessResourceRequests(ref SystemState state, EntityCommandBuffer buffer)
    {
        foreach (var (request, receiveRequest, rpcEntity) in
            SystemAPI.Query<RefRO<AddResourcesRpc>, RefRO<ReceiveRpcCommandRequest>>()
            .WithEntityAccess())
        {
            var connection = receiveRequest.ValueRO.SourceConnection;

            if (SystemAPI.HasComponent<PlayerResources>(connection))
            {
                var resources = SystemAPI.GetComponent<PlayerResources>(connection);
                resources.resource1 += request.ValueRO.resource1ToAdd;
                resources.resource2 += request.ValueRO.resource2ToAdd;

                buffer.SetComponent(connection, resources);

                var netId = SystemAPI.GetComponent<NetworkId>(connection).Value;
                Debug.Log($"[Server] Added resources for player {netId}: +R1:{request.ValueRO.resource1ToAdd}, +R2:{request.ValueRO.resource2ToAdd}");

                // Sync new amounts back to client
                var syncRpc = buffer.CreateEntity();
                buffer.AddComponent(syncRpc, new SyncResourcesRpc
                {
                    resource1 = resources.resource1,
                    resource2 = resources.resource2
                });
                buffer.AddComponent(syncRpc, new SendRpcCommandRequest { TargetConnection = connection });
            }

            buffer.DestroyEntity(rpcEntity);
        }
    }

    private void GenerateResourcesPeriodically(ref SystemState state, EntityCommandBuffer buffer, float deltaTime)
    {
        resourceGenerationTimer += deltaTime;

        if (resourceGenerationTimer >= RESOURCE_GENERATION_INTERVAL)
        {
            resourceGenerationTimer = 0f;

            foreach (var (resources, netId, entity) in
                SystemAPI.Query<RefRW<PlayerResources>, RefRO<NetworkId>>()
                .WithAll<NetworkStreamConnection>()
                .WithEntityAccess())
            {
                // Add resources
                resources.ValueRW.resource1 += RESOURCE_GENERATION_AMOUNT;
                resources.ValueRW.resource2 += RESOURCE_GENERATION_AMOUNT;

                Debug.Log($"[Server] Generated resources for player {netId.ValueRO.Value}. New totals: R1:{resources.ValueRO.resource1}, R2:{resources.ValueRO.resource2}");

                // Send sync RPC to client
                var syncRpc = buffer.CreateEntity();
                buffer.AddComponent(syncRpc, new SyncResourcesRpc
                {
                    resource1 = resources.ValueRO.resource1,
                    resource2 = resources.ValueRO.resource2
                });
                buffer.AddComponent(syncRpc, new SendRpcCommandRequest { TargetConnection = entity });
            }
        }
    }

    private void SyncResourcesToClients(ref SystemState state, EntityCommandBuffer buffer)
    {
        // This method can be expanded to sync on specific events or periodically
        // For now, syncing happens when resources change (in other methods)
    }

    // Helper method to get player resources (for use by other systems)
    public static bool TryGetPlayerResources(ref SystemState state, Entity connectionEntity, out PlayerResources resources)
    {
        if (state.EntityManager.HasComponent<PlayerResources>(connectionEntity))
        {
            resources = state.EntityManager.GetComponentData<PlayerResources>(connectionEntity);
            return true;
        }

        resources = default;
        return false;
    }

    // Helper method to spend resources (returns true if successful)
    // Now accepts an EntityCommandBuffer to avoid conflicts
    public static bool TrySpendResources(ref SystemState state, EntityCommandBuffer ecb,
        Entity connectionEntity, int resource1Cost, int resource2Cost)
    {
        if (!state.EntityManager.HasComponent<PlayerResources>(connectionEntity))
            return false;

        var resources = state.EntityManager.GetComponentData<PlayerResources>(connectionEntity);

        if (resources.resource1 >= resource1Cost && resources.resource2 >= resource2Cost)
        {
            resources.resource1 -= resource1Cost;
            resources.resource2 -= resource2Cost;
            ecb.SetComponent(connectionEntity, resources);

            // Send sync to client
            var syncRpc = ecb.CreateEntity();
            ecb.AddComponent(syncRpc, new SyncResourcesRpc
            {
                resource1 = resources.resource1,
                resource2 = resources.resource2
            });
            ecb.AddComponent(syncRpc, new SendRpcCommandRequest { TargetConnection = connectionEntity });

            return true;
        }

        return false;
    }
}