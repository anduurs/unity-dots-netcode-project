using Unity.Collections;
using Unity.NetCode;

namespace GameCode.Shared.RpcCommands
{
    public struct SpawnPlayerRpcCommand : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
    }
}

