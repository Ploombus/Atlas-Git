using UnityEngine;
using Unity.Entities;
using Unity.VisualScripting;
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
        float dt = SystemAPI.Time.DeltaTime;

        // Link Animator when we have a transform; snap pose before showing (no (0,0,0) T-pose)
        foreach (var (unitGameObjectPrefab, localTransform, entity) in
         SystemAPI.Query<UnitGameObjectPrefab, LocalTransform>()
                  .WithNone<UnitAnimatorReference>()
                  .WithEntityAccess())
        {
            var go = Object.Instantiate(unitGameObjectPrefab.Value);

            // Temporarily hide visuals (but keep GO active so Animator can Update)
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;

            // Snap to entity pose
            go.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);

            var anim = go.GetComponent<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; // helps if offscreen
            anim.Update(0f); // evaluate once at correct pose

            // Reveal visuals
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = true;

            // Store reference
            var animatorRef = new UnitAnimatorReference { Value = anim };
            buffer.AddComponent(entity, animatorRef);



            if (!SystemAPI.HasComponent<PreviousPosition>(entity))
                buffer.AddComponent(entity, new PreviousPosition { hasValue = false });

            var indicatorRoot = new GameObject("HealthIndicator");
            indicatorRoot.transform.SetParent(go.transform, false);
            indicatorRoot.transform.localPosition = new Vector3(0f, 2.4f, 0f);
            indicatorRoot.AddComponent<Billboard>(); // make it face camera

            // Outline (background) quad
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "HI_Background";
            bg.transform.SetParent(indicatorRoot.transform, false);
            bg.transform.localScale   = new Vector3(0.3f, 0.3f, 0.3f);
            var bgRenderer = bg.GetComponent<MeshRenderer>();
            bgRenderer.material.color = Color.white;

            // Fill (slightly smaller)
            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "HI_Fill";
            fill.transform.SetParent(indicatorRoot.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.001f); //prevents clipping
            fill.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            var fillRenderer = fill.GetComponent<MeshRenderer>();
            fillRenderer.material.color = Color.green; // placeholder; we'll drive this next

            // Save refs so we can color/update later
            buffer.AddComponent(entity, new UnitHealthIndicator
            {
                backgroundRenderer = bgRenderer,
                fillRenderer = fillRenderer
            });
        }

        // Animate predicted + interpolated
        foreach (var (localTransform, animatorReference, entity) in
                 SystemAPI.Query<LocalTransform, UnitAnimatorReference>().WithEntityAccess())
        {
            float speed;
            bool isPredicted = SystemAPI.HasComponent<PredictedGhost>(entity);

            if (isPredicted)
            {
                var physicsVelocity = SystemAPI.GetComponent<PhysicsVelocity>(entity);
                speed = math.length(physicsVelocity.Linear);
            }
            else
            {
                var prev = SystemAPI.GetComponentRW<PreviousPosition>(entity);
                speed = prev.ValueRO.hasValue
                    ? math.length(localTransform.Position - prev.ValueRO.value) / math.max(dt, 1e-6f)
                    : 0f;
                prev.ValueRW.value = localTransform.Position;
                prev.ValueRW.hasValue = true;
            }

            animatorReference.Value.SetFloat("Speed", speed, 0.01f, dt);
            animatorReference.Value.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
        }

        // Cleanup
        
        foreach (var (animatorReference, entity) in
                 SystemAPI.Query<UnitAnimatorReference>()
                          .WithNone<UnitGameObjectPrefab, LocalTransform>()
                          .WithEntityAccess())
        {
            Object.Destroy(animatorReference.Value.gameObject);
            buffer.RemoveComponent<UnitAnimatorReference>(entity);
            if (SystemAPI.HasComponent<PreviousPosition>(entity))
                buffer.RemoveComponent<PreviousPosition>(entity);
        }
        

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}

public class UnitHealthIndicator : IComponentData
{
    public Renderer backgroundRenderer;
    public Renderer fillRenderer;
}