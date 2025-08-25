/*using Unity.Entities;
using Unity.NetCode;

[GhostComponent(PrefabType = GhostPrefabType.Server)]
public struct PlayerScore : IComponentData
{
    [GhostField] public int totalScore;
    [GhostField] public int resource1Score; // Tracking breakdown for analytics
    [GhostField] public int resource2Score;
}


public struct SyncScoreRpc : IRpcCommand
{
    public int totalScore;
    public int resource1Score;
    public int resource2Score;
}




public struct ResourceChangeEvent : IComponentData      // Added when resources change, consumed by score system
{
    public int resource1Delta;
    public int resource2Delta;
    public Entity playerConnection;
}

public struct DirectScoreEvent : IComponentData
{
    public int scorePoints;
    public Entity playerConnection;
    public ScoreReason reason; // Optional: for analytics/debugging
}

public enum ScoreReason : byte
{
    UnitKill = 0,
    UnitSpawn = 1,
    Gathering = 2,
    Custom = 255
}
public struct ScoreConfig : IComponentData      //score calculation
{
    public int pointsPerResource1;
    public int pointsPerResource2;

    public static ScoreConfig Default => new ScoreConfig
    {
        pointsPerResource1 = 1,
        pointsPerResource2 = 1
    };
}*/
