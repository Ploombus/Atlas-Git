using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Manages the scoreboard UI display and integrates with ECS score sync system
/// Simple, modular design for easy testing and extension
/// </summary>
public class ScoreboardManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Settings")]
    [SerializeField] private bool showScoreboard = true;
    [SerializeField] private float updateInterval = 0.5f; // Update UI every 0.5 seconds

    // UI Elements
    private VisualElement scoreboardContainer;
    private VisualElement scoresContainer;
    private Label localTotalScore;
    private Label localResource1Score;
    private Label localResource2Score;

    // Score tracking
    private Dictionary<int, PlayerScoreData> playerScores = new Dictionary<int, PlayerScoreData>();
    private PlayerScoreData localPlayerScore;
    private float lastUpdateTime;

    // ECS World reference
    private World clientWorld;

    private void Start()
    {
        InitializeUI();
        FindClientWorld();
    }

    private void Update()
    {
        if (!showScoreboard) return;

        // Update UI at intervals to avoid per-frame overhead
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateScoreDisplay();
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

        // Get local player score elements
        localTotalScore = root.Q<Label>("local-total-score");
        localResource1Score = root.Q<Label>("local-resource1-score");
        localResource2Score = root.Q<Label>("local-resource2-score");

        // Validate UI elements
        if (scoreboardContainer == null || scoresContainer == null)
        {
            Debug.LogError("ScoreboardManager: Missing required UI elements!");
            return;
        }

        // Set initial visibility
        SetScoreboardVisibility(showScoreboard);

        Debug.Log("ScoreboardManager: UI initialized successfully");
    }

    private void FindClientWorld()
    {
        // Find the client world for ECS queries
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

    private void UpdateScoreDisplay()
    {
        if (clientWorld == null || !clientWorld.IsCreated) return;

        // This will be implemented when we integrate with the ECS system
        // For now, just update with dummy data for testing
        UpdateLocalPlayerScore();
        UpdateAllPlayerScores();
    }

    private void UpdateLocalPlayerScore()
    {
        // TODO: Integrate with ScoreQueryUtils.TryGetLocalPlayerScore when implemented
        // For now, use dummy data
        if (localTotalScore != null)
        {
            localTotalScore.text = localPlayerScore.totalScore.ToString();
            localResource1Score.text = $"Resource 1: {localPlayerScore.resource1Score}";
            localResource2Score.text = $"Resource 2: {localPlayerScore.resource2Score}";
        }
    }

    private void UpdateAllPlayerScores()
    {
        // TODO: Query all connected players and their scores from ECS
        // This is where we'll integrate with the networked player data

        // Clear existing score entries (except template)
        ClearPlayerScoreEntries();

        // Add current players (dummy data for now)
        foreach (var kvp in playerScores)
        {
            AddPlayerScoreEntry(kvp.Key, kvp.Value);
        }
    }

    private void ClearPlayerScoreEntries()
    {
        var children = scoresContainer.Children();
        var toRemove = new List<VisualElement>();

        foreach (var child in children)
        {
            // Remove all dynamically created player entries
            if (child.ClassListContains("player-score-entry"))
            {
                toRemove.Add(child);
            }
        }

        foreach (var element in toRemove)
        {
            scoresContainer.Remove(element);
        }
    }

    private void AddPlayerScoreEntry(int playerId, PlayerScoreData scoreData)
    {
        // Clone the template element by copying its structure
        var scoreEntry = new VisualElement();
        scoreEntry.AddToClassList("player-score-entry");

        // Create player info section
        var playerInfo = new VisualElement();
        playerInfo.AddToClassList("player-info");

        var playerName = new Label($"Player {playerId}");
        playerName.AddToClassList("player-name");
        playerName.name = "player-name";

        var playerIdLabel = new Label($"ID: {playerId}");
        playerIdLabel.AddToClassList("player-id");
        playerIdLabel.name = "player-id";

        playerInfo.Add(playerName);
        playerInfo.Add(playerIdLabel);

        // Create score info section
        var scoreInfo = new VisualElement();
        scoreInfo.AddToClassList("score-info");

        var totalScore = new Label(scoreData.totalScore.ToString());
        totalScore.AddToClassList("total-score");
        totalScore.name = "total-score";

        var scoreBreakdown = new VisualElement();
        scoreBreakdown.AddToClassList("score-breakdown");

        var resource1Score = new Label($"R1: {scoreData.resource1Score}");
        resource1Score.AddToClassList("resource-score");
        resource1Score.name = "resource1-score";

        var resource2Score = new Label($"R2: {scoreData.resource2Score}");
        resource2Score.AddToClassList("resource-score");
        resource2Score.name = "resource2-score";

        scoreBreakdown.Add(resource1Score);
        scoreBreakdown.Add(resource2Score);

        scoreInfo.Add(totalScore);
        scoreInfo.Add(scoreBreakdown);

        // Assemble the complete entry
        scoreEntry.Add(playerInfo);
        scoreEntry.Add(scoreInfo);

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

    // Method called by ClientScoreSyncSystem or other ECS systems
    public void UpdatePlayerScore(int playerId, int totalScore, int resource1Score, int resource2Score)
    {
        var scoreData = new PlayerScoreData
        {
            totalScore = totalScore,
            resource1Score = resource1Score,
            resource2Score = resource2Score
        };

        playerScores[playerId] = scoreData;

        // If this is the local player, update local display immediately
        // TODO: Determine local player ID from ECS system
    }

    public void UpdateLocalPlayerScore(int totalScore, int resource1Score, int resource2Score)
    {
        localPlayerScore = new PlayerScoreData
        {
            totalScore = totalScore,
            resource1Score = resource1Score,
            resource2Score = resource2Score
        };
    }

    // For testing purposes
    [ContextMenu("Add Test Scores")]
    private void AddTestScores()
    {
        UpdatePlayerScore(1, 150, 100, 50);
        UpdatePlayerScore(2, 200, 120, 80);
        UpdatePlayerScore(3, 75, 50, 25);
        UpdateLocalPlayerScore(175, 110, 65);
    }

    private void OnDestroy()
    {
        playerScores?.Clear();
    }
}

/// <summary>
/// Simple struct to hold player score data for UI display
/// </summary>
[System.Serializable]
public struct PlayerScoreData
{
    public int totalScore;
    public int resource1Score;
    public int resource2Score;
}