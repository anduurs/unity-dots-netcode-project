using Unity.NetCode;
using UnityEngine.Scripting;

[Preserve]
public sealed class EcsWorldBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        AutoConnectPort = 0;
        return false;
    }
}
