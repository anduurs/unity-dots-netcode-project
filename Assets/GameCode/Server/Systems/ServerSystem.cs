using GameCode.Shared.Components;
using GameCode.Shared.RpcCommands;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;

namespace GameCode.Server.Systems
{
    public struct InitializedClient : IComponentData
    {
    }
    
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ServerSystem : SystemBase
    {
        private ComponentLookup<NetworkId> _connectedClients;
        
        // TODO make a prefab handling system
        private Entity _playerPrefab = Entity.Null;
        private Entity _unitPrefab = Entity.Null;
        
        protected override void OnCreate()
        {
            _connectedClients = GetComponentLookup<NetworkId>(true);
        }

        protected override void OnUpdate()
        {
            var prefabEcb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (prefabLoadResult, entity) in SystemAPI.Query<RefRO<PrefabLoadResult>>()
                         .WithAll<PrefabReferenceComponent>()
                         .WithEntityAccess())
            {
                var entityPrefab = prefabLoadResult.ValueRO.PrefabRoot;
                if (EntityManager.HasComponent<PlayerTag>(entityPrefab))
                {
                    _playerPrefab = entityPrefab;
                }
                else if (EntityManager.HasComponent<HordeUnitTag>(entityPrefab))
                {
                    _unitPrefab = entityPrefab;
                }
                
                prefabEcb.RemoveComponent<PrefabReferenceComponent>(entity);
            }
            
            prefabEcb.Playback(EntityManager);
            prefabEcb.Dispose();
            
            if (_playerPrefab == Entity.Null || _unitPrefab == Entity.Null) return;
            
            _connectedClients.Update(this);
            
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            
            foreach (var (id, entity) in SystemAPI.Query<RefRO<NetworkId>>()
                         .WithNone<InitializedClient>()
                         .WithEntityAccess())
            {
                ecb.AddComponent<InitializedClient>(entity);
                Debug.Log("[Server]: Client with connected with id: " + id.ValueRO.Value);

                var player = ecb.Instantiate(_playerPrefab);
                ecb.SetComponent(player, new LocalTransform
                {
                    Position = new float3(UnityEngine.Random.Range(-10.0f, 10.0f), 0, UnityEngine.Random.Range(-10.0f, 10.0f)),
                    Rotation = quaternion.identity,
                    Scale = 1f
                });
                
                ecb.SetComponent(player, new GhostOwner
                {
                    NetworkId = id.ValueRO.Value
                });
                
                // link the spawned player to the client connection entity. This will ensure that the spawned player entity gets destroyed when client disconnects.
                ecb.AppendToBuffer(entity, new LinkedEntityGroup
                {
                    Value = player
                });
                
                Debug.Log("[Server]: Spawned player");
                
                
                SendMessageRpc("Client with connected with id: " + id.ValueRO.Value, ConnectionManager.ServerWorld);
            }

            foreach (var (request, command, entity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ClientMessageRpcCommand>>()
                         .WithEntityAccess())
            {
                Debug.Log("[Server]: " + command.ValueRO.Message + " from client index " + request.ValueRO.SourceConnection.Index + " version " + request.ValueRO.SourceConnection.Version);
                ecb.DestroyEntity(entity);
            }
            
            foreach (var (request, command, entity) in SystemAPI
                         .Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SpawnUnitRpcCommand>>()
                         .WithEntityAccess())
            {
                var unit = ecb.Instantiate(_unitPrefab);
                ecb.SetComponent(unit, new LocalTransform
                {
                    Position = new float3(UnityEngine.Random.Range(-10.0f, 10.0f), 0, UnityEngine.Random.Range(-10.0f, 10.0f)),
                    Rotation = quaternion.identity,
                    Scale = 1f
                });
                
                ecb.SetComponent(unit, new Position2dComponent
                {
                    Position = new float2(UnityEngine.Random.Range(-10.0f, 10.0f), UnityEngine.Random.Range(-10.0f, 10.0f))
                });
                
                ecb.SetComponent(unit, new RadiusComponent
                {
                    Radius = 0.3f
                });
                
                Debug.Log("[Server]: Spawned unit ");
                
                ecb.DestroyEntity(entity);
            }
            
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        private static void SendMessageRpc(string text, World world, Entity target = default)
        {
            if (world is null || !world.IsCreated) 
                return;

            var entity =
                world.EntityManager.CreateEntity(typeof(SendRpcCommandRequest), typeof(ServerMessageRpcCommand));
            
            world.EntityManager.SetComponentData(entity, new ServerMessageRpcCommand
            {
                Message = text
            });

            if (target != Entity.Null)
            {
                world.EntityManager.SetComponentData(entity, new SendRpcCommandRequest
                {
                    TargetConnection = target
                });
            }
        }
    }
}


