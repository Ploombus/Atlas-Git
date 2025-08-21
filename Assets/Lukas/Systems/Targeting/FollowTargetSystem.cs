using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
[UpdateAfter(typeof(TransformSystemGroup))]
partial struct FollowTargetServerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        // COMPLETE transform writers before we read them
        em.CompleteDependencyBeforeRO<LocalToWorld>();
        em.CompleteDependencyBeforeRO<LocalTransform>();

        // World-position resolver (supports Dynamic or Static targets)
        var lt  = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var ltw = SystemAPI.GetComponentLookup<LocalToWorld>(true);
        bool TryGetWorldPos(Entity e, out float3 pos)
        {
            if (lt.HasComponent(e))  { pos = lt[e].Position;  return true; }
            if (ltw.HasComponent(e)) { pos = ltw[e].Position; return true; }
            pos = default; return false;
        }

        const float attackRangeTolerance = 1f;   // shrink the “ring” a bit for enemies
        const float followPadding        = 0.05f; // small buffer for friendlies/neutral

        foreach (var (targetsRW, selfXformRO, selfCombatRO, selfEntity) in
                 SystemAPI.Query<RefRW<UnitTargets>, RefRO<LocalTransform>, RefRO<CombatStats>>()
                          .WithEntityAccess())
        {
            ref var targets = ref targetsRW.ValueRW;

            Entity targetEntity = targets.targetEntity;
            if (targetEntity == Entity.Null)
                continue;

            // Target might have despawned or be static (LTW). If we can’t resolve, clear follow.
            if (!em.Exists(targetEntity) || !TryGetWorldPos(targetEntity, out var targetWorldPos))
            {
                targets.targetEntity = Entity.Null;
                continue;
            }

            float3 selfWorldPos = selfXformRO.ValueRO.Position;
            float3 toTarget     = targetWorldPos - selfWorldPos;
            float  distance     = math.length(toTarget);
            if (distance <= 1e-6f)
                continue;

            // Read horizontal radii (0 if missing)
            float targetRadius = em.HasComponent<TargetingSize>(targetEntity)
                               ? em.GetComponentData<TargetingSize>(targetEntity).radius
                               : 0f;
            float selfRadius   = em.HasComponent<TargetingSize>(selfEntity)
                               ? em.GetComponentData<TargetingSize>(selfEntity).radius
                               : 0f;

            // Simple ownership check (no owner => neutral)
            int selfOwner   = SystemAPI.HasComponent<GhostOwner>(selfEntity)
                            ? SystemAPI.GetComponent<GhostOwner>(selfEntity).NetworkId
                            : int.MinValue;
            int targetOwner = em.HasComponent<GhostOwner>(targetEntity)
                            ? em.GetComponentData<GhostOwner>(targetEntity).NetworkId
                            : int.MinValue;

            bool targetIsEnemy = em.HasComponent<GhostOwner>(targetEntity)
                               && selfOwner != int.MinValue
                               && targetOwner != selfOwner;

            // Desired stop distance from target center:
            //  - Enemies: use attack range (minus a small tolerance)
            //  - Friendlies/neutral: sum of radii + tiny padding
            float effectiveAttackRange = math.max(0f, selfCombatRO.ValueRO.attackRange - attackRangeTolerance);
            float stopDistanceFromCenter = targetIsEnemy
                ? effectiveAttackRange
                : (targetRadius + selfRadius + followPadding);

            float3 dirToTarget = toTarget / distance;
            float3 desiredStopPos = targetWorldPos - dirToTarget * stopDistanceFromCenter;
            float  desiredYaw     = math.atan2(dirToTarget.x, dirToTarget.z);

            // Small “ring” deadband to avoid jitter on the edge
            float radialDeadband = math.max(0.15f, 0.1f * stopDistanceFromCenter);
            float radialDelta    = distance - stopDistanceFromCenter;

            // Exponential smoothing when the ring target “jumps” (e.g., target swap or sudden move)
            float3 goalPosNew = (math.abs(radialDelta) <= radialDeadband) ? selfWorldPos : desiredStopPos;
            float3 goalPosOld = targets.destinationPosition;
            float  dt         = SystemAPI.Time.DeltaTime;
            float  responsiveness = 14f; // 12–20 feels good
            float  alpha      = 1f - math.exp(-responsiveness * dt);

            targets.destinationPosition = math.lerp(goalPosOld, goalPosNew, alpha);
            targets.destinationRotation = desiredYaw; // used when arriving/idle
        }
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsClientPredictSystem))]
partial struct FollowTargetClientPredictSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        // COMPLETE transform writers before we read them
        em.CompleteDependencyBeforeRO<LocalToWorld>();
        em.CompleteDependencyBeforeRO<LocalTransform>();

        var lt = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var ltw = SystemAPI.GetComponentLookup<LocalToWorld>(true);
        bool TryGetWorldPos(Entity e, out float3 pos)
        {
            if (lt.HasComponent(e)) { pos = lt[e].Position; return true; }
            if (ltw.HasComponent(e)) { pos = ltw[e].Position; return true; }
            pos = default; return false;
        }

        const float attackRangeTolerance = 1f;
        const float followPadding = 0.05f;

        foreach (var (targetsRW, selfXformRO, selfCombatRO, selfEntity) in
                 SystemAPI.Query<RefRW<UnitTargets>, RefRO<LocalTransform>, RefRO<CombatStats>>()
                          .WithAll<PredictedGhost>()
                          .WithEntityAccess())
        {
            ref var targets = ref targetsRW.ValueRW;

            Entity targetEntity = targets.targetEntity;
            if (targetEntity == Entity.Null)
                continue;

            if (!em.Exists(targetEntity) || !TryGetWorldPos(targetEntity, out var targetWorldPos))
            {
                targets.targetEntity = Entity.Null;
                continue;
            }

            float3 selfWorldPos = selfXformRO.ValueRO.Position;
            float3 toTarget = targetWorldPos - selfWorldPos;
            float distance = math.length(toTarget);
            if (distance <= 1e-6f)
                continue;

            float targetRadius = em.HasComponent<TargetingSize>(targetEntity)
                               ? em.GetComponentData<TargetingSize>(targetEntity).radius
                               : 0f;
            float selfRadius = em.HasComponent<TargetingSize>(selfEntity)
                               ? em.GetComponentData<TargetingSize>(selfEntity).radius
                               : 0f;

            int selfOwner = SystemAPI.HasComponent<GhostOwner>(selfEntity)
                            ? SystemAPI.GetComponent<GhostOwner>(selfEntity).NetworkId
                            : int.MinValue;
            int targetOwner = em.HasComponent<GhostOwner>(targetEntity)
                            ? em.GetComponentData<GhostOwner>(targetEntity).NetworkId
                            : int.MinValue;

            bool targetIsEnemy = em.HasComponent<GhostOwner>(targetEntity)
                               && selfOwner != int.MinValue
                               && targetOwner != selfOwner;

            float effectiveAttackRange = math.max(0f, selfCombatRO.ValueRO.attackRange - attackRangeTolerance);
            float stopDistanceFromCenter = targetIsEnemy
                ? effectiveAttackRange
                : (targetRadius + selfRadius + followPadding);

            float3 dirToTarget = toTarget / distance;
            float3 desiredStopPos = targetWorldPos - dirToTarget * stopDistanceFromCenter;
            float desiredYaw = math.atan2(dirToTarget.x, dirToTarget.z);

            float radialDeadband = math.max(0.15f, 0.1f * stopDistanceFromCenter);
            float radialDelta = distance - stopDistanceFromCenter;

            float3 goalPosNew = (math.abs(radialDelta) <= radialDeadband) ? selfWorldPos : desiredStopPos;
            float3 goalPosOld = targets.destinationPosition;
            float dt = SystemAPI.Time.DeltaTime;
            float responsiveness = 14f;
            float alpha = 1f - math.exp(-responsiveness * dt);

            targets.destinationPosition = math.lerp(goalPosOld, goalPosNew, alpha);
            targets.destinationRotation = desiredYaw;
        }
    }
}


