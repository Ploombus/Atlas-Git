using Unity.Entities;
using Unity.NetCode;

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateBefore(typeof(MovementSystem))]
partial struct ApplyMoveRequestsSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (request, mover) in
            SystemAPI.Query<RefRW<UnitTargetNetcode>, RefRW<UnitMover>>())
        {
            if (!request.ValueRO.requestActiveTarget)
                continue;

            // IMPORTANT: only apply if this is newer than what we already have
            if (request.ValueRO.requestSequence == mover.ValueRO.lastAppliedSequence)
                continue;

            mover.ValueRW.targetPosition = request.ValueRO.requestTargetPosition;
            mover.ValueRW.targetRotation = request.ValueRO.requestTargetRotation;
            mover.ValueRW.activeTarget = true;
            mover.ValueRW.isRunning = request.ValueRO.requestIsRunning;
            mover.ValueRW.lastAppliedSequence = request.ValueRO.requestSequence;
        }
    }
}

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct ClearRequestOnAckSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (request, mover) in
            SystemAPI.Query<RefRW<UnitTargetNetcode>, RefRO<UnitMover>>())
        {
            if (!request.ValueRO.requestActiveTarget)
                continue;

            if (mover.ValueRO.lastAppliedSequence == request.ValueRO.requestSequence)
            {
                request.ValueRW.requestActiveTarget = false;
                request.ValueRW.requestIsRunning    = false;
            }
        }
    }
}