using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Managers;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct DotVisualSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MovementDot>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;

        var unitMoverLookup = SystemAPI.GetComponentLookup<UnitMover>(true);
        var selectedLookup = SystemAPI.GetComponentLookup<Selected>(true);

        foreach (
            var (dotTransform, dotData)
            in SystemAPI.Query<RefRW<LocalTransform>, RefRO<MovementDot>>())
        {
            Entity unit = dotData.ValueRO.owner;

            bool isSelected = selectedLookup.HasComponent(unit) &&
                              selectedLookup.IsComponentEnabled(unit);

            bool isActive = unitMoverLookup.HasComponent(unit) &&
                            unitMoverLookup[unit].activeTarget;

            // Show or hide
            if (isSelected && isActive)
            {
                float scale = 0.15f + math.sin(time * 5f) * 0.05f; // pulsing
                dotTransform.ValueRW.Scale = scale;
            }
            else
            {
                dotTransform.ValueRW.Scale = 0f; // hide
            }
            
        }
        
    }
}