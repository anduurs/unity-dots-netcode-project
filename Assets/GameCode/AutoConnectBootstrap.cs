using Unity.NetCode;
using UnityEngine.Scripting;

[Preserve]
public sealed class AutoConnectBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        AutoConnectPort = 0;
        return false;
    }
}
