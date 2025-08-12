using Unity.Entities;

public struct UnitModifiers : IComponentData
{
    public float moveSpeedMultiplier;
    // add more when needed (vision, locks, etc.)
}

public struct HealthStageTable
{
    public BlobArray<StageEntry> entries;

    public struct StageEntry
    {
        public HealthStage stage;
        public float moveSpeedMultiplier;
        // extend here later if needed
    }
}