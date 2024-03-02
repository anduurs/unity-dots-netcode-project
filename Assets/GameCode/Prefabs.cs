using Unity.Entities;
using UnityEngine;

public struct PrefabsData : IComponentData
{
    public Entity Player;
    public Entity Unit;
}

public class Prefabs : MonoBehaviour
{
    public GameObject Player = null;
    public GameObject Unit = null;

    class PrefabsBaker : Baker<Prefabs>
    {
        public override void Bake(Prefabs authoring)
        {
            Entity playerPrefab = default;
            Entity unitPrefab = default;
            if (authoring.Player is not null)
            {
                playerPrefab = GetEntity(authoring.Player, TransformUsageFlags.Dynamic);
            }
            else
            {
                Debug.LogError("Player Prefab was null, baking failed.");
            }
            
            if (authoring.Unit is not null)
            {
                unitPrefab = GetEntity(authoring.Unit, TransformUsageFlags.Dynamic);
            }
            else
            {
                Debug.LogError("Unit Prefab was null, baking failed.");
            }
            
            var entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new PrefabsData
            {
                Player = playerPrefab,
                Unit = unitPrefab
            });
        }
    }
}