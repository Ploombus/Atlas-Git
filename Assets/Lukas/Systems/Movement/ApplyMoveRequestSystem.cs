using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct ApplyMoveRequestsServerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        foreach (var (reqRW, targetsRW) in
                 SystemAPI.Query<RefRW<UnitTargetsNetcode>, RefRW<UnitTargets>>())
        {
            var req = reqRW.ValueRO;
            ref var targets = ref targetsRW.ValueRW;

            // Only apply if this is a new order
            if (req.requestLastAppliedSequence == 0 ||
                req.requestLastAppliedSequence == targets.lastAppliedSequence)
                continue;

            // Bump sequence and mark as having an active order
            targets.lastAppliedSequence = req.requestLastAppliedSequence;
            targets.activeTargetSet     = true; // make authoritative here
            targets.hasArrived          = false; // clear sticky-arrival on new order

            // Follow vs Move
            if (req.requestTargetEntity != Entity.Null &&
                em.Exists(req.requestTargetEntity)) // defensive: target may be gone
            {
                targets.targetEntity = req.requestTargetEntity;
                // Keep last destination values around (useful for “return to” logic later)
            }
            else
            {
                targets.targetEntity        = Entity.Null;
                targets.destinationPosition = req.requestDestinationPosition;
                targets.destinationRotation = req.requestDestinationRotation;
            }
        }
    }
}

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct ApplyMoveRequestsClientPredictSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (reqRW, targetsRW) in
                 SystemAPI.Query<RefRW<UnitTargetsNetcode>, RefRW<UnitTargets>>()
                          .WithAll<GhostOwnerIsLocal, PredictedGhost>())
        {
            var req = reqRW.ValueRO;
            ref var targets = ref targetsRW.ValueRW;

            if (req.requestLastAppliedSequence == 0 ||
                req.requestLastAppliedSequence == targets.lastAppliedSequence)
                continue;

            targets.lastAppliedSequence = req.requestLastAppliedSequence;
            targets.activeTargetSet     = true;   // mirror server intent
            targets.hasArrived          = false;  // clear sticky-arrival locally too

            if (req.requestTargetEntity != Entity.Null)
            {
                targets.targetEntity = req.requestTargetEntity;
            }
            else
            {
                targets.targetEntity        = Entity.Null;
                targets.destinationPosition = req.requestDestinationPosition;
                targets.destinationRotation = req.requestDestinationRotation;
            }
        }
    }
}