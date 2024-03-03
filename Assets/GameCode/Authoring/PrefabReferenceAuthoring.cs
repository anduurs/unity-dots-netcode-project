using GameCode.Shared.Components;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

namespace GameCode.Authoring
{
#if UNITY_EDITOR
    public class PrefabReferenceAuthoring : MonoBehaviour
    {
        public GameObject Prefab = null;

        class PrefabReferenceBaker : Baker<PrefabReferenceAuthoring>
        {
            public override void Bake(PrefabReferenceAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PrefabReferenceComponent
                {
                    PrefabReference = new EntityPrefabReference(authoring.Prefab)
                });
            }
        }
    }
#endif
}