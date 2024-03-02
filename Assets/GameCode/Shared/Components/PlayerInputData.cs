using Unity.Mathematics;
using Unity.NetCode;

namespace GameCode.Shared.Components
{
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct PlayerInputData : IInputComponentData
    {
        public float2 Move;
    }
}