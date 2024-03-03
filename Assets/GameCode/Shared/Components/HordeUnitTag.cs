using Unity.Entities;
using Unity.Mathematics;

namespace GameCode.Shared.Components
{
    public struct HordeUnitTag : IComponentData
    {
        
    }

    public struct Position2dComponent : IComponentData
    {
        public float2 Position;
    }

    public struct VelocityComponent : IComponentData
    {
        public float2 Velocity;
    }

    public struct RadiusComponent : IComponentData
    {
        public float Radius;
    }
}