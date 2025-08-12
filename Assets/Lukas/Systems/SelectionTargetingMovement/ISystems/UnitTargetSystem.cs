using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using Managers;
using Unity.Collections;
using Unity.Transforms;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
partial struct UnitTargetSystem : ISystem
{
    float lastClickTime;
    float angleOffset;
    float doubleClickThreshold;
    int arrayLength;
    float3 cursorPosition;
    float3 targetPosition;
    


    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<UnitTargetNetcode>();

        doubleClickThreshold = 0.3f;
    }

    public void OnUpdate(ref SystemState state)
    {
        //System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if (isInGame == false) return;

        //Formation
        bool locked = FormationUIState.IsLocked;
        Formations selectedFormation = FormationUIState.SelectedFormation;

        //Getting target and number of units
        if (Input.GetMouseButtonDown(1))
        {
            arrayLength = 0;
            foreach (var unitSelected in SystemAPI.Query<RefRO<Unit>>().WithAll<GhostOwnerIsLocal, Selected>()) { arrayLength++; }
            if (arrayLength == 0) return;
            cursorPosition = MouseWorldPosition.Instance.GetPosition();
            targetPosition = cursorPosition;
        }

        //Check if any unit is selected
        if (arrayLength == 0) return;


        if (Input.GetMouseButton(1))
        {
            cursorPosition = MouseWorldPosition.Instance.GetPosition();

            //Set angle offset
            float3 offsetPosition = MouseWorldPosition.Instance.GetPosition();
            float3 dragVector = offsetPosition - targetPosition;
            if (math.lengthsq(dragVector.xz) < 0.5f)
            {
                angleOffset = 0f;
            }
            else
            {
                float3 direction = math.normalize(dragVector);
                angleOffset = math.atan2(direction.z, direction.x);
                if (!math.isfinite(angleOffset)) angleOffset = 0f;
            }

            //Formation mode
            float formationWidth = math.length(cursorPosition - targetPosition);
            if (formationWidth > 2f)
            {
                //Create Positions
                FormationGenerator generateFormation = FormationLibrary.Get(selectedFormation);
                NativeArray<float3> emptyCurrentPositions = new NativeArray<float3>(0, Allocator.Temp);

                NativeArray<float3> targetPositionArray = generateFormation(
                    targetPosition,
                    cursorPosition,
                    emptyCurrentPositions,
                    arrayLength,
                    angleOffset,
                    Allocator.Temp
                );

                var buffer = new EntityCommandBuffer(Allocator.Temp);
                var references = SystemAPI.GetSingleton<EntitiesReferencesLukas>();

                //Remove existing arrows
                foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<TargetArrow>>().WithEntityAccess())
                {
                    buffer.DestroyEntity(entity);
                }

                //Add new arrows
                foreach (var targetPosition in targetPositionArray)
                {
                    var targetEntity = buffer.Instantiate(references.targetArrowPrefabEntity);
                    buffer.SetComponent(targetEntity, LocalTransform.FromPositionRotationScale(targetPosition, quaternion.RotateY(-angleOffset + math.PI), 0.1f));
                }

                buffer.Playback(state.EntityManager);
                buffer.Dispose();
            }
            else
            {
                //Remove existing arrows
                var buffer = new EntityCommandBuffer(Allocator.Temp);
                foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<TargetArrow>>().WithEntityAccess())
                {
                    buffer.DestroyEntity(entity);
                }
                buffer.Playback(state.EntityManager);
                buffer.Dispose();
            }
        }

        //Setting target
        if (Input.GetMouseButtonUp(1))
        {
            //AUTO FORMATION

            float formationWidth = math.length(cursorPosition - targetPosition);
            if (formationWidth < 2f)
            {

                //Get current positions
                NativeList<float3> currentPositions = new NativeList<float3>(Allocator.Temp);
                foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<GhostOwnerIsLocal, Selected>())
                {
                    currentPositions.Add(transform.ValueRO.Position);
                }

                // Compute center
                float3 center = float3.zero;
                for (int i = 0; i < currentPositions.Length; i++)
                    center += currentPositions[i];
                center /= math.max(1, currentPositions.Length);


                if (locked)
                {
                    FormationGenerator generateFormation = FormationLibrary.Get(Formations.Locked);

                    NativeArray<float3> targetPositionArray = generateFormation(
                        targetPosition,
                        cursorPosition,
                        currentPositions.AsArray(),
                        arrayLength,
                        angleOffset,
                        Allocator.Temp
                    );

                    float3 summedForward = float3.zero;
                    int count = 0;

                    foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<GhostOwnerIsLocal, Selected>())
                    {
                        float3 forward = math.forward(transform.ValueRO.Rotation);
                        summedForward += forward;
                        count++;
                    }

                    float3 averageForward = math.normalizesafe(summedForward / math.max(count, 1));
                    angleOffset = -math.atan2(averageForward.x, averageForward.z); // correct facing


                    SetTargets(targetPositionArray, angleOffset, ref state);
                }
                else
                {
                    //Change rotation
                    float3 direction = math.normalize(center - targetPosition);
                    angleOffset = math.atan2(direction.z, direction.x) + (math.PI * 0.5f);

                    NativeArray<float3> targetPositionArray = FormationLibrary.GenerateAutoMimic(
                        FormationUIState.SelectedFormation,
                        targetPosition,
                        //currentPositions.AsArray(),
                        arrayLength,
                        angleOffset,
                        Allocator.Temp
                    );

                    SetTargets(targetPositionArray, angleOffset, ref state);
                }



            }
            //MANUAL FORMATION
            else
            {
                FormationGenerator generateFormation = FormationLibrary.Get(selectedFormation);
                NativeArray<float3> emptyCurrentPositions = new NativeArray<float3>(0, Allocator.Temp);

                NativeArray<float3> targetPositionArray = generateFormation(
                    targetPosition,
                    cursorPosition,
                    emptyCurrentPositions,
                    arrayLength,
                    angleOffset,
                    Allocator.Temp
                );

                var buffer = new EntityCommandBuffer(Allocator.Temp);

                //Remove existing arrows
                foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<TargetArrow>>().WithEntityAccess())
                {
                    buffer.DestroyEntity(entity);
                }

                SetTargets(targetPositionArray, angleOffset, ref state);

                buffer.Playback(state.EntityManager);
                buffer.Dispose();
            }
        }
    }

    public void SetTargets(NativeArray<float3> targetPositionArray, float angleOffset, ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Allocator.Temp);
        var references = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        int unitNumber = 0;

        foreach (var (unitTargetNetcode, dotRef, unitEntity) in
         SystemAPI.Query<RefRW<UnitTargetNetcode>, RefRW<MovementDotRef>>()
                  .WithAll<GhostOwnerIsLocal, Selected>()
                  .WithEntityAccess())
        {
            float3 position = targetPositionArray[unitNumber];

            //Target
            unitTargetNetcode.ValueRW.requestTargetPosition = position;
            unitTargetNetcode.ValueRW.requestTargetRotation = -angleOffset;
            unitTargetNetcode.ValueRW.requestActiveTarget = true;
            unitTargetNetcode.ValueRW.requestIsRunning = (Time.time - lastClickTime) <= doubleClickThreshold;
            
            unitTargetNetcode.ValueRW.requestSequence++;

            //Dot
            if (dotRef.ValueRO.Dot != Entity.Null)
            {
                buffer.DestroyEntity(dotRef.ValueRO.Dot);
            }

            Entity dotEntity = buffer.Instantiate(references.dotPrefabEntity);
            buffer.SetComponent(dotEntity,
                LocalTransform.FromPositionRotationScale(position, quaternion.identity, 0.2f));
            buffer.AddComponent(dotEntity, new MovementDot { owner = unitEntity });
            buffer.SetComponent(unitEntity, new MovementDotRef { Dot = dotEntity }); // IMPORTANT: write the reference via the SAME ECB (not direct)

            unitNumber++;
        }

        lastClickTime = Time.time;

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
        targetPositionArray.Dispose();
    }

}