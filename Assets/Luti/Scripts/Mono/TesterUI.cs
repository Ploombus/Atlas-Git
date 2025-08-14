using Managers;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Event-driven UI that never directly accesses ECS components
/// All data comes through events from ECS systems
/// </summary>
public class TesterUI : MonoBehaviour
{
    public static TesterUI Instance { get; private set; }

    [SerializeField] UIDocument _TesterUI;
    [SerializeField] private ResourceManager resourceManager;

    // UI Elements
    private VisualElement root;
    private IntegerField counter1Amount;
    private IntegerField counter2Amount;
    private IntegerField resource1Input;
    private IntegerField resource2Input;
    private Button spawnerButton;
    private Button addResource1Button;
    private Button addResource2Button;
    private bool buildMode;
    private VisualElement buildingUI;
    private Button spawnUnitButton;

    // Cached data from events
    private Entity selectedBuilding = Entity.Null;
    private int cachedResource1Cost;
    private int cachedResource2Cost;
    private bool canAffordCurrent;

    public void Awake()
    {
        Instance = this;

        root = _TesterUI.rootVisualElement;



        // Initialize UI elements
        InitializeUIElements();

        // Subscribe to events
        SubscribeToEvents();

        // Subscribe to ResourceManager events
        resourceManager.OnResourceChanged += UpdateResourceCounter;
    }

    private void InitializeUIElements()
    {
        spawnerButton = root.Q<Button>("SpawnerButton");
        spawnerButton.RegisterCallback<ClickEvent>(StartBuildMode);

        addResource1Button = root.Q<Button>("AddResource1");
        addResource2Button = root.Q<Button>("AddResource2");
        resource1Input = root.Q<IntegerField>("Resource1Input");
        resource2Input = root.Q<IntegerField>("Resource2Input");

        counter1Amount = root.Q<IntegerField>("Counter1Amount");
        counter2Amount = root.Q<IntegerField>("Counter2Amount");

        addResource1Button.RegisterCallback<ClickEvent>(AddResource1Amount);
        addResource2Button.RegisterCallback<ClickEvent>(AddResource2Amount);

        buildingUI = root.Q<VisualElement>("BuildingUIPanel");
        buildingUI.style.display = DisplayStyle.None;

        spawnUnitButton = root.Q<Button>("UnitButton");
        spawnUnitButton.RegisterCallback<ClickEvent>(OnSpawnUnitClicked);

        resource1Input.RegisterCallback<ChangeEvent<int>>((evt) => { resource1Input.value = evt.newValue; });
        resource2Input.RegisterCallback<ChangeEvent<int>>((evt) => { resource2Input.value = evt.newValue; });
    }

    private void SubscribeToEvents()
    {
        BuildingUIEvents.OnBuildingSelected += HandleBuildingSelected;
        BuildingUIEvents.OnBuildingDeselected += HandleBuildingDeselected;
        BuildingUIEvents.OnSpawnCostUpdated += HandleSpawnCostUpdated;
        BuildingUIEvents.OnResourcesUpdated += HandleResourcesUpdated;
        BuildingUIEvents.OnSpawnValidated += HandleSpawnValidation;
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        BuildingUIEvents.OnBuildingSelected -= HandleBuildingSelected;
        BuildingUIEvents.OnBuildingDeselected -= HandleBuildingDeselected;
        BuildingUIEvents.OnSpawnCostUpdated -= HandleSpawnCostUpdated;
        BuildingUIEvents.OnResourcesUpdated -= HandleResourcesUpdated;
        BuildingUIEvents.OnSpawnValidated -= HandleSpawnValidation;

        if (resourceManager != null)
        {
            resourceManager.OnResourceChanged -= UpdateResourceCounter;
        }
    }

    // Event Handlers
    private void HandleBuildingSelected(BuildingSelectedEventData data)
    {
        Debug.Log($"TesterUI: Received BuildingSelected event for entity {data.BuildingEntity}");

        selectedBuilding = data.BuildingEntity;
        cachedResource1Cost = data.Resource1Cost;
        cachedResource2Cost = data.Resource2Cost;

        // Show building UI
        buildingUI.style.display = DisplayStyle.Flex;
        Debug.Log("TesterUI: Building UI should now be visible");

        // Update button text with costs
        UpdateUnitButtonDisplay();
    }

    private void HandleBuildingDeselected()
    {
        selectedBuilding = Entity.Null;
        buildingUI.style.display = DisplayStyle.None;
    }

    private void HandleSpawnCostUpdated(SpawnCostUIData data)
    {
        if (data.BuildingEntity != selectedBuilding) return;

        cachedResource1Cost = data.Resource1Cost;
        cachedResource2Cost = data.Resource2Cost;
        canAffordCurrent = data.CanAfford;

        UpdateUnitButtonDisplay();
        UpdateUnitButtonState();
    }

    private void HandleResourcesUpdated(ResourceUIData data)
    {
        canAffordCurrent = data.CanAffordCurrent;
        UpdateUnitButtonState();
    }

    private void HandleSpawnValidation(SpawnValidationData data)
    {
        if (!data.Success)
        {
            // Refund resources
            resourceManager.AddResource(ResourceManager.ResourceType.Resource1, data.RefundResource1);
            resourceManager.AddResource(ResourceManager.ResourceType.Resource2, data.RefundResource2);
            Debug.Log($"Spawn failed: {data.Message}. Resources refunded.");
        }
    }

