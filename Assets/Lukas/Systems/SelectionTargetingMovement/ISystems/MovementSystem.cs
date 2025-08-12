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

        //Movement logic
        foreach ((
            RefRW<LocalTransform> localTransform,
            RefRW<UnitMover> unitMover,
            RefRW<PhysicsVelocity> physicsVelocity,
            RefRO<UnitModifiers> unitModifiers)
            in SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<UnitMover>,
                RefRW<PhysicsVelocity>,
                RefRO<UnitModifiers>>().WithAll<Simulate>())
        {
            float3 toTarget = unitMover.ValueRO.targetPosition - localTransform.ValueRO.Position;
            float distSq = math.lengthsq(toTarget); // Preventing div by 0

            // Stage effect
            float speedMultiplier = unitModifiers.ValueRO.moveSpeedMultiplier;
            if (speedMultiplier <= 0f)
            {
                //Dead: don't move or rotate
                physicsVelocity.ValueRW.Linear  = float3.zero;
                physicsVelocity.ValueRW.Angular = float3.zero;
                continue;
            }

            if (unitMover.ValueRO.activeTarget)
            {
                bool isClose = distSq < 0.05f;
                if (isClose)
                {
                    physicsVelocity.ValueRW.Linear = float3.zero;
                    physicsVelocity.ValueRW.Angular = float3.zero;

                    quaternion targetRot = quaternion.RotateY(unitMover.ValueRO.targetRotation);
                    if (IsRotationCloseEnough(localTransform.ValueRO.Rotation, targetRot, 1f))
                    {
                        localTransform.ValueRW.Rotation = targetRot;
                        unitMover.ValueRW.activeTarget = false;
                    }
                    else
                    {
                        localTransform.ValueRW.Rotation = math.slerp(
                            localTransform.ValueRO.Rotation,
                            targetRot,
                            SystemAPI.Time.DeltaTime * unitMover.ValueRO.rotationSpeed
                        );
                    }
                }
                else
                {
                    float3 moveDirection = math.normalize(toTarget);
                    localTransform.ValueRW.Rotation = math.slerp(
                        localTransform.ValueRO.Rotation,
                        quaternion.LookRotation(moveDirection, math.up()),
                        SystemAPI.Time.DeltaTime * unitMover.ValueRO.rotationSpeed);

                    float baseSpeed = unitMover.ValueRO.moveSpeed;
                    float finalSpeed = (unitMover.ValueRO.isRunning ? baseSpeed * 1.5f : baseSpeed) * speedMultiplier; // <-- scaled
                    physicsVelocity.ValueRW.Linear = moveDirection * finalSpeed;
                    physicsVelocity.ValueRW.Angular = float3.zero;
                }
            }
            else
            {
                bool isClose = distSq < 0.5f;
                if (isClose)
                {
                    physicsVelocity.ValueRW.Linear = float3.zero;
                    physicsVelocity.ValueRW.Angular = float3.zero;

                    quaternion targetRot = quaternion.RotateY(unitMover.ValueRO.targetRotation);
                    if (IsRotationCloseEnough(localTransform.ValueRO.Rotation, targetRot, 1f))
                    {
                        localTransform.ValueRW.Rotation = targetRot;
                        unitMover.ValueRW.activeTarget = false;
                    }
                    else
                    {
                        localTransform.ValueRW.Rotation = math.slerp(
                            localTransform.ValueRO.Rotation,
                            targetRot,
                            SystemAPI.Time.DeltaTime * unitMover.ValueRO.rotationSpeed
                        );
                    }
                }
                else
                {
                    float3 moveDirection = math.normalize(toTarget);
                    localTransform.ValueRW.Rotation = math.slerp(
                        localTransform.ValueRO.Rotation,
                        quaternion.LookRotation(moveDirection, math.up()),
                        SystemAPI.Time.DeltaTime * unitMover.ValueRO.rotationSpeed);

                    float finalSpeed = (unitMover.ValueRO.moveSpeed / 2f) * speedMultiplier; // <-- scaled
                    physicsVelocity.ValueRW.Linear = moveDirection * finalSpeed;
                    physicsVelocity.ValueRW.Angular = float3.zero;
                }
            }
        }
    }
    
    bool IsRotationCloseEnough(quaternion current, quaternion target, float thresholdDegrees)
            {
                float dot = math.dot(current.value, target.value);
                dot = math.clamp(dot, -1f, 1f);
                float angleDiff = math.degrees(math.acos(dot));
                return angleDiff < thresholdDegrees;
            }
}
