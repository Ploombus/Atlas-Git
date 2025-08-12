using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Mathematics;

public class UnitAuthoring : MonoBehaviour
{
    public GameObject UnitGameObjectPrefab;
    public float moveSpeed;
    public float rotationSpeed;

    public class Baker : Baker<UnitAuthoring>
    {
        public override void Bake(UnitAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Unit());
            AddComponent(entity, new MovementDotRef { Dot = Entity.Null });
            AddComponent(entity, new UnitStats
            {
                moveSpeed = authoring.moveSpeed,
                rotationSpeed = authoring.rotationSpeed,
            });
            AddComponent(entity, new UnitTargetNetcode());
            AddComponent(entity, new UnitMover());
            AddComponent(entity, new HealthState
            {
                currentStage = HealthStage.Healthy,
                previousStage = HealthStage.Healthy,
                healthChange = 0
            });
            AddComponent(entity, new UnitModifiers { moveSpeedMultiplier = 1f });
            AddComponentObject(entity, new UnitGameObjectPrefab { Value = authoring.UnitGameObjectPrefab });
        }
    }
}


public struct Unit : IComponentData { }
public struct MovementDotRef : IComponentData { public Entity Dot; }
public struct UnitStats : IComponentData
{
    [GhostField] public float moveSpeed;
    [GhostField] public float rotationSpeed;
}
public struct UnitMover : IComponentData
{
    [GhostField] public float3 targetPosition;
    [GhostField] public float targetRotation;
    [GhostField] public bool activeTarget;
    [GhostField] public bool isRunning;
    [GhostField] public uint lastAppliedSequence;
}
public struct UnitTargetNetcode : IInputComponentData
{
    public float3 requestTargetPosition;
    public float requestTargetRotation;
    public bool requestActiveTarget;
    public bool requestIsRunning;
    public uint requestSequence;
}

//something for calculating speed? i dont get it...
public struct PreviousPosition : IComponentData
{
    public float3 value;
    public bool hasValue;
}

public struct UnitModifiers : IComponentData
{
    public float moveSpeedMultiplier;
}

public sealed class UnitGameObjectPrefab : IComponentData
{
    public GameObject Value;
}
public sealed class UnitAnimatorReference : IComponentData
{
    public Animator Value;
}
public sealed class UnitHealthIndicator : IComponentData
{
    public Renderer backgroundRenderer;
    public Renderer fillRenderer;
}