using Unity.Entities;
using System.Collections.Generic;
using Unity.NetCode;
using Unity.Mathematics;

public struct SpawnUnitRpc : IRpcCommand
{
    public float3 position;
}
/*
public struct AddUnitRequest
{
    // Add data if needed, for now it's just a signal
}

public static class UnitRequestQueue
{
    public static List<AddUnitRequest> SpawnNeutralUnit = new();
    public static List<AddUnitRequest> SpawnMyUnit = new();
}
*/