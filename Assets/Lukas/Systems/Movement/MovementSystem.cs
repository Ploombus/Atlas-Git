using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using Unity.Collections;
using Managers;

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
//[UpdateAfter(typeof(ApplyMoveRequestsClientPredictSystem))]
//[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
partial struct MovementSystem : ISystem
{
    // ========================= KNOBS =========================

    // --- General movement ---
    const float MIN_DISTANCE           = 0.33f; // [m] distance to goal at which we mark "arrived"
    const float STICK_RADIUS_MULT      = 3f;    // sticky zone radius = MIN_DISTANCE * this

    // --- Mass / acceleration model (asymmetric accel/decel) ---
    const float REF_MASS_KG            = 100f;  // [kg] reference mass for scaling accel/decel
    const float ACCEL_AT_REF           = 10f;   // [m/s^2] forward accel at REF_MASS_KG
    const float DECEL_AT_REF           = 10f;   // [m/s^2] braking decel at REF_MASS_KG
    const float MASS_EXPONENT          = 0.8f;  // how strongly mass changes accel/decel (higher = more effect)
    const float MIN_ACCEL              = 2.2f;  // [m/s^2] lower clamp for accel
    const float MAX_ACCEL              = 50f;   // [m/s^2] upper clamp for accel
    const float MIN_DECEL              = 1.5f;  // [m/s^2] lower clamp for decel
    const float MAX_DECEL              = 50f;   // [m/s^2] upper clamp for decel

    // --- Arrival / braking ---
    const float ARRIVE_SAFETY           = 1.1f;  // multiplies stopping distance (extra buffer)
    const float ARRIVE_MIN_DIST         = 1f;    // [m] minimum arrival radius (never smaller)
    const float ARRIVE_MAX_DIST         = 20f;   // [m] bypass arrival slowdown when farther than this
    const float ARRIVE_SPEED_FLOOR_MPS  = 3f;    // [m/s] absolute floor on target speed near goal
    const float ARRIVE_SPEED_INFLUENCE  = 0.5f;  // 0..1 — extra radius scale from current speed fraction
    const float ARRIVE_WEIGHT_INFLUENCE = 0.3f;  // 0..1 — extra radius scale from weight (vs REF_MASS_KG)
    const float ARRIVE_CAP_CURVE        = 0.2f;  // 0=linear, 1=steeper near target (uses p in [1..3])

    // --- Heading slowdown (reduces speed when not facing input) ---
    const float HEADING_CURVE_EXP = 0.4f;       // shape of slowdown vs. angle (1=linear; lower = gentler)
    const float HEADING_THROTTLE_FLOOR = 0.5f;  // minimum forward fraction even at large heading error

    // --- Lateral slip reduction (pulls velocity toward desired heading) ---
    const float LATERAL_KILL_MIN  = 0.5f;  // strength at low speed
    const float LATERAL_KILL_MAX  = 10f;   // strength at high speed
    const float LATERAL_KILL_EXP  = 0.5f;  // curve shape
    const float LATERAL_MASS_EXP  = 0.2f;  // heavier kills less

    // --- Turn braking (slows down during sharp heading changes) ---
    const float TURN_BRAKE_START_DEG = 75f;
    const float TURN_BRAKE_RANGE_DEG = 60f;
    const float TURN_BRAKE_STRENGTH  = 0.5f;
    const float TURN_BRAKE_EXP       = 1.10f;

    // --- Snap / sleep near target ---
    const float SNAP_STOP_DIST_SQ = 0.1f;

    // --- Rotation (visual yaw) ---
    const bool  AUTO_FACE_WHEN_IDLE     = true;  // when not moving, face nearest enemy in attack range
    const float MOVE_FACING_THRESHOLD   = 0.06f; // [m/s] prefer facing movement direction above this speed
    const float MAX_YAW_DEG_PER_SEC     = 180f;  // visual yaw cap
    const float ROT_YAW_WEIGHT_INFLUENCE = 1.20f; // ≥0
    const float ROT_YAW_SPEED_INFLUENCE  = 0.70f; // ≥0
    const float ROT_YAW_TURN_RATE_MULT   = 2.0f;  // visual yaw multiplier

