using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using Unity.Physics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct RallyPointInputSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkId>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Get camera
        var mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        // Check for right mouse button click
        if (!Input.GetMouseButtonDown(1))
            return;

        // Get local player's network ID
        var localNetworkId = SystemAPI.GetSingleton<NetworkId>().Value;

        // Perform raycast to get world position
        var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (!PerformRaycast(ref state, ray, out float3 hitPosition))
            return;

        // Query for selected entities (entities with Selected component enabled)
        // and that have GhostOwner component for ownership
        var selectedQuery = SystemAPI.QueryBuilder()
            .WithAll<Selected, Building, GhostOwner>()
            .Build();

        if (selectedQuery.IsEmpty)
            return;

        // Check each selected entity
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var selectedEntity in selectedQuery.ToEntityArray(Allocator.Temp))
        {
            // Check if it's owned by the player
            var ghostOwner = SystemAPI.GetComponent<GhostOwner>(selectedEntity);
            if (ghostOwner.NetworkId != localNetworkId)
                continue;

            // Check if building can spawn units (has spawn queue)
            if (!SystemAPI.HasComponent<BuildingSpawnQueue>(selectedEntity))
                continue;

            Debug.Log($"Setting rally point for building {selectedEntity} at position {hitPosition}");

            // Send RPC to server to set rally point
            var request = ecb.CreateEntity();
            ecb.AddComponent(request, new SetRallyPointRequest
            {
                buildingEntity = selectedEntity,
                rallyPosition = hitPosition,
                ownerNetworkId = localNetworkId
            });
            ecb.AddComponent(request, new SendRpcCommandRequest());

            // Visual feedback (optional) - you could spawn a rally point marker here
            CreateRallyPointVisual(hitPosition);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private bool PerformRaycast(ref SystemState state, UnityEngine.Ray ray, out float3 hitPosition)
    {
        hitPosition = float3.zero;

        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        var raycastInput = new RaycastInput
        {
            Start = ray.origin,
            End = ray.origin + ray.direction * 100f,
            Filter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = 1u << 0, // Adjust this based on your terrain layer
                GroupIndex = 0
            }
        };

        if (collisionWorld.CastRay(raycastInput, out var hit))
        {
            hitPosition = hit.Position;
            return true;
        }

        return false;
    }

    private static void CreateRallyPointVisual(float3 position)
    {
        // Optional: Create a visual indicator for the rally point
        // This could be a particle effect, a flag model, etc.
        // For now, just log it
        Debug.Log($"Rally point visual would be created at {position}");

        // Example: You could instantiate a prefab here
        // GameObject.Instantiate(rallyPointMarkerPrefab, position, Quaternion.identity);
    }
}