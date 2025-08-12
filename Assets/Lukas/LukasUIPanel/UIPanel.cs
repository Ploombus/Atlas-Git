using UnityEngine;
using UnityEngine.UIElements;
using Managers;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

public class UIPanel : MonoBehaviour
{

    [SerializeField] UIDocument uiDocument;
    [SerializeField] Texture2D lockpng;
    [SerializeField] Texture2D unlockpng;
    [SerializeField] Texture2D loosepng;
    [SerializeField] Texture2D tightpng;
    private VisualElement root;
    bool lockT;
    bool formationT;
    bool spawnMyUnitT;
    bool spawnEnemyUnitT;
    Color baseColor;

    private void OnEnable()
    {
        lockT = false;
        FormationUIState.IsLocked = lockT;
        formationT = true;
        baseColor = new Color32(59, 50, 42, 255);

        root = uiDocument.rootVisualElement;
        var lockFormation = root.Q<VisualElement>("LockFormation");
        var formations = root.Q<VisualElement>("Formations");
        var spawnUnit = root.Q<VisualElement>("SpawnUnit");
        var spawnEnemy = root.Q<VisualElement>("SpawnEnemy");

        lockFormation.RegisterCallback<ClickEvent>(LockFormationButton);
        formations.RegisterCallback<ClickEvent>(FormationsButton);
        spawnUnit.RegisterCallback<ClickEvent>(SpawnMyUnit);
        spawnEnemy.RegisterCallback<ClickEvent>(SpawnEnemyUnit);
    }

    public void Update()
    {
        if (spawnMyUnitT)
        {
            if (Input.GetMouseButtonDown(0) && !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                var mousePosition = MouseWorldPosition.Instance.GetPosition();
                SpawnUnitRpcRequest(mousePosition, 1);
            }
        }
        if (spawnEnemyUnitT)
        {
            if (Input.GetMouseButtonDown(0) && !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                 var mousePosition = MouseWorldPosition.Instance.GetPosition();
                 SpawnUnitRpcRequest(mousePosition, -1);
            }
        }
    }

    public void LockFormationButton(ClickEvent evt)
    {
        lockT = !lockT;

        var lockFormation = root.Q<VisualElement>("LockFormation");

        if (lockT)
        {
            lockFormation.style.backgroundColor = Color.red;
            lockFormation.style.backgroundImage = new StyleBackground(lockpng);
        }
        else
        {
            lockFormation.style.backgroundColor = baseColor;
            lockFormation.style.backgroundImage = new StyleBackground(unlockpng);
        }

        FormationUIState.IsLocked = lockT;
    }
    public void FormationsButton(ClickEvent evt)
    {
        formationT = !formationT;

        var formations = root.Q<VisualElement>("Formations");
        formations.style.backgroundImage = new StyleBackground(formationT ? tightpng : loosepng);
        FormationUIState.SelectedFormation = formationT ? Formations.Tight : Formations.Loose;
    }

    public void SpawnMyUnit(ClickEvent evt)
    {
        var spawnUnit = root.Q<VisualElement>("SpawnUnit");
        var spawnEnemy = root.Q<VisualElement>("SpawnEnemy");

        spawnMyUnitT = !spawnMyUnitT;
        spawnEnemyUnitT = false;
        spawnEnemy.style.backgroundColor = baseColor;

        if (spawnMyUnitT)
        {
            spawnUnit.style.backgroundColor = Color.red;
        }
        else
        {
            spawnUnit.style.backgroundColor = baseColor;
        }

    }
    public void SpawnEnemyUnit(ClickEvent evt)
    {
        var spawnUnit = root.Q<VisualElement>("SpawnUnit");
        var spawnEnemy = root.Q<VisualElement>("SpawnEnemy");
        
        spawnEnemyUnitT = !spawnEnemyUnitT;
        spawnMyUnitT = false;
        spawnUnit.style.backgroundColor = baseColor;

        if (spawnEnemyUnitT)
        {
            spawnEnemy.style.backgroundColor = Color.red;
        }
        else
        {
            spawnEnemy.style.backgroundColor = baseColor;
        }
    }

    public void SpawnUnitRpcRequest(Vector3 position, int owner)
    {
        var em = WorldManager.GetClientWorld().EntityManager;
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new SpawnUnitRpc { position = position, owner = owner });
        em.AddComponentData(rpc, new SendRpcCommandRequest());
    }

}

public static class FormationUIState
{
    public static Formations SelectedFormation = Formations.Tight;
    public static bool IsLocked = false;
}

public struct SpawnUnitRpc : IRpcCommand
{
    public float3 position;
    public int owner;
}