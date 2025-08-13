using Unity.Entities;

/// <summary>
/// Simple spawn progress tracker component
/// </summary>
public struct SpawnProgress : IComponentData
{
    public float currentTime;
    public int unitsInQueue;
}
