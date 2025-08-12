using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct CombatServerSystem : ISystem
{
    private Random _rng;

    // [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        _rng = Random.CreateFromIndex(0xD00DFEEDu);
    }

    // [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTimeSeconds = SystemAPI.Time.DeltaTime;

        // Collect candidates once
        var targetEntitiesList   = new NativeList<Entity>(Allocator.Temp);
        var targetPositionsList  = new NativeList<float3>(Allocator.Temp);
        var targetOwnerIdsList   = new NativeList<int>(Allocator.Temp);
        var targetAliveFlagsList = new NativeList<bool>(Allocator.Temp);

        foreach (var (localTransformRO, entity) in SystemAPI
                     .Query<RefRO<LocalTransform>>()
                     .WithAll<Unit>()
                     .WithEntityAccess())
        {
            int ownerNetworkId = SystemAPI.HasComponent<GhostOwner>(entity)
                ? SystemAPI.GetComponent<GhostOwner>(entity).NetworkId
                : -1;

            bool isAlive = true;
            if (SystemAPI.HasComponent<HealthState>(entity))
            {
                var health = SystemAPI.GetComponent<HealthState>(entity);
                isAlive = health.currentStage != HealthStage.Dead;
            }

            targetEntitiesList.Add(entity);
            targetPositionsList.Add(localTransformRO.ValueRO.Position);
            targetOwnerIdsList.Add(ownerNetworkId);
            targetAliveFlagsList.Add(isAlive);
        }

        foreach (var (localTransformRO, proximityRO, attackStatsRO, cooldownRW, windupRW, attackerEntity) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<ProximitySensor>, RefRO<AttackStats>, RefRW<AttackCooldown>, RefRW<AttackWindup>>()
                     .WithAll<Unit>()
                     .WithEntityAccess())
        {
            // Skip dead attackers
            if (SystemAPI.HasComponent<HealthState>(attackerEntity))
            {
                var attackerHealth = SystemAPI.GetComponent<HealthState>(attackerEntity);
                if (attackerHealth.currentStage == HealthStage.Dead)
                    continue;
            }

            int attackerOwnerId = SystemAPI.HasComponent<GhostOwner>(attackerEntity)
                ? SystemAPI.GetComponent<GhostOwner>(attackerEntity).NetworkId
                : -1;

            float3 attackerPosition = localTransformRO.ValueRO.Position;
            float detectionRadius   = proximityRO.ValueRO.detectRadius;
            float attackRange       = proximityRO.ValueRO.attackRange;
            float detectionRadiusSq = detectionRadius * detectionRadius;
            float attackRangeSq     = attackRange * attackRange;

            // Current target (will be updated/acquired below)
            var attackTarget = SystemAPI.GetComponent<AttackTarget>(attackerEntity);
            Entity currentTargetEntity = attackTarget.value;

            // Validate target within detection; acquire if needed
            bool targetIsValidWithinDetection = false;
            if (currentTargetEntity != Entity.Null && SystemAPI.Exists(currentTargetEntity))
            {
                int currentIndex = FindIndexOfEntity(targetEntitiesList, currentTargetEntity);
                if (currentIndex >= 0
                    && targetAliveFlagsList[currentIndex]
                    && targetOwnerIdsList[currentIndex] != attackerOwnerId)
                {
                    float distanceSquared = math.lengthsq(targetPositionsList[currentIndex] - attackerPosition);
                    if (distanceSquared <= detectionRadiusSq)
                        targetIsValidWithinDetection = true;
                }
            }

            if (!targetIsValidWithinDetection)
            {
                Entity bestCandidateEntity = Entity.Null;
                float bestCandidateDistanceSquared = float.PositiveInfinity;

                for (int i = 0; i < targetEntitiesList.Length; i++)
                {
                    if (!targetAliveFlagsList[i]) continue;
                    if (targetEntitiesList[i] == attackerEntity) continue;
                    if (targetOwnerIdsList[i] == attackerOwnerId) continue;

                    float distanceSquared = math.lengthsq(targetPositionsList[i] - attackerPosition);
                    if (distanceSquared <= detectionRadiusSq && distanceSquared < bestCandidateDistanceSquared)
                    {
                        bestCandidateDistanceSquared = distanceSquared;
                        bestCandidateEntity = targetEntitiesList[i];
                    }
                }

                attackTarget.value = bestCandidateEntity;
                SystemAPI.SetComponent(attackerEntity, attackTarget);
                currentTargetEntity = bestCandidateEntity;
            }

            // No target → tick cooldown and wind-up (if any) and continue
            if (currentTargetEntity == Entity.Null)
            {
                cooldownRW.ValueRW.timeLeft = math.max(0f, cooldownRW.ValueRO.timeLeft - deltaTimeSeconds);

                if (windupRW.ValueRO.timeLeftSeconds > 0f)
                {
                    var w = windupRW.ValueRO;
                    w.timeLeftSeconds -= deltaTimeSeconds;
                    if (w.timeLeftSeconds <= 0f) { w.timeLeftSeconds = 0f; w.targetSnapshot = Entity.Null; }
                    windupRW.ValueRW = w;
                }

                // ⬇️ add this: stop AI-chase if we had been chasing
                if (SystemAPI.HasComponent<AutoChaseState>(attackerEntity))
                {
                    var ac = SystemAPI.GetComponent<AutoChaseState>(attackerEntity);
                    if (ac.isChasing)
                    {
                        ac.isChasing = false;
                        SystemAPI.SetComponent(attackerEntity, ac);
                    }
                }

                continue;
            }

            // Distance + hysteresis
            int targetIndex = FindIndexOfEntity(targetEntitiesList, currentTargetEntity);
            if (targetIndex < 0
                || !targetAliveFlagsList[targetIndex]
                || targetOwnerIdsList[targetIndex] == attackerOwnerId)
            {
                // lost or invalid → clear
                SystemAPI.SetComponent(attackerEntity, new AttackTarget { value = Entity.Null });

                // ⬇️ add this: stop AI-chase since our target is gone
                if (SystemAPI.HasComponent<AutoChaseState>(attackerEntity))
                {
                    var ac = SystemAPI.GetComponent<AutoChaseState>(attackerEntity);
                    if (ac.isChasing)
                    {
                        ac.isChasing = false;
                        SystemAPI.SetComponent(attackerEntity, ac);
                    }
                }

                cooldownRW.ValueRW.timeLeft = math.max(0f, cooldownRW.ValueRO.timeLeft - deltaTimeSeconds);

                // tick/clear wind-up if active
                if (windupRW.ValueRO.timeLeftSeconds > 0f)
                {
                    var w = windupRW.ValueRO;
                    w.timeLeftSeconds -= deltaTimeSeconds;
                    if (w.timeLeftSeconds <= 0f) { w.timeLeftSeconds = 0f; w.targetSnapshot = Entity.Null; }
                    windupRW.ValueRW = w;
                }
                continue;
            }

            float distanceSquaredToTarget = math.lengthsq(targetPositionsList[targetIndex] - attackerPosition);

            const float chaseHysteresis = 0.25f;
            float chaseRadius = attackRange + chaseHysteresis;
            float chaseRadiusSquared = chaseRadius * chaseRadius;

            // Respect manual orders vs AI-chase
            bool hasUnitMover = SystemAPI.HasComponent<UnitMover>(attackerEntity);
            bool isCurrentlyAutoChasing = SystemAPI.HasComponent<AutoChaseState>(attackerEntity)
                ? SystemAPI.GetComponent<AutoChaseState>(attackerEntity).isChasing
                : false;

            UnitMover mover = default;
            bool isManualMove = false;
            if (hasUnitMover)
            {
                mover = SystemAPI.GetComponent<UnitMover>(attackerEntity);
                isManualMove = mover.activeTarget && !isCurrentlyAutoChasing;
            }

            // ---- tick wind-up timer (and apply delayed hit when it expires) ----
            if (windupRW.ValueRO.timeLeftSeconds > 0f)
            {
                var w = windupRW.ValueRO;
                w.timeLeftSeconds -= deltaTimeSeconds;

                if (w.timeLeftSeconds <= 0f)
                {
                    // Try to apply the hit to the snapshot target if still valid and near enough
                    Entity plannedTarget = w.targetSnapshot;
                    int snapIndex = FindIndexOfEntity(targetEntitiesList, plannedTarget);
                    bool canAttemptHit = snapIndex >= 0
                                         && targetAliveFlagsList[snapIndex]
                                         && targetOwnerIdsList[snapIndex] != attackerOwnerId;

                    if (canAttemptHit)
                    {
                        float distSqNow = math.lengthsq(targetPositionsList[snapIndex] - attackerPosition);
                        bool stillInReasonableRange = distSqNow <= chaseRadiusSquared; // tolerate slight drift
                        if (stillInReasonableRange && SystemAPI.HasComponent<HealthState>(plannedTarget))
                        {
                            var localRandom = _rng;
                            bool didHit = localRandom.NextFloat() <= math.saturate(attackStatsRO.ValueRO.hitchance);
                            _rng = localRandom;

                            if (didHit)
                            {
                                var targetHealth = SystemAPI.GetComponent<HealthState>(plannedTarget);
                                if (targetHealth.currentStage != HealthStage.Dead)
                                {
                                    targetHealth.healthChange -= 1;
                                    SystemAPI.SetComponent(plannedTarget, targetHealth);
                                }
                            }
                        }
                    }

                    // Clear wind-up
                    w.timeLeftSeconds = 0f;
                    w.targetSnapshot = Entity.Null;
                }

                windupRW.ValueRW = w;
            }

            // ---- CHASE if outside radius (respect manual move) ----
            if (distanceSquaredToTarget > chaseRadiusSquared)
            {
                if (hasUnitMover && (!isManualMove || isCurrentlyAutoChasing))
                {
                    const float stopBuffer = 0.50f; // ↑ from 0.35f; stops earlier so they don't pass through
                    float desiredStopRange = math.max(attackRange - stopBuffer, 0.3f);

                    float3 vectorToTarget     = targetPositionsList[targetIndex] - attackerPosition;
                    float3 directionToTarget  = math.normalizesafe(vectorToTarget, new float3(0, 0, 1));
                    float3 desiredChasePoint  = targetPositionsList[targetIndex] - directionToTarget * desiredStopRange;

                    mover.targetPosition = desiredChasePoint;
                    mover.targetRotation = math.atan2(directionToTarget.x, directionToTarget.z);
                    mover.activeTarget   = true;
                    mover.isRunning      = true;
                    SystemAPI.SetComponent(attackerEntity, mover);

                    if (SystemAPI.HasComponent<AutoChaseState>(attackerEntity))
                    {
                        var ac = SystemAPI.GetComponent<AutoChaseState>(attackerEntity);
                        ac.isChasing = true;
                        SystemAPI.SetComponent(attackerEntity, ac);
                    }
                }

                cooldownRW.ValueRW.timeLeft = math.max(0f, cooldownRW.ValueRO.timeLeft - deltaTimeSeconds);
                continue;
            }

            if (hasUnitMover && isCurrentlyAutoChasing)
            {
                mover.activeTarget = false; // stop AI movement at range
                SystemAPI.SetComponent(attackerEntity, mover);

                var ac = SystemAPI.GetComponent<AutoChaseState>(attackerEntity);
                ac.isChasing = false;
                SystemAPI.SetComponent(attackerEntity, ac);
            }

            // Always face the current target while in range (even if move was manual)
            if (hasUnitMover)
            {
                float3 vNow    = targetPositionsList[targetIndex] - attackerPosition;
                float3 dirNow  = math.normalizesafe(vNow, new float3(0, 0, 1));
                var moverNow   = SystemAPI.GetComponent<UnitMover>(attackerEntity);
                moverNow.targetRotation = math.atan2(dirNow.x, dirNow.z);
                SystemAPI.SetComponent(attackerEntity, moverNow);
            }

            // Cooldown gate (always tick here)
            float remainingCooldown = cooldownRW.ValueRO.timeLeft - deltaTimeSeconds;
            if (remainingCooldown > 0f)
            {
                cooldownRW.ValueRW.timeLeft = remainingCooldown;
                continue;
            }

            // Don't start a new swing while a previous wind-up is still active
            if (windupRW.ValueRO.timeLeftSeconds > 0f)
            {
                // cooldown already at/near zero; keep it there until we can swing again
                cooldownRW.ValueRW.timeLeft = 0f;
                continue;
            }

            // ---- START a new swing: trigger animation now, delay the hit ----
            {
                // Bump the replicated animation tick so clients play Punch immediately
                var animState = SystemAPI.GetComponent<AttackAnimationState>(attackerEntity);
                animState.attackTick++;
                SystemAPI.SetComponent(attackerEntity, animState);

                // Arm the wind-up to apply damage later to the current target
                var w = windupRW.ValueRO;
                w.timeLeftSeconds = math.max(0f, attackStatsRO.ValueRO.hitDelaySeconds);
                w.targetSnapshot  = currentTargetEntity;
                windupRW.ValueRW  = w;

                // Reset the cooldown from swing start (classic ARPG/RTS timing)
                float attackPeriodSeconds = math.max(0.01f, 1f / attackStatsRO.ValueRO.attacksPerSecond);
                cooldownRW.ValueRW.timeLeft = attackPeriodSeconds;
            }
        }

        targetEntitiesList.Dispose();
        targetPositionsList.Dispose();
        targetOwnerIdsList.Dispose();
        targetAliveFlagsList.Dispose();
    }

    private static int FindIndexOfEntity(NativeList<Entity> list, Entity entity)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] == entity) return i;
        return -1;
    }
}