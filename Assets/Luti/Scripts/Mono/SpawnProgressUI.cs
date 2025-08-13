using UnityEngine;
using UnityEngine.UIElements;
using Unity.Mathematics;
using System.Collections;

/// <summary>
/// MonoBehaviour component that handles the visual progress bar for unit spawning
/// Uses Unity UI Toolkit for the progress bar visualization
/// </summary>
public class SpawnProgressUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private string progressBarName = "SpawnProgressBar";
    [SerializeField] private string progressFillName = "ProgressFill";
    [SerializeField] private string queueCounterName = "QueueCounter";
    [SerializeField] private string unitInfoName = "UnitInfo";

    [Header("Positioning")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0, 3f, 0);
    [SerializeField] private bool followWorldPosition = true;

    // UI Elements
    private VisualElement progressBar;
    private VisualElement progressFill;
    private Label queueCounter;
    private Label unitInfo;

    // World positioning
    private Camera mainCamera;
    private float3 targetWorldPosition;
    private bool isVisible;

    private void Awake()
    {
        mainCamera = Camera.main;

        // Initialize UI elements after a frame to ensure UIDocument is ready
        StartCoroutine(InitializeUIElementsDelayed());

        // Hide by default
        SetVisible(false);
    }

    private System.Collections.IEnumerator InitializeUIElementsDelayed()
    {
        yield return null; // Wait one frame
        InitializeUIElements();
    }

    private void InitializeUIElements()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError("SpawnProgressUI: UIDocument component not found!");
            return;
        }

        // Ensure UIDocument is enabled and initialized
        if (!uiDocument.enabled)
        {
            uiDocument.enabled = true;
        }

        // Wait for root element to be available
        if (uiDocument.rootVisualElement == null)
        {
            Debug.LogWarning("SpawnProgressUI: Root visual element not ready, retrying...");
            StartCoroutine(RetryInitialization());
            return;
        }

        var root = uiDocument.rootVisualElement;
        progressBar = root.Q<VisualElement>(progressBarName);
        progressFill = root.Q<VisualElement>(progressFillName);
        queueCounter = root.Q<Label>(queueCounterName);
        unitInfo = root.Q<Label>(unitInfoName);

        // Set initial styles if elements exist
        if (progressBar != null)
        {
            progressBar.style.position = Position.Absolute;
        }
        else
        {
            Debug.LogError($"SpawnProgressUI: Could not find progress bar element '{progressBarName}'");
        }

        if (progressFill != null)
        {
            progressFill.style.width = Length.Percent(0);
        }
        else
        {
            Debug.LogError($"SpawnProgressUI: Could not find progress fill element '{progressFillName}'");
        }

        if (queueCounter == null)
        {
            Debug.LogError($"SpawnProgressUI: Could not find queue counter element '{queueCounterName}'");
        }

        if (unitInfo == null)
        {
            Debug.LogError($"SpawnProgressUI: Could not find unit info element '{unitInfoName}'");
        }
    }

    private System.Collections.IEnumerator RetryInitialization()
    {
        int retryCount = 0;
        while (retryCount < 10 && (uiDocument == null || uiDocument.rootVisualElement == null))
        {
            yield return new WaitForSeconds(0.1f);
            retryCount++;

            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                InitializeUIElements();
                yield break;
            }
        }

        if (retryCount >= 10)
        {
            Debug.LogError("SpawnProgressUI: Failed to initialize UI elements after multiple retries!");
        }
    }

    private void Update()
    {
        if (followWorldPosition && isVisible)
        {
            UpdateScreenPosition();
        }
    }

    /// <summary>
    /// Updates the progress bar fill based on spawn progress
    /// </summary>
    /// <param name="currentTime">Current spawn time elapsed</param>
    /// <param name="totalTime">Total time required for spawning</param>
    public void UpdateProgress(float currentTime, float totalTime)
    {
        if (progressFill == null) return;

        float progress = totalTime > 0 ? Mathf.Clamp01(currentTime / totalTime) : 0f;
        progressFill.style.width = Length.Percent(progress * 100f);

        // Optional: Update progress color based on progress
        UpdateProgressColor(progress);
    }

    /// <summary>
    /// Updates the queue counter display
    /// </summary>
    /// <param name="currentUnit">Current unit being spawned (1-based)</param>
    /// <param name="totalUnits">Total units in queue</param>
    public void UpdateQueueInfo(int currentUnit, int totalUnits)
    {
        if (queueCounter != null)
        {
            queueCounter.text = $"{currentUnit}/{totalUnits}";
        }

        if (unitInfo != null)
        {
            unitInfo.text = totalUnits > 1 ? $"Spawning Unit {currentUnit}" : "Spawning Unit";
        }
    }

    /// <summary>
    /// Sets the world position that this UI should follow
    /// </summary>
    /// <param name="worldPos">World position to follow</param>
    public void SetWorldPosition(Vector3 worldPos)
    {
        targetWorldPosition = worldPos + worldOffset;
        UpdateScreenPosition();
    }

    /// <summary>
    /// Shows or hides the progress bar
    /// </summary>
    /// <param name="visible">Whether the progress bar should be visible</param>
    public void SetVisible(bool visible)
    {
        isVisible = visible;

        if (progressBar != null)
        {
            progressBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        gameObject.SetActive(visible);
    }

    /// <summary>
    /// Updates the screen position based on world position
    /// </summary>
    private void UpdateScreenPosition()
    {
        if (mainCamera == null || progressBar == null) return;

        Vector3 screenPos = mainCamera.WorldToScreenPoint(targetWorldPosition);

        // Check if position is in front of camera and within screen bounds
        bool inView = screenPos.z > 0 &&
                     screenPos.x >= 0 && screenPos.x <= Screen.width &&
                     screenPos.y >= 0 && screenPos.y <= Screen.height;

        if (inView)
        {
            // Convert to UI Toolkit coordinates (top-left origin)
            float uiX = screenPos.x;
            float uiY = Screen.height - screenPos.y;

            progressBar.style.left = uiX;
            progressBar.style.top = uiY;

            if (!progressBar.style.display.Equals(DisplayStyle.Flex))
            {
                progressBar.style.display = DisplayStyle.Flex;
            }
        }
        else
        {
            progressBar.style.display = DisplayStyle.None;
        }
    }

    /// <summary>
    /// Updates progress bar color based on completion percentage
    /// </summary>
    /// <param name="progress">Progress value from 0-1</param>
    private void UpdateProgressColor(float progress)
    {
        if (progressFill == null) return;

        // Color interpolation: Red -> Yellow -> Green
        Color progressColor;
        if (progress < 0.5f)
        {
            // Red to Yellow
            progressColor = Color.Lerp(Color.red, Color.yellow, progress * 2f);
        }
        else
        {
            // Yellow to Green
            progressColor = Color.Lerp(Color.yellow, Color.green, (progress - 0.5f) * 2f);
        }

        progressFill.style.backgroundColor = progressColor;
    }

    /// <summary>
    /// Resets the progress bar to initial state
    /// </summary>
    public void ResetProgress()
    {
        UpdateProgress(0f, 1f);
        UpdateQueueInfo(0, 0);
        SetVisible(false);
    }

    private void OnDestroy()
    {
        // Clean up any references
        progressBar = null;
        progressFill = null;
        queueCounter = null;
        unitInfo = null;
    }
}