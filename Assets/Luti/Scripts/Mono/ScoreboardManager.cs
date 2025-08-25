using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Enhanced ScoreboardManager that displays both resources and scores
/// Works with the unified PlayerStats system
/// </summary>
public class ScoreboardManager : MonoBehaviour
{
    public static ScoreboardManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Settings")]
    [SerializeField] private bool showScoreboard = true;
    [SerializeField] private float updateInterval = 0.5f;
    [SerializeField] private bool showResourcesInScoreboard = true; // New option

    // UI Elements
    private VisualElement scoreboardContainer;
    private VisualElement scoresContainer;
    private Label localTotalScore;
    private Label localResource1Score;
    private Label localResource2Score;
    private Label localResource1Current; // New - shows current resources
    private Label localResource2Current; // New - shows current resources

    // Data tracking
    private Dictionary<int, PlayerStatsData> playerStats = new Dictionary<int, PlayerStatsData>();
    private PlayerStatsData localPlayerStats;
    private float lastUpdateTime;

    // ECS World reference
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

        // Subscribe to unified stats events
        PlayerStatsUIEvents.OnLocalStatsChanged += OnLocalStatsChanged;
    }

    private void OnDestroy()
    {
        PlayerStatsUIEvents.OnLocalStatsChanged -= OnLocalStatsChanged;
        playerStats?.Clear();

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

    private void InitializeUI()
    {
        if (uiDocument == null)
        {
            Debug.LogError("ScoreboardManager: UIDocument is null!");
            return;
        }

        var root = uiDocument.rootVisualElement;

        // Get main UI elements
        scoreboardContainer = root.Q<VisualElement>("scoreboard-container");
        scoresContainer = root.Q<VisualElement>("scores-container");

        // Get local player elements
        localTotalScore = root.Q<Label>("local-total-score");
        localResource1Score = root.Q<Label>("local-resource1-score");
        localResource2Score = root.Q<Label>("local-resource2-score");

        // Get current resource elements (if they exist in UI)
        localResource1Current = root.Q<Label>("local-resource1-current");
        localResource2Current = root.Q<Label>("local-resource2-current");

        if (scoreboardContainer == null || scoresContainer == null)
        {
            Debug.LogError("ScoreboardManager: Missing required UI elements!");
            return;
        }

        SetScoreboardVisibility(showScoreboard);
        Debug.Log("ScoreboardManager: UI initialized successfully");
    }

    private void FindClientWorld()
    {
        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                clientWorld = world;
                Debug.Log($"ScoreboardManager: Found client world - {world.Name}");
                break;
            }
        }

        if (clientWorld == null)
        {
            Debug.LogWarning("ScoreboardManager: No client world found!");
        }
    }

    private void UpdateStatsDisplay()
    {
        if (clientWorld == null || !clientWorld.IsCreated) return;

        // Get local player stats from ECS
        if (PlayerStatsQueryUtils.TryGetLocalPlayerStats(clientWorld,
            out int resource1, out int resource2,
            out int totalScore, out int resource1Score, out int resource2Score))
        {
            UpdateLocalPlayerStats(totalScore, resource1Score, resource2Score, resource1, resource2);
        }

        // Clear and rebuild scoreboard
        scoresContainer?.Clear();

        // Add local player first
        if (localPlayerStats.hasValidData)
        {
            CreatePlayerStatsEntry("You", localPlayerStats, isLocalPlayer: true);
        }

        // Add other players
        foreach (var kvp in playerStats)
        {
            if (kvp.Value.hasValidData)
            {
                CreatePlayerStatsEntry($"Player {kvp.Key}", kvp.Value, isLocalPlayer: false);
            }
        }
    }

    private void CreatePlayerStatsEntry(string playerName, PlayerStatsData statsData, bool isLocalPlayer)
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

        // Total score (main display)
        var totalScore = new Label(statsData.totalScore.ToString());
        totalScore.AddToClassList("total-score");

        var statsBreakdown = new VisualElement();
        statsBreakdown.AddToClassList("stats-breakdown");

        // Score breakdown
        var resource1Score = new Label($"R1 Score: {statsData.resource1Score}");
        resource1Score.AddToClassList("resource-score");

        var resource2Score = new Label($"R2 Score: {statsData.resource2Score}");
        resource2Score.AddToClassList("resource-score");

        statsBreakdown.Add(resource1Score);
        statsBreakdown.Add(resource2Score);

        // Current resources (if enabled)
        if (showResourcesInScoreboard)
        {
            var resourcesSection = new VisualElement();
            resourcesSection.AddToClassList("current-resources");

            var resource1Current = new Label($"R1: {statsData.currentResource1}");
            resource1Current.AddToClassList("resource-current");

            var resource2Current = new Label($"R2: {statsData.currentResource2}");
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

    // Called by ECS system or events
    public void UpdateLocalPlayerStats(int totalScore, int resource1Score, int resource2Score,
        int currentResource1, int currentResource2)
    {
        localPlayerStats = new PlayerStatsData
        {
            totalScore = totalScore,
            resource1Score = resource1Score,
            resource2Score = resource2Score,
            currentResource1 = currentResource1,
            currentResource2 = currentResource2,
            hasValidData = true
        };

        // Update individual UI elements if they exist
        if (localTotalScore != null) localTotalScore.text = totalScore.ToString();
        if (localResource1Score != null) localResource1Score.text = $"R1: {resource1Score}";
        if (localResource2Score != null) localResource2Score.text = $"R2: {resource2Score}";
        if (localResource1Current != null) localResource1Current.text = currentResource1.ToString();
        if (localResource2Current != null) localResource2Current.text = currentResource2.ToString();
    }

    public void UpdatePlayerStats(int playerId, int totalScore, int resource1Score, int resource2Score,
        int currentResource1, int currentResource2)
    {
        playerStats[playerId] = new PlayerStatsData
        {
            totalScore = totalScore,
            resource1Score = resource1Score,
            resource2Score = resource2Score,
            currentResource1 = currentResource1,
            currentResource2 = currentResource2,
            hasValidData = true
        };
    }

    // Event handler for unified stats system
    private void OnLocalStatsChanged(int resource1, int resource2, int totalScore, int resource1Score, int resource2Score)
    {
        UpdateLocalPlayerStats(totalScore, resource1Score, resource2Score, resource1, resource2);
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

    [ContextMenu("Add Test Stats")]
    private void AddTestStats()
    {
        UpdatePlayerStats(1, 150, 100, 50, 25, 15);
        UpdatePlayerStats(2, 200, 120, 80, 30, 20);
        UpdatePlayerStats(3, 75, 50, 25, 10, 5);
        UpdateLocalPlayerStats(175, 110, 65, 20, 12);
    }
}

/// <summary>
/// Enhanced struct to hold complete player stats for UI display
/// </summary>
[System.Serializable]
public struct PlayerStatsData
{
    public int totalScore;
    public int resource1Score;
    public int resource2Score;
    public int currentResource1;
    public int currentResource2;
    public bool hasValidData;
}