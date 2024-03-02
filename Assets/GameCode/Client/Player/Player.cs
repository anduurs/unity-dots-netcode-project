using GameCode.Shared.Components;
using Unity.Entities;
using UnityEngine;

namespace GameCode.Client.Player
{
    public class Player : MonoBehaviour
    {
        public float Speed = 5f;

        class PlayerBaker : Baker<Player>
        {
            public override void Bake(Player authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<PlayerTag>(entity);
                AddComponent<PlayerInputData>(entity);
                AddComponent(entity, new PlayerData
                {
                    Speed = authoring.Speed
                });
            }
        }
    }
}

