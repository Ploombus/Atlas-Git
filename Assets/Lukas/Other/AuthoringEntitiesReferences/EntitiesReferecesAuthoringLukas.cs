using UnityEngine;
using Unity.Entities;


public class EntitiesReferencesAuthoringLukas : MonoBehaviour
{
    public GameObject unitPrefabGameObject;
    public GameObject dotPrefabGameObject;
    public GameObject targetArrowPrefabGameObject;
   
    public class Baker : Baker<EntitiesReferencesAuthoringLukas>
    {
        public override void Bake(EntitiesReferencesAuthoringLukas authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EntitiesReferencesLukas
            {
                unitPrefabEntity = GetEntity(authoring.unitPrefabGameObject, TransformUsageFlags.Dynamic),
                dotPrefabEntity = GetEntity(authoring.dotPrefabGameObject, TransformUsageFlags.Renderable),
                targetArrowPrefabEntity = GetEntity(authoring.targetArrowPrefabGameObject, TransformUsageFlags.Renderable),
            });
        }
    }
   
}
public struct EntitiesReferencesLukas : IComponentData
{
    public Entity unitPrefabEntity;
    public Entity dotPrefabEntity;
    public Entity targetArrowPrefabEntity;
}