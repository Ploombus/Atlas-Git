using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Mathematics;

public class UnitMoverAuthoring : MonoBehaviour
{
    public float moveSpeed;
    public float rotationSpeed;

    public class Baker : Baker<UnitMoverAuthoring>
    {
        
        public override void Bake(UnitMoverAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitMover
            {
                moveSpeed = authoring.moveSpeed,
                rotationSpeed = authoring.rotationSpeed,
            });
            AddComponent(entity, new UnitTargetNetcode());
        }
    }
}
public struct UnitMover : IComponentData
{
    [GhostField] public float moveSpeed;
    [GhostField] public float rotationSpeed;
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

public struct PreviousPosition : IComponentData
{
    public float3 value;
    public bool hasValue;
}