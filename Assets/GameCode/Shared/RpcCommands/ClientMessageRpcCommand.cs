using Unity.Collections;
using Unity.NetCode;

namespace GameCode.Shared.RpcCommands
{
    public struct ClientMessageRpcCommand : IRpcCommand
    {
        public FixedString64Bytes Message;
    }
}