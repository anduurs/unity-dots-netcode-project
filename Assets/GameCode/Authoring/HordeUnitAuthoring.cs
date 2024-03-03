using GameCode.Shared.Components;
using Unity.Entities;
using UnityEngine;

namespace GameCode.Authoring
{
    public class HordeUnitAuthoring : MonoBehaviour
    {
        class HordeUnitBaker : Baker<HordeUnitAuthoring>
        {
            public override void Bake(HordeUnitAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<HordeUnitTag>(entity);
                AddComponent<Position2dComponent>(entity);
                AddComponent<VelocityComponent>(entity);
                AddComponent<RadiusComponent>(entity);
            }
        }
    }
}


