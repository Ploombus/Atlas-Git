using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using Managers;

partial struct SelectorSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        //System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if(isInGame == false) return;
        
        int unitCount = 0;

        foreach ((
            RefRW<LocalTransform> localTransform,
            RefRO<UnitMover> unitMover,
            RefRW<PhysicsVelocity> physicsVelocity,
            RefRO<Selected> selected)
            in SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRO<UnitMover>,
                RefRW<PhysicsVelocity>,
                RefRO<Selected>>())
        {
            unitCount++;
        }
    }
}