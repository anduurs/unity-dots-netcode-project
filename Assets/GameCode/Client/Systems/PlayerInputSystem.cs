using GameCode.Shared.Components;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace GameCode.Client.Systems
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class PlayerInputSystem : SystemBase
    {
        private GameInputActions _gameInputActions;
        
        protected override void OnCreate()
        {
            _gameInputActions = new GameInputActions();
            _gameInputActions.Enable();
            RequireForUpdate<PlayerInputData>();
        }

        protected override void OnDestroy()
        {
            _gameInputActions.Disable();
        }

        protected override void OnUpdate()
        {
            var playerMove = _gameInputActions.GameMap.PlayerMovement.ReadValue<Vector2>();
            foreach (var input in SystemAPI.Query<RefRW<PlayerInputData>>().WithAll<GhostOwnerIsLocal>())
            {
                input.ValueRW.Move = playerMove;
            }
        }
    }
}