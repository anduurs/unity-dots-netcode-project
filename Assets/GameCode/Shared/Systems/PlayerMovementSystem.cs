using GameCode.Shared.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace GameCode.Shared.Systems
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial struct PlayerMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp);
            builder.WithAll<PlayerData, PlayerInputData, LocalTransform>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var moveJob = new PlayerMoveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };

            state.Dependency = moveJob.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct PlayerMoveJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(in PlayerData playerData, in PlayerInputData input, ref LocalTransform transform)
        {
            float3 movement = new float3(input.Move.x, 0, input.Move.y) * playerData.Speed * DeltaTime;
            transform.Position = transform.Translate(movement).Position;
        }
    }
}