    // --- Steering rotation (velocity alignment) ---
    const float ROT_STEER_SPEED_INFLUENCE  = 1.00f; // ≥0
    const float ROT_STEER_WEIGHT_INFLUENCE = 1.00f; // ≥0
    const float ROT_STEER_TURN_RATE_MULT   = 3.0f;

    // ===================== HELPERS =====================

    // Steering turn-rate (used for velocity-direction blending caps)
    static float ComputeTurnRateRadPerSec(float currentSpeed, float topSpeed, float massKg)
    {
        const float turnRateAtZero = 7f;   // rad/s
        const float turnRateAtRun  = 4f;   // rad/s

        float speedFrac = math.saturate(currentSpeed / math.max(0.1f, topSpeed));
        float t = math.lerp(0f, speedFrac, math.max(0f, ROT_STEER_SPEED_INFLUENCE));

        const float refMass = 80f;
        const float massExp = 0.25f;
        float massScale = math.pow(refMass / math.max(1f, massKg), massExp); // heavier → smaller
        massScale = math.clamp(massScale, 0.8f, 1.25f);
        massScale = math.lerp(1f, massScale, math.max(0f, ROT_STEER_WEIGHT_INFLUENCE));

        float baseRate = math.lerp(turnRateAtZero, turnRateAtRun, t);
        return baseRate * massScale; // rad/s
    }

    // Visual yaw turn-rate (used only when rotating LocalTransform visually)
    static float ComputeYawTurnRateRadPerSec(float currentSpeed, float topSpeed, float massKg)
    {
        const float turnRateAtZero = 7f;  // rad/s
        const float turnRateAtRun  = 4f;  // rad/s

        float speedFrac = math.saturate(currentSpeed / math.max(0.1f, topSpeed));
        float t = math.lerp(0f, speedFrac, math.max(0f, ROT_YAW_SPEED_INFLUENCE));

        const float refMass = 80f;
        const float massExp = 0.25f;
        float massScale = math.pow(refMass / math.max(1f, massKg), massExp);
        massScale = math.clamp(massScale, 0.8f, 1.25f);
        massScale = math.lerp(1f, massScale, math.max(0f, ROT_YAW_WEIGHT_INFLUENCE));

        float baseRate = math.lerp(turnRateAtZero, turnRateAtRun, t);
        return baseRate * massScale; // rad/s
    }

    // --- Rotation helpers ---
    static float GetCurrentYaw(quaternion rot)
    {
        float3 fwd = math.rotate(rot, new float3(0f, 0f, 1f));
        return math.atan2(fwd.x, fwd.z);
    }

    static void RotateYawToward(ref LocalTransform lt, float targetYaw, float maxDeltaYaw)
    {
        float currentYaw = GetCurrentYaw(lt.Rotation);
        float delta = targetYaw - currentYaw;
        delta = math.atan2(math.sin(delta), math.cos(delta)); // wrap to [-pi, pi]
        float applied = math.clamp(delta, -maxDeltaYaw, maxDeltaYaw);
        float newYaw = currentYaw + applied;
        lt.Rotation = quaternion.RotateY(newYaw);
    }

    // Find closest enemy unit within range (broadphase point-distance)
    static bool TryFindAutoFacingTarget(
        in CollisionWorld collisionWorld,
        EntityManager em,
        Entity self,
        float3 selfPos,
        float range,
        int myNetId,
        out float3 targetPos)
    {
        targetPos = default;

        var input = new PointDistanceInput
        {
            Position    = selfPos,
            MaxDistance = range,
            Filter      = new CollisionFilter
            {
                BelongsTo   = ~0u,
                CollidesWith= ~0u,
                GroupIndex  = 0
            }
        };

        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        bool any = collisionWorld.CalculateDistance(input, ref hits);

        float bestDistSq = float.MaxValue;
        Entity best = Entity.Null;

        if (any)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.Entity == self) continue;

                // Must be a Unit and have an owner and a transform
                if (!em.HasComponent<Unit>(h.Entity)) continue;
                if (!em.HasComponent<GhostOwner>(h.Entity)) continue;
                if (!em.HasComponent<LocalTransform>(h.Entity)) continue;

