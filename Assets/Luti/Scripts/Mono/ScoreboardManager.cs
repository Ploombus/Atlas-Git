using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Collections;

/// <summary>
/// SIMPLIFIED: ScoreboardManager that reads Ghost-replicated PlayerStats directly
/// No complex caching or event handling - just reads from ECS when needed
/// </summary>
public class ScoreboardManager : MonoBehaviour
{
    public static ScoreboardManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Settings")]
    [SerializeField] private bool showScoreboard = true;
    [SerializeField] private float updateInterval = 0.5f;
    [SerializeField] private bool showResourcesInScoreboard = true;

    // UI Elements
    private VisualElement scoreboardContainer;
    private VisualElement scoresContainer;
    private Label localTotalScore;
    private Label localResource1Score;
    private Label localResource2Score;
    private Label localResource1Current;
    private Label localResource2Current;

    // Simple tracking
    private float lastUpdateTime;
    private World clientWorld;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        InitializeUI();
        FindClientWorld();

        // SIMPLIFIED: Only listen to the general update event
        PlayerStatsUIEvents.OnAllPlayerStatsUpdated += ForceUpdateDisplay;
    }

    private void OnDestroy()
    {
        PlayerStatsUIEvents.OnAllPlayerStatsUpdated -= ForceUpdateDisplay;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!showScoreboard) return;

        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateStatsDisplay();
            lastUpdateTime = Time.time;
        }
    }

    private void ForceUpdateDisplay()
    {
        UpdateStatsDisplay();
    }

    private void InitializeUI()
    {
        if (uiDocument == null)
        {
            Debug.LogError("ScoreboardManager: UIDocument is null!");
            return;
        }

        var root = uiDocument.rootVisualElement;
        scoreboardContainer = root.Q<VisualElement>("scoreboard-container");
        scoresContainer = root.Q<VisualElement>("scores-container");
        localTotalScore = root.Q<Label>("local-total-score");
        localResource1Score = root.Q<Label>("local-resource1-score");
        localResource2Score = root.Q<Label>("local-resource2-score");
        localResource1Current = root.Q<Label>("local-resource1-current");
        localResource2Current = root.Q<Label>("local-resource2-current");

        if (scoreboardContainer == null || scoresContainer == null)
        {
            Debug.LogError("ScoreboardManager: Missing required UI elements!");
            return;
        }

        SetScoreboardVisibility(showScoreboard);
    }

    private void FindClientWorld()
    {
        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                clientWorld = world;
                break;
            }
        }
    }

    /// <summary>
    /// SIMPLIFIED: Direct ECS query approach - reads all PlayerStats from Ghost components
    /// </summary>
    private void UpdateStatsDisplay()
    {
        if (clientWorld == null || !clientWorld.IsCreated) return;

        var entityManager = clientWorld.EntityManager;

        // Clear scoreboard
        scoresContainer?.Clear();

        // SIMPLIFIED: Direct query for all PlayerStats
        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerStats>(),
            ComponentType.ReadOnly<NetworkId>()
        );

        if (query.IsEmpty) return;

        var allStats = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);
        var allNetIds = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
        var localPlayerId = GetLocalPlayerId();

        // Create a simple list and sort by score
        var playerDisplayData = new List<(PlayerStats stats, bool isLocal)>();

        for (int i = 0; i < allStats.Length; i++)
        {
            var stats = allStats[i];
            bool isLocal = (stats.playerId == localPlayerId);

            // Update local player UI elements if this is local player
            if (isLocal)
            {
                UpdateLocalPlayerUI(stats);
            }

            playerDisplayData.Add((stats, isLocal));
        }

        // Sort by total score (descending)
        playerDisplayData.Sort((a, b) => b.stats.totalScore.CompareTo(a.stats.totalScore));

        // Create UI entries
        foreach (var (stats, isLocal) in playerDisplayData)
        {
            CreatePlayerStatsEntry(
                isLocal ? "You" : $"Player {stats.playerId}",
                stats,
                isLocal
            );
        }

        allStats.Dispose();
        allNetIds.Dispose();
    }

    private void UpdateLocalPlayerUI(PlayerStats stats)
    {
        if (localTotalScore != null) localTotalScore.text = stats.totalScore.ToString();
        if (localResource1Score != null) localResource1Score.text = $"R1: {stats.resource1Score}";
        if (localResource2Score != null) localResource2Score.text = $"R2: {stats.resource2Score}";
        if (localResource1Current != null) localResource1Current.text = stats.resource1.ToString();
        if (localResource2Current != null) localResource2Current.text = stats.resource2.ToString();
    }

    private int GetLocalPlayerId()
    {
        if (clientWorld == null || !clientWorld.IsCreated) return -1;

        var entityManager = clientWorld.EntityManager;

        // First try to find using GhostOwnerIsLocal (preferred method)
        using var ghostOwnerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GhostOwner>(),
            ComponentType.ReadOnly<GhostOwnerIsLocal>()
        );

        if (!ghostOwnerQuery.IsEmpty)
        {
            var ghostOwners = ghostOwnerQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            if (ghostOwners.Length > 0)
            {
                int localId = ghostOwners[0].NetworkId;
                ghostOwners.Dispose();
                return localId;
            }
            ghostOwners.Dispose();
        }

        // Fallback: find the first NetworkStreamConnection (client connection)
        using var connectionQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkStreamConnection>(),
            ComponentType.ReadOnly<NetworkId>()
        );

        if (!connectionQuery.IsEmpty)
        {
            var networkIds = connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            if (networkIds.Length > 0)
            {
                var localId = networkIds[0].Value;
                networkIds.Dispose();
                return localId;
            }
            networkIds.Dispose();
        }

        return -1;
    }

    private void CreatePlayerStatsEntry(string playerName, PlayerStats stats, bool isLocalPlayer)
    {
        var scoreEntry = new VisualElement();
        scoreEntry.AddToClassList("score-entry");
        if (isLocalPlayer) scoreEntry.AddToClassList("local-player");

        // Player info section
        var playerInfo = new VisualElement();
        playerInfo.AddToClassList("player-info");

        var nameLabel = new Label(playerName);
        nameLabel.AddToClassList("player-name");
        playerInfo.Add(nameLabel);

        // Stats info section
        var statsInfo = new VisualElement();
        statsInfo.AddToClassList("stats-info");

        // Total score
        var totalScore = new Label(stats.totalScore.ToString());
        totalScore.AddToClassList("total-score");

        var statsBreakdown = new VisualElement();
        statsBreakdown.AddToClassList("stats-breakdown");

        // Score breakdown
        var resource1Score = new Label($"R1 Score: {stats.resource1Score}");
        resource1Score.AddToClassList("resource-score");

        var resource2Score = new Label($"R2 Score: {stats.resource2Score}");
        resource2Score.AddToClassList("resource-score");

        statsBreakdown.Add(resource1Score);
        statsBreakdown.Add(resource2Score);

        // Current resources (if enabled)
        if (showResourcesInScoreboard)
        {
            var resourcesSection = new VisualElement();
            resourcesSection.AddToClassList("current-resources");

            var resource1Current = new Label($"R1: {stats.resource1}");
            resource1Current.AddToClassList("resource-current");

            var resource2Current = new Label($"R2: {stats.resource2}");
            resource2Current.AddToClassList("resource-current");

            resourcesSection.Add(resource1Current);
            resourcesSection.Add(resource2Current);
            statsBreakdown.Add(resourcesSection);
        }

        statsInfo.Add(totalScore);
        statsInfo.Add(statsBreakdown);

        scoreEntry.Add(playerInfo);
        scoreEntry.Add(statsInfo);

        scoresContainer.Add(scoreEntry);
    }

    public void SetScoreboardVisibility(bool visible)
    {
        showScoreboard = visible;
        if (scoreboardContainer != null)
        {
            scoreboardContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public void ToggleScoreboard()
    {
        SetScoreboardVisibility(!showScoreboard);
    }

    public void ToggleResourceDisplay()
    {
        showResourcesInScoreboard = !showResourcesInScoreboard;
    }
}