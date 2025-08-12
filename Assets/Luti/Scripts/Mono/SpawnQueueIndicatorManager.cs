using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SpawnQueueIndicatorManager : MonoBehaviour
{
    public static SpawnQueueIndicatorManager Instance { get; private set; }

    [Header("Indicator Settings")]
    [SerializeField] private GameObject indicatorPrefab;
    [SerializeField] private Canvas overlayCanvas;
    [SerializeField] private float updateFrequency = 0.1f; // Update 10 times per second

    private Dictionary<Entity, SpawnQueueIndicatorUI> activeIndicators = new Dictionary<Entity, SpawnQueueIndicatorUI>();
    private float lastUpdateTime;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("SpawnQueueIndicatorManager instance created");

            // ALWAYS create overlay canvas if not assigned
            if (overlayCanvas == null)
            {
                Debug.Log("No canvas assigned, creating overlay canvas automatically");
                CreateOverlayCanvas();
            }
            else
            {
                Debug.Log($"Using assigned canvas: {overlayCanvas.name}");
            }

            if (indicatorPrefab == null)
            {
                Debug.LogError("Indicator prefab is not assigned! Please assign a prefab in the inspector.");
            }
            else
            {
                Debug.Log($"Using indicator prefab: {indicatorPrefab.name}");
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void CreateOverlayCanvas()
    {
        GameObject canvasGO = new GameObject("SpawnQueueOverlayCanvas");
        overlayCanvas = canvasGO.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 100; // Ensure it's on top

        var canvasScaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasScaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920, 1080);

        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Don't destroy on load to persist across scenes
        DontDestroyOnLoad(canvasGO);

        Debug.Log($"Created overlay canvas: {canvasGO.name}");
    }

    public void UpdateIndicator(Entity buildingEntity, float3 worldPosition, int queueCount)
    {
        Debug.Log($"UpdateIndicator called for entity {buildingEntity} with queue count {queueCount} at position {worldPosition}");

        // Only update at specified frequency to avoid performance issues
        if (Time.time - lastUpdateTime < updateFrequency) return;

        if (queueCount > 0)
        {
            Debug.Log($"Queue count > 0, creating/updating indicator for entity {buildingEntity}");

            // Get or create indicator
            if (!activeIndicators.TryGetValue(buildingEntity, out var indicator))
            {
                indicator = CreateIndicator(buildingEntity);
                Debug.Log($"Created new indicator for entity {buildingEntity}");
            }

            // Update indicator
            if (indicator != null)
            {
                indicator.UpdateQueueCount(queueCount);
                indicator.UpdateWorldPosition(worldPosition);
                Debug.Log($"Updated indicator for entity {buildingEntity} with count {queueCount} at position {worldPosition}");
            }
            else
            {
                Debug.LogError($"Failed to create indicator for entity {buildingEntity}");
            }
        }
        else
        {
            // Hide indicator if queue is empty
            HideIndicator(buildingEntity);
        }

        lastUpdateTime = Time.time;
    }

    public void HideIndicator(Entity buildingEntity)
    {
        if (activeIndicators.TryGetValue(buildingEntity, out var indicator))
        {
            if (indicator != null)
            {
                indicator.UpdateQueueCount(0); // This will hide the indicator
                Debug.Log($"Hid indicator for entity {buildingEntity}");
            }
        }
    }

    private SpawnQueueIndicatorUI CreateIndicator(Entity buildingEntity)
    {
        if (indicatorPrefab == null)
        {
            Debug.LogError("Cannot create spawn queue indicator: indicatorPrefab is null! Please assign a prefab in the inspector.");
            return null;
        }

        if (overlayCanvas == null)
        {
            Debug.LogError("Cannot create spawn queue indicator: overlayCanvas is null! This should have been auto-created.");
            return null;
        }

        Debug.Log($"Creating indicator GameObject for entity {buildingEntity}");
        GameObject indicatorGO = Instantiate(indicatorPrefab, overlayCanvas.transform);

        // Make sure the instantiated object is active
        indicatorGO.SetActive(true);

        var indicator = indicatorGO.GetComponent<SpawnQueueIndicatorUI>();

        if (indicator == null)
        {
            Debug.Log("Adding SpawnQueueIndicatorUI component to instantiated prefab");
            indicator = indicatorGO.AddComponent<SpawnQueueIndicatorUI>();
        }

        activeIndicators[buildingEntity] = indicator;
        Debug.Log($"Successfully created indicator for entity {buildingEntity}. Total indicators: {activeIndicators.Count}");
        return indicator;
    }

    private void LateUpdate()
    {
        // Clean up destroyed indicators
        var keysToRemove = new List<Entity>();
        foreach (var kvp in activeIndicators)
        {
            if (kvp.Value == null)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            activeIndicators.Remove(key);
            Debug.Log($"Cleaned up destroyed indicator for entity {key}");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Debug.Log("SpawnQueueIndicatorManager instance destroyed");
        }
    }

    // Public method to check setup in inspector
    [ContextMenu("Check Setup")]
    private void CheckSetup()
    {
        Debug.Log("=== SpawnQueueIndicatorManager Setup Check ===");
        Debug.Log($"Indicator Prefab: {(indicatorPrefab != null ? indicatorPrefab.name : "NULL - ASSIGN THIS!")}");
        Debug.Log($"Overlay Canvas: {(overlayCanvas != null ? overlayCanvas.name : "NULL - Will auto-create")}");
        Debug.Log($"Active Indicators: {activeIndicators.Count}");
        Debug.Log("=== End Setup Check ===");
    }
}