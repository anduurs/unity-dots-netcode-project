using Unity.Entities;
using Unity.Entities.Serialization;

namespace GameCode.Shared.Components
{
    public struct PrefabReferenceComponent : IComponentData
    {
        public EntityPrefabReference PrefabReference;
    }
}