                // Enemy only
                int owner = em.GetComponentData<GhostOwner>(h.Entity).NetworkId;
                if (owner == myNetId) continue;

                // Keep nearest
                float3 p = em.GetComponentData<LocalTransform>(h.Entity).Position;
                float d2 = math.lengthsq(p - selfPos);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = h.Entity;
                    targetPos = p;
                }
            }
        }

        hits.Dispose();
        return best != Entity.Null;
    }

    // =====================================================================

    public void OnUpdate(ref SystemState state)
    {
        // System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if (!isInGame) return;

        float physicsDt = SystemAPI.Time.DeltaTime;
        float deltaTime = SystemAPI.Time.DeltaTime;

        if (SystemAPI.TryGetSingleton<ClientServerTickRate>(out var tickRate) && tickRate.SimulationTickRate > 0)
            physicsDt = 1f / (float)tickRate.SimulationTickRate;

        // Physics broadphase (if present)
        bool haveCollisionWorld = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var pws);
        CollisionWorld collisionWorld = haveCollisionWorld ? pws.CollisionWorld : default;

        // My NetworkId (for enemy filtering)
        int myNetId = -1;
        bool myNetIdValid = SystemAPI.TryGetSingleton<NetworkId>(out var nid);
        if (myNetIdValid) myNetId = nid.Value;

        bool skipDampingComp = SystemAPI.Time.ElapsedTime < 0.05;

        foreach ((
            RefRW<LocalTransform> localTransform,
            RefRW<PhysicsVelocity> physicsVelocity,
            RefRO<UnitStats> unitStats,
            RefRO<UnitModifiers> unitModifiers,
            RefRW<UnitTargets> unitTargets,
            Entity unitEntity
        ) in SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<PhysicsVelocity>,
                RefRO<UnitStats>,
                RefRO<UnitModifiers>,
                RefRW<UnitTargets>
            >().WithAll<Simulate>().WithEntityAccess())
        {
            // ================= RUNTIME FETCH =================

            float minDistance = MIN_DISTANCE;
            float stickRadius = MIN_DISTANCE * STICK_RADIUS_MULT;

            float3 goalPosition = unitTargets.ValueRO.destinationPosition;
            float goalRotation  = unitTargets.ValueRO.destinationRotation;

            float3 toTarget = goalPosition - localTransform.ValueRO.Position;
            float distSq = math.lengthsq(toTarget);

            bool haveFacingFromTarget = math.isfinite(unitTargets.ValueRO.targetRotation);
            bool targetInRangeForFacing = false;

            if (SystemAPI.HasComponent<CombatStats>(unitEntity) && haveFacingFromTarget)
            {
                var combatStats = SystemAPI.GetComponentRO<CombatStats>(unitEntity);
                float effectiveAttackRange = combatStats.ValueRO.attackRange - 0.2f;
                effectiveAttackRange = math.max(0f, effectiveAttackRange);
                float effectiveAttackRangeSq = effectiveAttackRange * effectiveAttackRange;

                float3 toTargetNow = unitTargets.ValueRO.targetPosition - localTransform.ValueRO.Position;
                toTargetNow.y = 0f;
                float d2 = math.lengthsq(toTargetNow);
                if (d2 >= 1e-6f && d2 <= effectiveAttackRangeSq)
                {
                    targetInRangeForFacing = true;
                }
            }

            // Auto-facing when idle: closest enemy within attack range
            bool autoFacingInRange = false;
            float3 autoFacingTargetPos = default;

            if (AUTO_FACE_WHEN_IDLE && haveCollisionWorld && myNetIdValid && SystemAPI.HasComponent<CombatStats>(unitEntity))
            {
                float attackRange = SystemAPI.GetComponentRO<CombatStats>(unitEntity).ValueRO.attackRange;
                autoFacingInRange = TryFindAutoFacingTarget(
                    collisionWorld,
                    state.EntityManager,
                    unitEntity,
                    localTransform.ValueRO.Position,
                    attackRange,
                    myNetId,
                    out autoFacingTargetPos);
            }

            // ================= STICKY ARRIVAL =================
            if (unitTargets.ValueRO.hasArrived)
            {
                if (distSq <= stickRadius * stickRadius && !unitTargets.ValueRO.activeTargetSet)
                {
                    if (math.length(physicsVelocity.ValueRO.Linear) <= MOVE_FACING_THRESHOLD)
                        physicsVelocity.ValueRW.Linear = float3.zero;
                    physicsVelocity.ValueRW.Angular = float3.zero;

                    // --- Apply rotation even while "sticking" ---
                    {
                        float speedNow = math.length(physicsVelocity.ValueRO.Linear);
                        float3 pos = localTransform.ValueRO.Position;
                        float targetYaw = GetCurrentYaw(localTransform.ValueRO.Rotation); // default: keep

                        if (speedNow >= MOVE_FACING_THRESHOLD)
                        {
                            float3 dir = math.normalizesafe(physicsVelocity.ValueRO.Linear, new float3(0f, 0f, 1f));
                            dir.y = 0f;
                            if (math.lengthsq(dir) > 1e-12f) targetYaw = math.atan2(dir.x, dir.z);
                        }
                        else if (targetInRangeForFacing)
                        {
                            float3 toTgt = unitTargets.ValueRO.targetPosition - pos; toTgt.y = 0f;
                            if (math.lengthsq(toTgt) > 1e-12f) targetYaw = math.atan2(toTgt.x, toTgt.z);
                        }
                        else if (autoFacingInRange) // NEW: idle auto-face
                        {
                            float3 toAuto = autoFacingTargetPos - pos; toAuto.y = 0f;
                            if (math.lengthsq(toAuto) > 1e-12f) targetYaw = math.atan2(toAuto.x, toAuto.z);
                        }
                        else if (math.isfinite(goalRotation))
                        {
                            targetYaw = goalRotation;
                        }

                        // Turn-rate model (visual yaw only)
                        float moveSpeedForTurn = unitStats.ValueRO.moveSpeed * unitModifiers.ValueRO.moveSpeedMultiplier;
                        float massKgForTurn = math.max(0.1f, unitStats.ValueRO.weight);
                        if (SystemAPI.HasComponent<PhysicsMass>(unitEntity))
                        {
                            float invMass = SystemAPI.GetComponentRO<PhysicsMass>(unitEntity).ValueRO.InverseMass;
                            if (invMass > 0f) massKgForTurn = 1f / invMass;
                        }
                        float turnRate = ComputeYawTurnRateRadPerSec(speedNow, math.max(0.1f, moveSpeedForTurn), massKgForTurn) * ROT_YAW_TURN_RATE_MULT;
                        float globalCap = math.radians(MAX_YAW_DEG_PER_SEC);
                        if (globalCap > 0f) turnRate = math.min(turnRate, globalCap);
                        float maxDeltaYaw = turnRate * deltaTime;

                        RotateYawToward(ref localTransform.ValueRW, targetYaw, maxDeltaYaw);
                    }

                    continue;
                }
                else
                {
                    unitTargets.ValueRW.hasArrived = false;
                }
            }

            // ================= ARRIVAL TRIGGER =================
            if (distSq <= minDistance * minDistance)
            {
                unitTargets.ValueRW.hasArrived = true;
                unitTargets.ValueRW.activeTargetSet = false;

                if (SystemAPI.HasComponent<UnitTargetsNetcode>(unitEntity))
                {
                    var net = SystemAPI.GetComponentRW<UnitTargetsNetcode>(unitEntity);
                    net.ValueRW.requestActiveTargetSet = false;
                }

                physicsVelocity.ValueRW.Linear = float3.zero;
                physicsVelocity.ValueRW.Angular = float3.zero;

                // --- Apply rotation on arrival (prefer target, then auto, then goal) ---
                {
                    float speedNow = 0f; // we just zeroed linear
                    float3 pos = localTransform.ValueRO.Position;
                    float targetYaw = GetCurrentYaw(localTransform.ValueRO.Rotation); // default: keep

                    if (targetInRangeForFacing)
                    {
                        float3 toTgt = unitTargets.ValueRO.targetPosition - pos; toTgt.y = 0f;
                        if (math.lengthsq(toTgt) > 1e-12f) targetYaw = math.atan2(toTgt.x, toTgt.z);
                    }
                    else if (autoFacingInRange) // NEW
                    {
                        float3 toAuto = autoFacingTargetPos - pos; toAuto.y = 0f;
                        if (math.lengthsq(toAuto) > 1e-12f) targetYaw = math.atan2(toAuto.x, toAuto.z);
                    }
                    else if (math.isfinite(goalRotation))
                    {
                        targetYaw = goalRotation;
                    }

                    float moveSpeedForTurn = unitStats.ValueRO.moveSpeed * unitModifiers.ValueRO.moveSpeedMultiplier;
                    float massKgForTurn = math.max(0.1f, unitStats.ValueRO.weight);
                    if (SystemAPI.HasComponent<PhysicsMass>(unitEntity))
                    {
                        float invMass = SystemAPI.GetComponentRO<PhysicsMass>(unitEntity).ValueRO.InverseMass;
                        if (invMass > 0f) massKgForTurn = 1f / invMass;
                    }
                    float turnRate = ComputeYawTurnRateRadPerSec(speedNow, math.max(0.1f, moveSpeedForTurn), massKgForTurn) * ROT_YAW_TURN_RATE_MULT;
                    float globalCap = math.radians(MAX_YAW_DEG_PER_SEC);
                    if (globalCap > 0f) turnRate = math.min(turnRate, globalCap);
                    float maxDeltaYaw = turnRate * deltaTime;

                    RotateYawToward(ref localTransform.ValueRW, targetYaw, maxDeltaYaw);
                }

                continue;
            }

            // ================= MOVE VECTOR & PRE-FACING =================
            float distanceToGoal = math.sqrt(distSq);
            float3 moveDirection = (distanceToGoal > 0.0001f) ? (toTarget / distanceToGoal) : float3.zero;

            // ================= MOVEMENT CORE =================

            // Scalars
            float speedMultiplier = unitModifiers.ValueRO.moveSpeedMultiplier;
            float moveSpeed = unitStats.ValueRO.moveSpeed;

            // Attack slowdown
            if (SystemAPI.HasComponent<Attacker>(unitEntity) && SystemAPI.HasComponent<CombatStats>(unitEntity))
            {
                var attacker = SystemAPI.GetComponentRO<Attacker>(unitEntity);
                if (attacker.ValueRO.attackDurationTimeLeft > 0f)
                {
                    var combatStats = SystemAPI.GetComponentRO<CombatStats>(unitEntity).ValueRO;
                    float slow = math.saturate(combatStats.attackSlowdown);
                    moveSpeed *= slow;
                }
            }
            moveSpeed *= speedMultiplier;

            // Mass & accel/decel scaling
            float massKg = math.max(0.1f, unitStats.ValueRO.weight);
            if (SystemAPI.HasComponent<PhysicsMass>(unitEntity))
            {
                float invMass = SystemAPI.GetComponentRO<PhysicsMass>(unitEntity).ValueRO.InverseMass;
                if (invMass > 0f) massKg = 1f / invMass;
            }
            float massScale = math.pow(massKg / REF_MASS_KG, MASS_EXPONENT);
            float accelPerSecond = math.clamp(ACCEL_AT_REF / math.max(0.01f, massScale), MIN_ACCEL, MAX_ACCEL);
            float decelPerSecond = math.clamp(DECEL_AT_REF / math.max(0.01f, massScale), MIN_DECEL, MAX_DECEL);

            // Current velocity state
            float3 vNow = physicsVelocity.ValueRO.Linear;
            float  vNowMag = math.length(vNow);
            float3 vNowDir = math.normalizesafe(vNow, float3.zero);

            // Speed-aware turn limit (steering, not visual yaw)
            float maxTurnRate = ComputeTurnRateRadPerSec(vNowMag, math.max(0.1f, moveSpeed), massKg) * ROT_STEER_TURN_RATE_MULT;
            float maxAngle = maxTurnRate * deltaTime;

            // Desired direction & target speed (baseline)
            float3 inputDir   = math.normalizesafe(moveDirection, float3.zero);
            float3 desiredDir = inputDir;
            float  headingAngle = 0f;

            if (math.lengthsq(vNowDir) > 1e-12f && math.lengthsq(inputDir) > 1e-12f)
            {
                float cosAng = math.clamp(math.dot(vNowDir, inputDir), -1f, 1f);
                headingAngle = math.acos(cosAng);

                if (headingAngle > 1e-6f)
                {
                    float t = math.saturate(maxAngle / headingAngle);
                    float3 blended = vNowDir * (1f - t) + inputDir * t;
                    desiredDir = math.normalizesafe(blended, inputDir);
                }
            }
            if (math.lengthsq(vNow) < 1e-6f)
                desiredDir = inputDir;

            float targetSpeed = (math.lengthsq(moveDirection) > 1e-8f) ? moveSpeed : 0f;

            // Heading-error slowdown
            float cosHeading   = (headingAngle > 0f) ? math.cos(headingAngle) : 1f;
            float headingScale = (cosHeading > 0f) ? math.pow(cosHeading, HEADING_CURVE_EXP) : 0f;
            if (headingAngle < math.radians(TURN_BRAKE_START_DEG))
                if (headingScale < HEADING_THROTTLE_FLOOR) headingScale = HEADING_THROTTLE_FLOOR;
            targetSpeed *= headingScale;

            // ---- Arrival slowdown (distance-based cap) ----
            if (distSq <= ARRIVE_MAX_DIST * ARRIVE_MAX_DIST)
            {
                float v     = vNowMag;
                float a     = math.max(0.01f, decelPerSecond);
                float dStop = (v * v) / (2f * a);
                float rPhys = ARRIVE_MIN_DIST + ARRIVE_SAFETY * dStop;

                float speedFrac = (moveSpeed > 1e-4f) ? math.saturate(v / moveSpeed) : 0f;
                float wRel      = (massKg / REF_MASS_KG) - 1f;
                float rScaled   = rPhys
                                * (1f + ARRIVE_SPEED_INFLUENCE  * speedFrac)
                                * (1f + ARRIVE_WEIGHT_INFLUENCE * wRel);

                float arrivalRadius = math.clamp(rScaled, ARRIVE_MIN_DIST, ARRIVE_MAX_DIST);

                float dist = math.sqrt(distSq);
                if (dist <= arrivalRadius)
                {
                    float p   = math.lerp(1f, 3f, math.saturate(ARRIVE_CAP_CURVE));
                    float s   = math.saturate(dist / math.max(0.001f, arrivalRadius));
                    float vCap = moveSpeed * math.pow(s, p);
                    vCap       = math.max(vCap, ARRIVE_SPEED_FLOOR_MPS);
                    targetSpeed = math.min(targetSpeed, vCap);
                }
            }

            // Decompose velocity relative to desiredDir
            float  vParallelMag = 0f;
            float3 vParallel = float3.zero;
            float3 vLateral = vNow;
            if (math.lengthsq(desiredDir) > 1e-12f)
            {
                vParallelMag = math.dot(vNow, desiredDir);
                vParallel    = desiredDir * vParallelMag;
                vLateral     = vNow - vParallel;
            }

            // Read linear damping (Unity Physics)
            float damping = 0f;
            if (SystemAPI.HasComponent<PhysicsDamping>(unitEntity))
                damping = SystemAPI.GetComponentRO<PhysicsDamping>(unitEntity).ValueRO.Linear;

            // Forward accel
            float desiredForwardDelta = targetSpeed - vParallelMag;
            float maxDeltaVForward = ((desiredForwardDelta >= 0f) ? accelPerSecond : decelPerSecond) * deltaTime;
            desiredForwardDelta = math.clamp(desiredForwardDelta, -maxDeltaVForward, maxDeltaVForward);
            float3 deltaVForward = desiredDir * desiredForwardDelta;

            // LATERAL slip reduction (traction-like)
            float3 deltaVLateral = float3.zero;
            float lateralSpeed = math.length(vLateral);
            if (lateralSpeed > 1e-6f)
            {
                float speedFrac = vNowMag / math.max(0.1f, moveSpeed);
                speedFrac = math.clamp(speedFrac, 0f, 1f);
                speedFrac = math.pow(speedFrac, LATERAL_KILL_EXP);
                float speedKillScale = math.lerp(LATERAL_KILL_MIN, LATERAL_KILL_MAX, speedFrac);
                float massFactor = math.pow(REF_MASS_KG / math.max(1f, massKg), LATERAL_MASS_EXP);
                float maxDeltaVLateral = accelPerSecond * speedKillScale * massFactor * deltaTime;

                float killAmount = math.min(lateralSpeed, maxDeltaVLateral);
                float3 lateralDir = vLateral / lateralSpeed;
                deltaVLateral = -lateralDir * killAmount;
            }

            // Turn braking
            if (headingAngle > math.radians(TURN_BRAKE_START_DEG) && math.lengthsq(moveDirection) > 1e-8f)
            {
                float speedNowTB = vNowMag;
                if (speedNowTB > 1e-6f)
                {
                    float h01 = math.saturate((headingAngle - math.radians(TURN_BRAKE_START_DEG)) / math.radians(TURN_BRAKE_RANGE_DEG));
                    h01 = math.pow(h01, TURN_BRAKE_EXP);

                    float brakePerSecond = decelPerSecond * TURN_BRAKE_STRENGTH * h01;
                    float maxBrake = brakePerSecond * deltaTime;
                    float brake = math.min(speedNowTB, maxBrake);

                    float3 dir = math.normalizesafe(vNow, float3.zero);
                    deltaVLateral += dir * -brake;
                }
            }

            // Idle braking
            if (math.lengthsq(moveDirection) <= 1e-8f)
            {
                float speedNowIB = vNowMag;
                if (speedNowIB > 1e-6f)
                {
                    float maxBrake = decelPerSecond * deltaTime;
                    float brake = math.min(speedNowIB, maxBrake);
                    float3 dir = math.normalizesafe(vNow, float3.zero);
                    deltaVLateral += dir * -brake;
                }
            }

            // Combine deltas
            float3 vNew = vNow + deltaVForward + deltaVLateral;

            // Tiny snap when basically at target (kill jitter)
            if (distSq < SNAP_STOP_DIST_SQ)
            {
                vNew = float3.zero;
            }
            else
            {
                // Linear-damping pre-comp using NetCode simulation dt
                if (!skipDampingComp && damping > 1e-6f && math.lengthsq(vNew) > 1e-12f)
                {
                    float k = 1f - damping * physicsDt;
                    if (k > 0.0f)
                        vNew /= k;
                }
            }

            // ================= ROTATION (VISUAL YAW) =================
            {
                float speedNow = math.length(vNew);
                float3 pos = localTransform.ValueRO.Position;
                float targetYaw = GetCurrentYaw(localTransform.ValueRO.Rotation); // default: keep

                if (speedNow >= MOVE_FACING_THRESHOLD)
                {
                    float3 dir = math.normalizesafe(vNew, new float3(0f, 0f, 1f));
                    dir.y = 0f;
                    if (math.lengthsq(dir) > 1e-12f) targetYaw = math.atan2(dir.x, dir.z);
                }
                else if (targetInRangeForFacing)
                {
                    float3 toTgt = unitTargets.ValueRO.targetPosition - pos; toTgt.y = 0f;
                    if (math.lengthsq(toTgt) > 1e-12f) targetYaw = math.atan2(toTgt.x, toTgt.z);
                }
                else if (autoFacingInRange) // NEW: idle auto-face
                {
                    float3 toAuto = autoFacingTargetPos - pos; toAuto.y = 0f;
                    if (math.lengthsq(toAuto) > 1e-12f) targetYaw = math.atan2(toAuto.x, toAuto.z);
                }
                else if (math.isfinite(goalRotation))
                {
                    targetYaw = goalRotation;
                }

                float turnRate = ComputeYawTurnRateRadPerSec(speedNow, math.max(0.1f, moveSpeed), massKg) * ROT_YAW_TURN_RATE_MULT;
                float globalCap = math.radians(MAX_YAW_DEG_PER_SEC);
                if (globalCap > 0f) turnRate = math.min(turnRate, globalCap);
                float maxDeltaYaw = turnRate * deltaTime;

                RotateYawToward(ref localTransform.ValueRW, targetYaw, maxDeltaYaw);
            }

            // ================= WRITE BACK VELOCITY =================
            physicsVelocity.ValueRW.Linear = vNew;
            physicsVelocity.ValueRW.Angular = float3.zero;
        }
    }
}