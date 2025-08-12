using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;



// MonoBehaviour for the UI indicator GameObject
public class SpawnQueueIndicatorUI : MonoBehaviour
{
    [SerializeField] private TMPro.TextMeshProUGUI queueText;
    [SerializeField] private GameObject indicatorPanel;

    private Camera mainCamera;
    private Canvas canvas;

    private void Awake()
    {
        mainCamera = Camera.main;
        canvas = GetComponentInParent<Canvas>();

        // Ensure we have the required components
        if (queueText == null)
            queueText = GetComponentInChildren<TMPro.TextMeshProUGUI>();

        if (indicatorPanel == null)
            indicatorPanel = gameObject;
    }

    public void UpdateQueueCount(int count)
    {
        if (queueText != null)
        {
            queueText.text = count.ToString();
        }

        // Show/hide indicator based on queue count
        bool shouldShow = count > 0;
        if (indicatorPanel != null)
        {
            indicatorPanel.SetActive(shouldShow);
        }
    }

    public void UpdateWorldPosition(float3 worldPosition)
    {
        if (mainCamera != null && canvas != null)
        {
            // Convert world position to screen position
            Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition);

            // Check if position is in front of camera
            if (screenPos.z > 0)
            {
                // Convert screen position to canvas position
                Vector2 canvasPos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.transform as RectTransform,
                    screenPos,
                    canvas.worldCamera,
                    out canvasPos
                );

                // Apply offset to show above the building
                canvasPos.y += 50f; // Offset above building

                transform.localPosition = canvasPos;

                // Show/hide based on visibility
                gameObject.SetActive(screenPos.z > 0);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}

// Component to mark buildings that should show spawn queue indicators
public struct SpawnQueueIndicator : IComponentData
{
    public int queueCount;
    public bool isVisible;
}