using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Simple bridge component that connects ECS score system to UI Manager
/// Runs on client world and updates the ScoreboardManager when scores change
/// </summary>
public class ScoreboardECSBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScoreboardManager scoreboardManager;

    [Header("Settings")]
    [SerializeField] private float updateFrequency = 1f; // How often to check for score updates

    private World clientWorld;
    private float lastUpdateTime;
    private EntityQuery scoreRpcQuery;

    // Cache for latest scores to avoid unnecessary UI updates
    private NativeHashMap<int, PlayerScoreData> cachedScores;
    private bool initialized = false;

    private void Start()
    {
        InitializeECSBridge();
    }

    private void Update()
    {
        if (!initialized || clientWorld == null || !clientWorld.IsCreated) return;

        // Check for score updates at specified frequency
        if (Time.time - lastUpdateTime >= (1f / updateFrequency))
        {
            ProcessScoreUpdates();
            lastUpdateTime = Time.time;
        }
    }

    private void InitializeECSBridge()
    {
        // Find ScoreboardManager if not assigned
        if (scoreboardManager == null)
        {
            scoreboardManager = FindFirstObjectByType<ScoreboardManager>();
            if (scoreboardManager == null)
            {
                Debug.LogError("ScoreboardECSBridge: No ScoreboardManager found!");
                return;
            }
        }

        // Find client world
        FindClientWorld();

        if (clientWorld != null)
        {
            // Initialize native collections
            cachedScores = new NativeHashMap<int, PlayerScoreData>(16, Allocator.Persistent);

            initialized = true;
            Debug.Log("ScoreboardECSBridge: Initialized successfully");
        }
    }

    private void FindClientWorld()
    {
        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                clientWorld = world;
                Debug.Log($"ScoreboardECSBridge: Connected to client world - {world.Name}");
                break;
            }
        }

        if (clientWorld == null)
        {
            Debug.LogWarning("ScoreboardECSBridge: No client world found, retrying...");
        }
    }

    private void ProcessScoreUpdates()
    {
        // This is a simplified approach - in a real implementation you'd want to:
        // 1. Listen for score RPC events from ClientScoreSyncSystem
        // 2. Query player entities with score components
        // 3. Track which player is the local player

        // For now, we'll implement a basic polling system that works with the existing setup
        var entityManager = clientWorld.EntityManager;

        // Check if we have any score sync RPCs pending
        using var scoreRpcQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<SyncScoreRpc>(),
            ComponentType.ReadOnly<ReceiveRpcCommandRequest>()
        );

        if (scoreRpcQuery.CalculateEntityCount() > 0)
        {
            // Process pending score RPCs
            var scoreRpcs = scoreRpcQuery.ToComponentDataArray<SyncScoreRpc>(Allocator.Temp);
            var rpcRequests = scoreRpcQuery.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);

            for (int i = 0; i < scoreRpcs.Length; i++)
            {
                var scoreRpc = scoreRpcs[i];
                var request = rpcRequests[i];

                // For now, assume this is the local player's score
                // In a full implementation, you'd determine the player ID from the connection
                int playerId = GetPlayerIdFromConnection(request.SourceConnection);

                UpdatePlayerScore(playerId, scoreRpc.totalScore, scoreRpc.resource1Score, scoreRpc.resource2Score);

                // If this is the local player (you'd determine this properly in a real implementation)
                bool isLocalPlayer = IsLocalPlayer(request.SourceConnection);
                if (isLocalPlayer)
                {
                    scoreboardManager.UpdateLocalPlayerScore(
                        scoreRpc.totalScore,
                        scoreRpc.resource1Score,
                        scoreRpc.resource2Score
                    );
                }
            }

            scoreRpcs.Dispose();
            rpcRequests.Dispose();
        }
    }

    private void UpdatePlayerScore(int playerId, int totalScore, int resource1Score, int resource2Score)
    {
        var newScoreData = new PlayerScoreData
        {
            totalScore = totalScore,
            resource1Score = resource1Score,
            resource2Score = resource2Score
        };

        // Check if score actually changed to avoid unnecessary UI updates
        bool shouldUpdate = !cachedScores.TryGetValue(playerId, out var cachedScore) ||
                           !ScoreDataEquals(cachedScore, newScoreData);

        if (shouldUpdate)
        {
            cachedScores[playerId] = newScoreData;
            scoreboardManager.UpdatePlayerScore(playerId, totalScore, resource1Score, resource2Score);
        }
    }

    private bool ScoreDataEquals(PlayerScoreData a, PlayerScoreData b)
    {
        return a.totalScore == b.totalScore &&
               a.resource1Score == b.resource1Score &&
               a.resource2Score == b.resource2Score;
    }

    private int GetPlayerIdFromConnection(Entity connectionEntity)
    {
        // TODO: Implement proper player ID resolution
        // This would typically involve querying the NetworkId component
        // or maintaining a connection-to-player mapping

        var entityManager = clientWorld.EntityManager;
        if (entityManager.HasComponent<NetworkId>(connectionEntity))
        {
            var networkId = entityManager.GetComponentData<NetworkId>(connectionEntity);
            return networkId.Value;
        }

        return 0; // Fallback
    }

    private bool IsLocalPlayer(Entity connectionEntity)
    {
        // TODO: Implement proper local player detection
        // This would check if the connection entity represents the local client

        var entityManager = clientWorld.EntityManager;

        // Check if this connection has the CommandTarget component (indicates local player)
        if (entityManager.HasComponent<CommandTarget>(connectionEntity))
        {
            return true;
        }

        return false; // Fallback - assume first player is local for testing
    }

    public void ForceRefresh()
    {
        if (initialized)
        {
            ProcessScoreUpdates();
        }
    }

    private void OnDestroy()
    {
        if (cachedScores.IsCreated)
        {
            cachedScores.Dispose();
        }
    }

    // Debug methods for testing
    [ContextMenu("Test Score Update")]
    private void TestScoreUpdate()
    {
        if (scoreboardManager != null)
        {
            scoreboardManager.UpdateLocalPlayerScore(100, 60, 40);
            scoreboardManager.UpdatePlayerScore(1, 150, 90, 60);
            scoreboardManager.UpdatePlayerScore(2, 200, 120, 80);
        }
    }
}