using Unity.Collections;
using Unity.NetCode;

namespace GameCode.Shared.RpcCommands
{
    public struct ServerMessageRpcCommand : IRpcCommand
    {
        public FixedString64Bytes Message;
    }
}