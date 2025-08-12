using UnityEngine;

public static class UIUtility
{
    public static bool IsPointerOverUI()
    {   

        // TesterUI must exist
        if (TesterUI.Instance == null) return false;

        var panel = TesterUI.Instance.GetRootPanel();
        if (panel == null) return false;

        // Convert screen position (Input.mousePosition) to panel coordinates.
        // UI Toolkit panels use top-left origin with y increasing down, while Input.mousePosition
        // has origin bottom-left; we flip Y.
        Vector2 screenPos = Input.mousePosition;
        Vector2 panelPos = new Vector2(screenPos.x, Screen.height - screenPos.y);

        // If Pick returns non-null, pointer is over some element in that panel
        return panel.Pick(panelPos) != null;
    }
}
