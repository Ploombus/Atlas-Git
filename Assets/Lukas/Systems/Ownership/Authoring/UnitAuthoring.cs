using Unity.Entities;
using UnityEngine;

public class UnitAuthoring : MonoBehaviour
{
    public class Baker : Baker<UnitAuthoring>
    {
        public override void Bake(UnitAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Unit());
            AddComponent(entity, new MovementDotRef { Dot = Entity.Null });
            AddComponent(entity, new HealthState
            {
                currentStage  = HealthStage.Healthy,
                previousStage = HealthStage.Healthy,
                healthChange  = 0
            });

            AddComponent(entity, new UnitModifiers
            {
                moveSpeedMultiplier = 1f
            });
        }
    }
}

public struct Unit : IComponentData {}

public struct MovementDotRef : IComponentData
{
    public Entity Dot;
}

