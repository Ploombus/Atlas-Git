using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// Unified component that tracks both resources and scores for a player
/// Server-authoritative with automatic score calculation
/// </summary>
[GhostComponent(PrefabType = GhostPrefabType.Server)]
public struct PlayerStats : IComponentData
{
    // Current resources
    [GhostField] public int resource1;
    [GhostField] public int resource2;

    // Lifetime scores (for leaderboard display)
    [GhostField] public int totalScore;
    [GhostField] public int resource1Score;
    [GhostField] public int resource2Score;

    // For display purposes - shows current resources in scoreboard
    public int CurrentResource1 => resource1;
    public int CurrentResource2 => resource2;
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

/// <summary>
/// Single RPC for syncing all player stats to client
/// </summary>
public struct SyncPlayerStatsRpc : IRpcCommand
{
    public int resource1;
    public int resource2;
    public int totalScore;
    public int resource1Score;
    public int resource2Score;
}

/// <summary>
/// Configuration for score calculation
/// </summary>
public struct StatsConfig : IComponentData
{
    public int pointsPerResource1;
    public int pointsPerResource2;

    public static StatsConfig Default => new StatsConfig
    {
        pointsPerResource1 = 10,
        pointsPerResource2 = 10
    };
}

/// <summary>
/// Events for triggering score updates
/// </summary>
public struct StatsChangeEvent : IComponentData
{
    public int resource1Delta;
    public int resource2Delta;
    public Entity playerConnection;
    public bool awardScorePoints; // True for resource gains, false for resource spending
}

public struct DirectScoreEvent : IComponentData
{
    public int scorePoints;
    public Entity playerConnection;
    public ScoreReason reason;
}

public enum ScoreReason : byte
{
    UnitKill = 0,
    UnitSpawn = 1,
    ResourceGathering = 2,
    Custom = 255
}