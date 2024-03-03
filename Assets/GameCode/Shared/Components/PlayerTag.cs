using Unity.Entities;

namespace GameCode.Shared.Components
{
    public struct PlayerTag : IComponentData
    {
    }

    public struct LocalPlayer : IComponentData
    {
        public int NetId;
        public Entity PlayerEntity;
    }
}