using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// Server-authoritative player resources component
/// Attached to player connection entities on the server
/// </summary>
[GhostComponent(PrefabType = GhostPrefabType.Server)]
public struct PlayerResources : IComponentData
{
    [GhostField] public int resource1;
    [GhostField] public int resource2;
}

/// <summary>
/// RPC to sync resources from server to a specific client
/// </summary>
public struct SyncResourcesRpc : IRpcCommand
{
    public int resource1;
    public int resource2;
}

/// <summary>
/// RPC for client to request resource update (for testing/admin)
/// </summary>
public struct AddResourcesRpc : IRpcCommand
{
    public int resource1ToAdd;
    public int resource2ToAdd;
}