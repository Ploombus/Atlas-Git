using Unity.Entities;
using Unity.NetCode;


//Server-authoritative player resources component
// Attached to player connection entities on the server

[GhostComponent(PrefabType = GhostPrefabType.Server)]
public struct PlayerResources : IComponentData
{
    [GhostField] public int resource1;
    [GhostField] public int resource2;
}

public struct SyncResourcesRpc : IRpcCommand
{
    public int resource1;
    public int resource2;
}

public struct AddResourcesRpc : IRpcCommand
{
    public int resource1ToAdd;
    public int resource2ToAdd;
}