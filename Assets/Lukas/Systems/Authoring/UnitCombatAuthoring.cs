using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Mathematics;

public class UnitCombatAuthoring : MonoBehaviour
{
    [Header("Detection / Range")]
    public float detectRadius = 6f;
    public float attackRange  = 2f;

    [Header("Attack Stats")]
    public float hitchance = 0.75f;
    public float attacksPerSecond = 1.0f;

    [Header("Attack Timing")]
    public float hitDelaySeconds = 0.33f;

    public class Baker : Baker<UnitCombatAuthoring>
    {
        public override void Bake(UnitCombatAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(e, new ProximitySensor
            {
                detectRadius = math.max(authoring.detectRadius, authoring.attackRange),
                attackRange  = authoring.attackRange
            });
            AddComponent(e, new AttackStats
            {
                hitchance         = math.saturate(authoring.hitchance),
                attacksPerSecond  = math.max(0.01f, authoring.attacksPerSecond),
                hitDelaySeconds   = math.max(0.6f, authoring.hitDelaySeconds)
            });
            AddComponent(e, new AttackTarget { value = Entity.Null });
            AddComponent(e, new AttackCooldown { timeLeft = 0f });
            AddComponent(e, new AttackAnimationState { attackTick = 0 });
            AddComponent(e, new AutoChaseState { isChasing = false });

            // wind-up state (server uses this to delay the hit)
            AddComponent(e, new AttackWindup
            {
                timeLeftSeconds = 0f,
                targetSnapshot  = Entity.Null
            });
        }
    }
}

public struct ProximitySensor : IComponentData
{
    public float detectRadius;
    public float attackRange;
}

public struct AttackStats : IComponentData
{
    public float hitchance;       
    public float attacksPerSecond;
    public float hitDelaySeconds;
}

public struct AttackWindup : IComponentData
{
    public float timeLeftSeconds;  // > 0 while waiting to apply the hit
    public Entity targetSnapshot;  // who we planned to hit when swing began
}

public struct AttackTarget : IComponentData
{
    public Entity value; // Entity.Null = no target
}

public struct AttackCooldown : IComponentData
{
    public float timeLeft;
}
public struct AttackAnimationState : IComponentData
{
    [GhostField] public uint attackTick; // increments every swing (hit or miss)
}
public struct AutoChaseState : IComponentData
{
    public bool isChasing; // true only when AI initiated a chase
}