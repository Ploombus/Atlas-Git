using Unity.Entities;
using UnityEngine;
using Managers;
using Unity.Transforms;
using Unity.NetCode;

partial struct SelectorVisualSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        //System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if (isInGame == false) return;



        foreach (RefRO<Selected> selected in SystemAPI.Query<RefRO<Selected>>().WithDisabled<Selected>())
        {
            RefRW<LocalTransform> visualLocalTransform = SystemAPI.GetComponentRW<LocalTransform>(selected.ValueRO.selectorEntity);
            {
                visualLocalTransform.ValueRW.Scale = 0f;
            }
        }

        foreach (RefRO<Selected> selected in SystemAPI.Query<RefRO<Selected>>().WithAll<GhostOwnerIsLocal>())
        {
            RefRW<LocalTransform> visualLocalTransform = SystemAPI.GetComponentRW<LocalTransform>(selected.ValueRO.selectorEntity);
            {
                visualLocalTransform.ValueRW.Scale = selected.ValueRO.showScale;
            }
        }


        // Update health indicator color
        foreach (var (unit, entity) in
         SystemAPI.Query<Unit>()
                  .WithAll<UnitHealthIndicator, GhostOwnerIsLocal>()
                  .WithPresent<HealthState>()      // ensure HealthState exists
                  .WithEntityAccess())
        {
            var em = state.EntityManager;

            // Choose stage: predicted for local owner, replicated for others
            HealthStage stage;
            var hs = em.GetComponentData<HealthState>(entity);
            if (em.HasComponent<GhostOwnerIsLocal>(entity))
            {
                stage = HealthStageUtil.ApplyDelta(
                    hs.currentStage, hs.healthChange, HealthStage.Dead, HealthStage.Healthy);
            }
            else
            {
                stage = hs.currentStage;
            }

            // Map stage -> color
            Color color = stage switch
            {
                HealthStage.Healthy  => new Color(0.20f, 0.85f, 0.20f),
                HealthStage.Grazed   => new Color(0.75f, 0.85f, 0.20f),
                HealthStage.Wounded  => new Color(0.95f, 0.55f, 0.15f),
                HealthStage.Critical => new Color(0.90f, 0.20f, 0.20f),
                HealthStage.Dead     => new Color(0.40f, 0.40f, 0.40f),
                _ => Color.magenta
            };

            // Set fill color
            var indicator = em.GetComponentObject<UnitHealthIndicator>(entity);
            if (indicator.fillRenderer != null)
                indicator.fillRenderer.material.color = color;
        }


        // Toggle health indicator visibility
        foreach (var (unit, entity) in
         SystemAPI.Query<Unit>()
                  .WithAll<UnitHealthIndicator, GhostOwnerIsLocal>()
                  .WithPresent<Selected>()
                  .WithEntityAccess())
        {
            var entityManager = state.EntityManager;

            // false if component missing or disabled
            bool isSelected = entityManager.HasComponent<Selected>(entity)
                            && SystemAPI.IsComponentEnabled<Selected>(entity);

            var indicator = entityManager.GetComponentObject<UnitHealthIndicator>(entity);

            if (indicator.backgroundRenderer != null)
                indicator.backgroundRenderer.enabled = isSelected;

            if (indicator.fillRenderer != null)
                indicator.fillRenderer.enabled = isSelected;
        }
    }
}