using System.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Scenes;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public sealed class ConnectionManager : MonoBehaviour
{
    public enum Role
    {
        ServerClient = 0,
        Server = 1,
        Client = 2
    }
    
    [SerializeField] private string _listenIp = "127.0.0.1";
    [SerializeField] private string _connectIp = "127.0.0.1";
    [SerializeField] private ushort _port = 7979;

    private static Role s_Role = Role.ServerClient;

    public static World ServerWorld = null;
    public static World ClientWorld = null;
    
    private void Start()
    {
        if (Application.isEditor)
        {
            s_Role = Role.ServerClient;
        }
        else if (Application.platform is RuntimePlatform.LinuxServer or RuntimePlatform.WindowsServer or RuntimePlatform.OSXServer)
        {
            s_Role = Role.Server;
        }
        else
        {
            s_Role = Role.Client;
        }
        
        StartCoroutine(Connect());
    }

    private IEnumerator Connect()
    {
        if (s_Role is Role.ServerClient or Role.Server)
        {
            ServerWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
        }

        if (s_Role is Role.ServerClient or Role.Client)
        {
            ClientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
        }

        DestroyDefaultGameWorld();

        if (ServerWorld is not null)
        {
            World.DefaultGameObjectInjectionWorld = ServerWorld;
        }
        else if (ClientWorld is not null)
        {
            World.DefaultGameObjectInjectionWorld = ClientWorld;
        }

        var subScenes = FindObjectsByType<SubScene>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (ServerWorld is not null)
        {
            while (!ServerWorld.IsCreated)
                yield return null;
            
            LoadAllSubScenesInWorld(subScenes, ServerWorld);
            
            using var query =
                ServerWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            query.GetSingletonRW<NetworkStreamDriver>().ValueRW
                .Listen(NetworkEndpoint.Parse(_listenIp, _port));
        }

        if (ClientWorld is not null)
        {
            while (!ClientWorld.IsCreated)
                yield return null;
            
            LoadAllSubScenesInWorld(subScenes, ClientWorld);
            
            using var query =
                ClientWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            query.GetSingletonRW<NetworkStreamDriver>().ValueRW
                .Connect(ClientWorld.EntityManager, NetworkEndpoint.Parse(_connectIp, _port));
        }
    }

    private static void LoadAllSubScenesInWorld(SubScene[] subScenes, World world)
    {
        if (subScenes is not null)
        {
            for (var i = 0; i < subScenes.Length; i++)
            {
                var loadParameters = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnStreamIn
                };

                var sceneEntity = SceneSystem.LoadSceneAsync(world.Unmanaged,
                    new Hash128(subScenes[i].SceneGUID.Value), loadParameters);
                    
                while (!SceneSystem.IsSceneLoaded(world.Unmanaged, sceneEntity))
                {
                    world.Update();
                }
            }
        }
    }

    private static void DestroyDefaultGameWorld()
    {
        foreach (var world in World.All)
        {
            if (world.Flags == WorldFlags.Game)
                world.Dispose();
            break;
        }
    }
}