    // UI Methods (no ECS access)
    private void UpdateUnitButtonDisplay()
    {
        string costText = "Base Unit";
        if (cachedResource1Cost > 0 || cachedResource2Cost > 0)
        {
            costText += "\nCost: ";
            if (cachedResource1Cost > 0)
                costText += $"R1:{cachedResource1Cost} ";
            if (cachedResource2Cost > 0)
                costText += $"R2:{cachedResource2Cost}";
        }

        spawnUnitButton.text = costText;
    }

    private void UpdateUnitButtonState()
    {
        spawnUnitButton.SetEnabled(canAffordCurrent);

        if (canAffordCurrent)
        {
            spawnUnitButton.RemoveFromClassList("insufficient-resources");
            spawnUnitButton.tooltip = "";
        }
        else
        {
            spawnUnitButton.AddToClassList("insufficient-resources");

            // Calculate what's missing
            int currentR1 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource1);
            int currentR2 = resourceManager.GetResourceAmount(ResourceManager.ResourceType.Resource2);
            int missingR1 = Mathf.Max(0, cachedResource1Cost - currentR1);
            int missingR2 = Mathf.Max(0, cachedResource2Cost - currentR2);

            if (missingR1 > 0 || missingR2 > 0)
            {
                string missingText = "Missing: ";
                if (missingR1 > 0) missingText += $"R1:{missingR1} ";
                if (missingR2 > 0) missingText += $"R2:{missingR2}";
                spawnUnitButton.tooltip = missingText;
            }
        }
    }

    private void Update()
    {
        if (UIUtility.IsPointerOverUI()) return;

        if (Input.GetMouseButtonDown(0) && buildMode)
        {
            int buildCost = 0;

            if (resourceManager.HasResources(buildCost, buildCost))
            {
                SpawnBarracksRpcRequest(MouseWorldPosition.Instance.GetPosition(), 1);
                resourceManager.TrySpendResources(buildCost, buildCost);
            }
            else
            {
                resourceManager.GetMissingResources(buildCost, buildCost,
                    out int missingR1, out int missingR2);

                if (missingR1 > 0)
                    Debug.Log($"You are missing {missingR1} Resource1.");
                if (missingR2 > 0)
                    Debug.Log($"You are missing {missingR2} Resource2.");
            }
        }
    }

    // Public methods for external access
    public void ShowBuildingUI(Entity buildingEntity)
    {
        selectedBuilding = buildingEntity;
        // The BuildingUISystem will detect this selection and fire events
        // Don't manipulate UI here - let the event system handle it
    }

    public void HideBuildingUI()
    {
        selectedBuilding = Entity.Null;
        // The BuildingUISystem will detect this deselection and fire events
    }

    // Public getter so BuildingUISystem can check what's selected
    public Entity GetSelectedBuilding()
    {
        return selectedBuilding;
    }

    public IPanel GetRootPanel()
    {
        return _TesterUI?.rootVisualElement?.panel;
    }

    // UI Actions
    private void StartBuildMode(ClickEvent clickEvent)
    {
        buildMode = !buildMode;
        UpdateSpawnerButtonStyle();
    }

    private void AddResource1Amount(ClickEvent clickEvent)
    {
        resourceManager.AddResource(ResourceManager.ResourceType.Resource1, resource1Input.value);
    }

    private void AddResource2Amount(ClickEvent clickEvent)
    {
        resourceManager.AddResource(ResourceManager.ResourceType.Resource2, resource2Input.value);
    }

    private void UpdateResourceCounter(ResourceManager.ResourceType type, int newAmount)
    {
        switch (type)
        {
            case ResourceManager.ResourceType.Resource1:
                counter1Amount.value = newAmount;
                break;
            case ResourceManager.ResourceType.Resource2:
                counter2Amount.value = newAmount;
                break;
        }
    }

    private void OnSpawnUnitClicked(ClickEvent evt)
    {
        if (selectedBuilding == Entity.Null)
        {
            Debug.Log("No building selected to spawn unit from");
            return;
        }

        // Don't do client-side resource validation anymore - let server handle it
        // Just send the spawn request and server will validate

        // Send spawn request RPC
        SendSpawnUnitRpc(selectedBuilding);

        Debug.Log($"Spawn request sent for building {selectedBuilding}");
    }

    private void SendSpawnUnitRpc(Entity buildingEntity)
    {
        var clientWorld = WorldManager.GetClientWorld();
        if (clientWorld == null || !clientWorld.IsCreated)
        {
            Debug.LogError("Client world not available for sending RPC");
            return;
        }

        var em = clientWorld.EntityManager;
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new SpawnUnitFromBuildingRpc { buildingEntity = buildingEntity });
        em.AddComponentData(rpc, new SendRpcCommandRequest());
    }

    public void SpawnBarracksRpcRequest(Vector3 position, int owner)
    {
        var clientWorld = WorldManager.GetClientWorld();
        if (clientWorld == null || !clientWorld.IsCreated)
        {
            Debug.LogError("Client world not available for sending RPC");
            return;
        }

        var em = clientWorld.EntityManager;
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new SpawnBarracksRpc { position = position, owner = owner });
        em.AddComponentData(rpc, new SendRpcCommandRequest());
    }

    private void UpdateSpawnerButtonStyle()
    {
        if (buildMode)
        {
            if (!spawnerButton.ClassListContains("SpawnerButtonClicked"))
                spawnerButton.AddToClassList("SpawnerButtonClicked");
        }
        else
        {
            if (spawnerButton.ClassListContains("SpawnerButtonClicked"))
                spawnerButton.RemoveFromClassList("SpawnerButtonClicked");
        }
    }
}

// RPC definitions
public struct SpawnBarracksRpc : IRpcCommand
{
    public float3 position;
    public int owner;
}

public struct SpawnUnitFromBuildingRpc : IRpcCommand
{
    public Entity buildingEntity;
}