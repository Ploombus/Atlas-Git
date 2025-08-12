using Unity.Entities;
using UnityEngine;

class BuildingAuthoring : MonoBehaviour
{
    [SerializeField] private int buildTime;
    [SerializeField] private int radius;
    [SerializeField] private bool spawnRequested;


    public class Baker : Baker<BuildingAuthoring>
    {
        public override void Bake(BuildingAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new Building
            {
                buildTime = authoring.buildTime,
                radius = authoring.radius,
                
            });
            AddComponent(entity, new UnitSpawnFromBuilding
            {
                spawnRequested = authoring.spawnRequested,
             

            });
        }

    }

}

public struct Building : IComponentData
{
    public int buildTime;
    public int radius;

}
public struct UnitSpawnFromBuilding : IComponentData
{
    public bool spawnRequested; // We can just toggle this true when UI button is pressed
}

