using GameCode.Shared.RpcCommands;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace GameCode.Client.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial struct GoInGameClientSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp);
            builder.WithAny<NetworkId>();
            builder.WithNone<NetworkStreamInGame>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (id, entity) in SystemAPI.Query<RefRO<NetworkId>>()
                         .WithNone<NetworkStreamInGame>()
                         .WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(entity);
                var request = ecb.CreateEntity();
                ecb.AddComponent<GoInGameRpcCommand>(request);
                ecb.AddComponent<SendRpcCommandRequest>(request);
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}