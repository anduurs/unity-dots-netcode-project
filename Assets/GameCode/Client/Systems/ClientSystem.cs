using GameCode.Shared.RpcCommands;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameCode.Client.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ClientSystem : SystemBase
    {
        protected override void OnCreate()
        {
            // Only run the OnUpdate function when connected to the server
            RequireForUpdate<NetworkId>();
        }

        protected override void OnUpdate()
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                SendSpawnUnitCommand(ConnectionManager.ClientWorld);
            }
            
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            
            foreach (var (request, command, entity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ServerMessageRpcCommand>>()
                         .WithEntityAccess())
            {
                Debug.Log("[Client]: " + command.ValueRO.Message);
                ecb.DestroyEntity(entity);
            }
            
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        private static void SendMessageRpc(string text, World world)
        {
            if (world is null || !world.IsCreated) return;

            var entity =
                world.EntityManager.CreateEntity(typeof(SendRpcCommandRequest), typeof(ClientMessageRpcCommand));
            
            world.EntityManager.SetComponentData(entity, new ClientMessageRpcCommand
            {
                Message = text
            });
        }

        private static void SendSpawnUnitCommand(World world)
        {
            if (world is null || !world.IsCreated) return;
            
            var entity =
                world.EntityManager.CreateEntity(typeof(SendRpcCommandRequest), typeof(SpawnUnitRpcCommand));
        }
    }
}


