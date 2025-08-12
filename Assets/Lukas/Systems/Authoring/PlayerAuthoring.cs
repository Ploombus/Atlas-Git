using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.NetCode;

public class PlayerAuthoring : MonoBehaviour
{
    public Color color = Color.white;

    public class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player
            {
                PlayerColor = new float4(authoring.color.r, authoring.color.g, authoring.color.b, authoring.color.a)
            });
        }
    }
}

public struct Player : IComponentData
{
    [GhostField] public float4 PlayerColor;
}