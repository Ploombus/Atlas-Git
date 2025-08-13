using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Singleton manager for spawn progress UI instances
/// Handles creation, pooling, and cleanup of progress bar UI elements
/// </summary>
public class SpawnProgressUIManager : MonoBehaviour
{
    public static SpawnProgressUIManager Instance { get; private set; }

    [Header("UI Settings")]
    [SerializeField] private GameObject progressUIPrefab;
    [SerializeField] private Transform uiParent;
    [SerializeField] private int maxPoolSize = 20;
    [SerializeField] private bool enablePooling = true;

    // UI instance management
    private Queue<GameObject> uiPool = new Queue<GameObject>();
    private Dictionary<Entity, int> activeUIInstances = new Dictionary<Entity, int>();
    private Dictionary<int, GameObject> uiInstancesById = new Dictionary<int, GameObject>();
    private HashSet<GameObject> allInstances = new HashSet<GameObject>();
    private int nextInstanceId = 1;

    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeManager();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeManager()
    {
        // Set default UI parent if none specified
        if (uiParent == null)
        {
            var canvasGO = FindFirstObjectByType<Canvas>();
            if (canvasGO != null)
            {
                uiParent = canvasGO.transform;
            }
            else
            {
                Debug.LogWarning("SpawnProgressUIManager: No Canvas found for UI parent. Progress bars may not display correctly.");
            }
        }

        // Pre-populate pool if pooling is enabled
        if (enablePooling && progressUIPrefab != null)
        {
            PrePopulatePool();
        }
    }

    /// <summary>
    /// Creates a progress UI instance for the specified building entity
    /// </summary>
    /// <param name="buildingEntity">The building entity to create UI for</param>
    /// <returns>The UI instance ID, or -1 if creation failed</returns>
    public int CreateProgressUI(Entity buildingEntity)
    {
        if (progressUIPrefab == null)
        {
            Debug.LogError("SpawnProgressUIManager: Progress UI prefab is not assigned!");
            return -1;
        }

        // Check if this building already has an active UI
        if (activeUIInstances.ContainsKey(buildingEntity))
        {
            Debug.LogWarning($"SpawnProgressUIManager: Building {buildingEntity} already has an active UI instance.");
            return activeUIInstances[buildingEntity];
        }

        GameObject uiInstance = GetUIInstance();
        if (uiInstance != null)
        {
            int instanceId = nextInstanceId++;

            // Configure the UI instance
            var spawnProgressUI = uiInstance.GetComponent<SpawnProgressUI>();
            if (spawnProgressUI != null)
            {
                spawnProgressUI.ResetProgress();
            }

            // Track the instance
            activeUIInstances[buildingEntity] = instanceId;
            uiInstancesById[instanceId] = uiInstance;
            uiInstance.SetActive(true);

            return instanceId;
        }

        Debug.LogError("SpawnProgressUIManager: Failed to create UI instance.");
        return -1;
    }

    /// <summary>
    /// Gets a UI instance by ID
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <returns>The GameObject instance or null</returns>
    public GameObject GetUIInstance(int instanceId)
    {
        return uiInstancesById.TryGetValue(instanceId, out GameObject instance) ? instance : null;
    }

    /// <summary>
    /// Destroys or returns to pool the progress UI instance by ID
    /// </summary>
    /// <param name="instanceId">The UI instance ID to destroy</param>
    public void DestroyProgressUI(int instanceId)
    {
        if (!uiInstancesById.TryGetValue(instanceId, out GameObject uiInstance) || uiInstance == null)
            return;

        // Remove from active instances tracking
        Entity entityToRemove = Entity.Null;
        foreach (var kvp in activeUIInstances)
        {
            if (kvp.Value == instanceId)
            {
                entityToRemove = kvp.Key;
                break;
            }
        }

        if (entityToRemove != Entity.Null)
        {
            activeUIInstances.Remove(entityToRemove);
        }

        uiInstancesById.Remove(instanceId);

        // Return to pool or destroy
        ReturnUIInstance(uiInstance);
    }

    /// <summary>
    /// Gets a UI instance from pool or creates a new one
    /// </summary>
    private GameObject GetUIInstance()
    {
        if (enablePooling && uiPool.Count > 0)
        {
            return uiPool.Dequeue();
        }

        // Create new instance
        GameObject newInstance = Instantiate(progressUIPrefab, uiParent);

        // Ensure the UIDocument is properly initialized
        var uiDocument = newInstance.GetComponent<UIDocument>();
        if (uiDocument != null)
        {
            // Force initialization if needed
            if (uiDocument.rootVisualElement == null)
            {
                uiDocument.enabled = false;
                uiDocument.enabled = true;
            }
        }

        allInstances.Add(newInstance);
        return newInstance;
    }

    /// <summary>
    /// Returns a UI instance to the pool or destroys it
    /// </summary>
    private void ReturnUIInstance(GameObject instance)
    {
        if (instance == null) return;

        // Reset the instance
        var spawnProgressUI = instance.GetComponent<SpawnProgressUI>();
        if (spawnProgressUI != null)
        {
            spawnProgressUI.ResetProgress();
        }

        instance.SetActive(false);

        if (enablePooling && uiPool.Count < maxPoolSize)
        {
            uiPool.Enqueue(instance);
        }
        else
        {
            allInstances.Remove(instance);
            Destroy(instance);
        }
    }

    /// <summary>
    /// Pre-populates the UI pool with instances
    /// </summary>
    private void PrePopulatePool()
    {
        int prePopulateCount = Mathf.Min(5, maxPoolSize); // Pre-create 5 instances or maxPoolSize, whichever is smaller

        for (int i = 0; i < prePopulateCount; i++)
        {
            GameObject instance = Instantiate(progressUIPrefab, uiParent);
            instance.SetActive(false);
            allInstances.Add(instance);
            uiPool.Enqueue(instance);
        }

        Debug.Log($"SpawnProgressUIManager: Pre-populated pool with {prePopulateCount} UI instances.");
    }

    /// <summary>
    /// Cleans up all UI instances and active references
    /// </summary>
    public void CleanupAll()
    {
        // Clear active instances
        foreach (var instanceId in activeUIInstances.Values)
        {
            if (uiInstancesById.TryGetValue(instanceId, out GameObject uiInstance) && uiInstance != null)
            {
                Destroy(uiInstance);
            }
        }
        activeUIInstances.Clear();
        uiInstancesById.Clear();

        // Clear pool
        while (uiPool.Count > 0)
        {
            var instance = uiPool.Dequeue();
            if (instance != null)
            {
                Destroy(instance);
            }
        }

        // Clear all instances
        foreach (var instance in allInstances)
        {
            if (instance != null)
            {
                Destroy(instance);
            }
        }
        allInstances.Clear();
        nextInstanceId = 1;
    }

    private void OnDestroy()
    {
        CleanupAll();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnApplicationQuit()
    {
        CleanupAll();
    }

    // Debug and utility methods
    public int GetActiveUICount() => activeUIInstances.Count;
    public int GetPooledUICount() => uiPool.Count;
    public int GetTotalUICount() => allInstances.Count;

    private void OnGUI()
    {
        if (Application.isPlaying && Debug.isDebugBuild)
        {
            GUILayout.BeginArea(new Rect(10, 100, 200, 100));
            GUILayout.Label($"Active UI: {GetActiveUICount()}");
            GUILayout.Label($"Pooled UI: {GetPooledUICount()}");
            GUILayout.Label($"Total UI: {GetTotalUICount()}");
            GUILayout.EndArea();
        }
    }
}