using GameCode.Shared.RpcCommands;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace GameCode.Server.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct GoInGameServerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp);
            builder.WithAll<ReceiveRpcCommandRequest, GoInGameRpcCommand>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (request, command, entity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<GoInGameRpcCommand>>()
                         .WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(request.ValueRO.SourceConnection);
                ecb.DestroyEntity(entity);
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}