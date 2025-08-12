using Managers;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;



public class TesterUI : MonoBehaviour
{
    public static TesterUI Instance { get; private set; }

    [SerializeField] UIDocument _TesterUI;

    [SerializeField] private VisualElement root;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private IntegerField counter1Amount;
    [SerializeField] private IntegerField counter2Amount;
    [SerializeField] private IntegerField resource1Input;
    [SerializeField] private IntegerField resource2Input;
    [SerializeField] private Button spawnerButton;
    [SerializeField] private Button addResource1Button;
    [SerializeField] private Button addResource2Button;
    [SerializeField] private bool buildMode;
    [SerializeField] private VisualElement buildingUI;
    [SerializeField] private Button spawnUnitButton;

    private Entity selectedBuilding;
    private World clientWorld;
    private EntityManager entityManager;

    public void Awake()
    {
        Instance = this;
        clientWorld = WorldManager.GetClientWorld();
        //entityManager = clientWorld.EntityManager;

        root = _TesterUI.rootVisualElement;

        spawnerButton = root.Q<Button>("SpawnerButton");
        spawnerButton.RegisterCallback<ClickEvent>(StartBuildMode);

        addResource1Button = root.Q<Button>("AddResource1");
        addResource2Button = root.Q<Button>("AddResource2");
        resource1Input = root.Q<IntegerField>("Resource1Input");
        resource2Input = root.Q<IntegerField>("Resource2Input");
        

        // Resource counter
        counter1Amount = root.Q<IntegerField>("Counter1Amount");
        counter2Amount = root.Q<IntegerField>("Counter2Amount");
        addResource1Button.RegisterCallback<ClickEvent>(AddResource1Amount);
        addResource2Button.RegisterCallback<ClickEvent>(AddResource2Amount);

        resourceManager.OnResourceChanged += UpdateResourceCounter;

        //Building UI
        buildingUI = root.Q<VisualElement>("BuildingUIPanel");
        buildingUI.style.display = DisplayStyle.None;
        //spawnUnitButton = root.Q<Button>("UnitButton");
        //spawnUnitButton.clicked += OnSpawnUnitClicked;

        resource1Input.RegisterCallback<ChangeEvent<int>>((evt) => { resource1Input.value = evt.newValue; });   //update value automatically when changed
        resource2Input.RegisterCallback<ChangeEvent<int>>((evt) => { resource2Input.value = evt.newValue; });   //update value automatically when changed
    }

    private void Update()
    {
        if (UIUtility.IsPointerOverUI()) return;

        if (Input.GetMouseButtonDown(0) && buildMode)
        {       

                int buildCost = 10;

                bool hasEnoughResource1 = counter1Amount.value >= buildCost;
                bool hasEnoughResource2 = counter2Amount.value >= buildCost;

                if (hasEnoughResource1 && hasEnoughResource2 && buildMode)
                {
                    SpawnUnitRpcRequest(MouseWorldPosition.Instance.GetPosition(), 1);
                    resourceManager.RemoveResource(ResourceManager.ResourceType.Resource1, buildCost);
                    resourceManager.RemoveResource(ResourceManager.ResourceType.Resource2, buildCost);
                    return;
                }

                // Otherwise, report missing resources
                if (!hasEnoughResource1)
                {
                    Debug.Log($"You are missing {buildCost - counter1Amount.value} Resource1.");
                }
                if (!hasEnoughResource2)
                {
                    Debug.Log($"You are missing {buildCost - counter2Amount.value} Resource2.");
                }
            
        }
    }

    public IPanel GetRootPanel()
    {
        return _TesterUI?.rootVisualElement?.panel;
    }
    private void StartBuildMode(ClickEvent clickEvent)

    {
        buildMode = !buildMode;
    }

    private void AddResource1Amount(ClickEvent clickEvent) 
    {
        Debug.Log("Add");
        resourceManager.AddResource(ResourceManager.ResourceType.Resource1, resource1Input.value);
    }

    private void AddResource2Amount(ClickEvent clickEvent)
    {
        Debug.Log("Add2");
        resourceManager.AddResource(ResourceManager.ResourceType.Resource2, resource2Input.value);
    }

    private void UpdateResourceCounter(ResourceManager.ResourceType type, int newAmount)
    {
        //Debug.Log($"UI received update: {type} -> {newAmount}");

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
    public void ShowBuildingUI(Entity buildingEntity)
    {
        selectedBuilding = buildingEntity;
        buildingUI.style.display = DisplayStyle.Flex;
    }

    public void HideBuildingUI()
    {
        buildingUI.style.display = DisplayStyle.None;
    }

    private void OnSpawnUnitClicked()
    {
      /*  if (selectedBuilding != Entity.Null && entityManager.Exists(selectedBuilding))
        {
            if (!entityManager.HasComponent<UnitSpawnFromBuilding>(selectedBuilding))
            {
                entityManager.AddComponentData(selectedBuilding, new UnitSpawnFromBuilding { spawnRequested = true });
            }
            else
            {
                var comp = entityManager.GetComponentData<UnitSpawnFromBuilding>(selectedBuilding);
                comp.spawnRequested = true;
                entityManager.SetComponentData(selectedBuilding, comp);
            }
        }*/
    }

    private void OnDestroy()
    {
        //  Unsubscribe when destroyed to avoid memory leaks
        if (resourceManager != null)
        {
            resourceManager.OnResourceChanged -= UpdateResourceCounter;
        }
    }

    public void SpawnUnitRpcRequest(Vector3 position, int owner)
    {
        EntityManager em = WorldManager.GetClientWorld().EntityManager;
        Entity rpc = em.CreateEntity();
        em.AddComponentData(rpc, new SpawnBarracksRpc { position = position, owner = owner });
        em.AddComponentData(rpc, new SendRpcCommandRequest());
    }
}
public struct SpawnBarracksRpc : IRpcCommand
{
    public float3 position;
    public int owner;
}

