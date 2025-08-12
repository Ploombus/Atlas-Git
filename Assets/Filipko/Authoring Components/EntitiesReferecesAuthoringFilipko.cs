using UnityEngine;
using Unity.Entities;


public class EntitiesReferencesAuthoringFilipko : MonoBehaviour
{
    public GameObject playerPrefabGameObject;
   
    public class Baker : Baker<EntitiesReferencesAuthoringFilipko>
        {
            public override void Bake(EntitiesReferencesAuthoringFilipko authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new EntitiesReferencesFilipko
                {
                    playerPrefabEntity = GetEntity(authoring.playerPrefabGameObject, TransformUsageFlags.Dynamic),
                });
            }
        }
}
public struct EntitiesReferencesFilipko : IComponentData
{
    public Entity playerPrefabEntity;
}