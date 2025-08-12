using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using Managers;

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsSystem))]
partial struct MovementSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        //System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if (isInGame == false) return;

        foreach ((
        RefRW<LocalTransform> localTransform,
        RefRW<UnitMover> unitMover,
        RefRW<UnitStats> unitStats,
        RefRW<PhysicsVelocity> physicsVelocity,
        RefRO<UnitModifiers> unitModifiers)
        in SystemAPI.Query<
            RefRW<LocalTransform>,
            RefRW<UnitMover>,
            RefRW<UnitStats>,
            RefRW<PhysicsVelocity>,
            RefRO<UnitModifiers>>().WithAll<Simulate>())
        {
            // constants (tweak as needed)
            const float minDistance = 0.26f;
            const float runSpeedMod = 1.5f;
            const float idleSpeedMod = 0.5f;
            const float minLength = 1e-6f;

            float3 toTarget = unitMover.ValueRO.targetPosition - localTransform.ValueRO.Position;
            float distSq = math.lengthsq(toTarget);

            // Dead/frozen → no motion or rotation
            float speedMultiplier = unitModifiers.ValueRO.moveSpeedMultiplier;
            if (speedMultiplier <= 0f)
            {
                physicsVelocity.ValueRW.Linear = float3.zero;
                physicsVelocity.ValueRW.Angular = float3.zero;
                continue;
            }

            // Position-only completion
            bool isClose = distSq < minDistance * minDistance;
            if (isClose)
            {
                unitMover.ValueRW.activeTarget = false;

                physicsVelocity.ValueRW.Linear = float3.zero;
                physicsVelocity.ValueRW.Angular = float3.zero;

                quaternion targetRot = quaternion.RotateY(unitMover.ValueRO.targetRotation);
                localTransform.ValueRW.Rotation = math.slerp(
                    localTransform.ValueRO.Rotation,
                    targetRot,
                    SystemAPI.Time.DeltaTime * unitStats.ValueRO.rotationSpeed
                );
                continue;
            }

            // Movement + facing
            float length = math.sqrt(distSq);
            float3 moveDirection = length > minLength ? toTarget / length : float3.zero;

            localTransform.ValueRW.Rotation = math.slerp(
                localTransform.ValueRO.Rotation,
                quaternion.LookRotation(moveDirection, math.up()),
                SystemAPI.Time.DeltaTime * unitStats.ValueRO.rotationSpeed
            );

            float baseSpeed = unitStats.ValueRO.moveSpeed;
            float behaviorMul = unitMover.ValueRO.activeTarget
                ? (unitMover.ValueRO.isRunning ? runSpeedMod : 1f)
                : idleSpeedMod;

            float finalSpeed = baseSpeed * behaviorMul * speedMultiplier;
            physicsVelocity.ValueRW.Linear = moveDirection * finalSpeed;
            physicsVelocity.ValueRW.Angular = float3.zero;
        }

    }      
}
