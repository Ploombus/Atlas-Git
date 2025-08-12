using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.NetCode;

[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct UnitAnimateSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        float delta = SystemAPI.Time.DeltaTime;
        bool isDead;

        // Init animator
        foreach (var (unitGameObjectPrefab, localTransform, entity) in
                 SystemAPI.Query<UnitGameObjectPrefab, LocalTransform>()
                          .WithNone<UnitAnimatorReference>()
                          .WithEntityAccess())
        {
            var unitBody = Object.Instantiate(unitGameObjectPrefab.Value);

            // Temporarily hide visuals (fix for 0,0,0 Tpose)
            var renderers = unitBody.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;

            unitBody.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
            var anim = unitBody.GetComponent<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Update(0f);

            // Reveal visuals
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = true;

            buffer.AddComponent(entity, new UnitAnimatorReference { Value = anim });
            buffer.AddComponent(entity, new PreviousPosition { hasValue = false });
        }

        // Ensure client cache exists for any animated combat unit
        foreach (var (animRef, entity) in
                 SystemAPI.Query<UnitAnimatorReference>()
                          .WithAll<AttackAnimationState>()
                          .WithNone<AttackAnimClientCache>()
                          .WithEntityAccess())
        {
            var initialTick = SystemAPI.GetComponent<AttackAnimationState>(entity).attackTick;
            buffer.AddComponent(entity, new AttackAnimClientCache { lastSeenTick = initialTick });
        }

        // Animate predicted + interpolated (skip if dead)
        foreach (var (localTransform, animatorReference, health, entity) in
                 SystemAPI.Query<LocalTransform, UnitAnimatorReference, RefRO<HealthState>>()
                          .WithEntityAccess())
        {
            var healthState = health.ValueRO;
            var stage = SystemAPI.HasComponent<GhostOwnerIsLocal>(entity)
                ? HealthStageUtil.ApplyDelta(healthState.currentStage, healthState.healthChange, HealthStage.Dead, HealthStage.Healthy)
                : healthState.currentStage;

            isDead = stage == HealthStage.Dead;
            animatorReference.Value.SetBool("Dead", isDead);

            if (isDead) 
                continue; // don't move corpses

            float speed;
            if (SystemAPI.HasComponent<PredictedGhost>(entity))
            {
                var physicsVelocity = SystemAPI.GetComponent<PhysicsVelocity>(entity);
                speed = math.length(physicsVelocity.Linear);
            }
            else
            {
                var prev = SystemAPI.GetComponentRW<PreviousPosition>(entity);
                speed = prev.ValueRO.hasValue
                    ? math.length(localTransform.Position - prev.ValueRO.value) / math.max(delta, 1e-6f)
                    : 0f;
                prev.ValueRW.value = localTransform.Position;
                prev.ValueRW.hasValue = true;
            }

            animatorReference.Value.SetFloat("Speed", speed, 0.03f, delta);
            animatorReference.Value.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
        }

        // Play attack animation when the replicated tick changes
        foreach (var (animRef, attackStateRO, cacheRW) in
                SystemAPI.Query<UnitAnimatorReference, RefRO<AttackAnimationState>, RefRW<AttackAnimClientCache>>())
        {
            if (attackStateRO.ValueRO.attackTick != cacheRW.ValueRO.lastSeenTick)
            {
                // Prefer a trigger named "Attack" in your Animator Controller
                animRef.Value.SetTrigger("Attack");
                cacheRW.ValueRW.lastSeenTick = attackStateRO.ValueRO.attackTick;
            }
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}

public struct AttackAnimClientCache : IComponentData
{
    public uint lastSeenTick;
}