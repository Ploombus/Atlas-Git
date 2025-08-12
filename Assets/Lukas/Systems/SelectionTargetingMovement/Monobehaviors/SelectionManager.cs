using System;
using Managers;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Unity.Physics;
using Unity.Mathematics;

public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; } //singleton

    public event EventHandler OnSelectionAreaStart;
    public event EventHandler OnSelectionAreaEnd;

    Vector2 selectionStartMousePosition;
    
    private float lastClickTime;
    private float doubleClickThreshold = 0.3f;
    Camera mainCamera;

    private void Awake()
    {
        Instance = this;
        mainCamera = Camera.main;

    }
    private void Update()
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()))
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (UIUtility.IsPointerOverUI())
                {
                    // clicked on UI -> don't start selection
                    return;
                }
                selectionStartMousePosition = Input.mousePosition;
                OnSelectionAreaStart?.Invoke(this, EventArgs.Empty);
            }
            if (Input.GetMouseButtonUp(0))
            {
                if (UIUtility.IsPointerOverUI())
                {
                    // released over UI -> ignore selection end (prevents deselect/hide UI)
                    return;
                }
                Vector2 selectionEndMousePosition = Input.mousePosition;
                EntityManager entityManager = WorldManager.GetClientWorld().EntityManager;

                //Deselecting everything
                EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<Selected>().Build(entityManager);
                NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entityArray.Length; i++)
                {

                    // If it's a building that was selected, hide UI
                    if (entityManager.HasComponent<Building>(entityArray[i]) && entityManager.IsComponentEnabled<Selected>(entityArray[i]))
                    {
                        TesterUI.Instance.HideBuildingUI();
                    }
                    
                    entityManager.SetComponentEnabled<Selected>(entityArray[i], false);
                }

                //Selecting single
                Rect selectionAreaRect = GetSelectionAreaRect();
                float selectionAreaSize = selectionAreaRect.width + selectionAreaRect.height;
                float multipleSelectionSizeMin = 30f;
                bool isSingleSelection = selectionAreaSize < multipleSelectionSizeMin;
                if (isSingleSelection)
                {
                    entityQuery = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
                    PhysicsWorldSingleton physicsWorldSingleton = entityQuery.GetSingleton<PhysicsWorldSingleton>();
                    CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;

                    UnityEngine.Ray cameraRay = Camera.main.ScreenPointToRay(Input.mousePosition);

                    int unitsLayer = 6; //Sixth layer is for units
                    RaycastInput raycastInput = new RaycastInput
                    {
                        Start = cameraRay.GetPoint(0f),
                        End = cameraRay.GetPoint(9999f), //Just a long ray
                        Filter = new CollisionFilter
                        {
                            BelongsTo = ~0u,
                            CollidesWith = 1u << unitsLayer, //Bitmask bit-shift layer
                            GroupIndex = 0,
                        }
                    };
                    if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit raycastHit))
                    {
                        if (entityManager.HasComponent<Selected>(raycastHit.Entity))
                        {
                            entityManager.SetComponentEnabled<Selected>(raycastHit.Entity, true);
                        }

                        // Selecting double
                        if (Time.time - lastClickTime <= doubleClickThreshold)
                        {
                            mainCamera = Camera.main;

                            entityQuery = new EntityQueryBuilder(Allocator.Temp)
                                .WithPresent<LocalToWorld, Selected>()
                                .Build(entityManager);

                            entityArray = entityQuery.ToEntityArray(Allocator.Temp);
                            var localToWorldArray = entityQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

                            for (int i = 0; i < entityArray.Length; i++)
                            {
                                Vector3 viewportPos = mainCamera.WorldToViewportPoint(localToWorldArray[i].Position);

                                bool onScreen = viewportPos.z > 0 &&
                                                viewportPos.x > 0 && viewportPos.x < 1 &&
                                                viewportPos.y > 0 && viewportPos.y < 1;

                                if (onScreen)
                                {
                                    entityManager.SetComponentEnabled<Selected>(entityArray[i], true);
                                }
                            }
                            entityArray.Dispose();
                            localToWorldArray.Dispose();
                        }
                    }
                        
                }

                //Selecting inside rect
                entityQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<LocalTransform, Unit>().WithPresent<Selected>().Build(entityManager);
                entityArray = entityQuery.ToEntityArray(Allocator.Temp);
                NativeArray<LocalTransform> localTransformArray = entityQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                if (isSingleSelection == false)
                {
                    for (int i = 0; i < localTransformArray.Length; i++)
                    {
                        LocalTransform unitLocalTransform = localTransformArray[i];
                        Vector2 unitScreenPosition = Camera.main.WorldToScreenPoint(unitLocalTransform.Position);
                        if (selectionAreaRect.Contains(unitScreenPosition)) //If unit is inside the selection area
                        {
                            entityManager.SetComponentEnabled<Selected>(entityArray[i], true);

                            // If this entity is a building, show its UI
                            if (entityManager.HasComponent<Building>(entityArray[i]))
                            {
                                TesterUI.Instance.ShowBuildingUI(entityArray[i]);
                            }
                        }
                    }
                }

                OnSelectionAreaEnd?.Invoke(this, EventArgs.Empty);

                entityArray.Dispose();      
                lastClickTime = Time.time;
            }
        }
    }

    public Rect GetSelectionAreaRect()
    {
        Vector2 selectionEndMousePosition = Input.mousePosition;
        Vector2 lowerLeftCorner = new Vector2(
            Mathf.Min(selectionStartMousePosition.x, selectionEndMousePosition.x),
            Mathf.Min(selectionStartMousePosition.y, selectionEndMousePosition.y)
        );
        Vector2 upperRightCorner = new Vector2(
            Mathf.Max(selectionStartMousePosition.x, selectionEndMousePosition.x),
            Mathf.Max(selectionStartMousePosition.y, selectionEndMousePosition.y)
        );
        return new Rect(
            lowerLeftCorner.x,
            lowerLeftCorner.y,
            upperRightCorner.x - lowerLeftCorner.x,
            upperRightCorner.y - lowerLeftCorner.y
        );
    }
}