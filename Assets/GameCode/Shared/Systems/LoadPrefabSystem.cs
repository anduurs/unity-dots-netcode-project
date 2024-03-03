using GameCode.Shared.Components;
using Unity.Burst;
using Unity.Entities;
using Unity.Scenes;

namespace GameCode.Shared.Systems
{
    [RequireMatchingQueriesForUpdate]
    public partial struct LoadPrefabSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<PrefabReferenceComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;

            var ecbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

            // Adding the RequestEntityPrefabLoaded component will request the prefab to be loaded.
            // It will load the entity scene file corresponding to the prefab and add a PrefabLoadResult
            // component to the entity. The PrefabLoadResult component contains the entity you can use to
            // instantiate the prefab (see the PrefabReferenceSpawnerSystem system).

            foreach (var (weakPrefabReference, entity) in SystemAPI.Query<RefRO<PrefabReferenceComponent>>()
                         .WithNone<RequestEntityPrefabLoaded>()
                         .WithEntityAccess())
            {
                ecb.AddComponent(entity, new RequestEntityPrefabLoaded
                {
                    Prefab = weakPrefabReference.ValueRO.PrefabReference
                });
            }
        }
    }
}