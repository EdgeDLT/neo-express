using Newtonsoft.Json;

namespace NeoExpress.Abstractions.Models
{
    public class ExpressConsensusNode
    {
        public const int TcpPortSuffix = 333;
        public const int WebSocketPortSuffix = 334;
        public const int RpcPortSuffix = 332;

        [JsonProperty("tcp-port")]
        public ushort TcpPort { get; set; }

        [JsonProperty("ws-port")]
        public ushort WebSocketPort { get; set; }

        [JsonProperty("rpc-port")]
        public ushort RpcPort { get; set; }

        [JsonProperty("wallet")]
        public ExpressWallet Wallet { get; set; } = new ExpressWallet();

        public ExpressConsensusNode()
        {
        }

        public ExpressConsensusNode(ExpressWallet wallet, int index)
        {
            TcpPort = Utility.GetPortNumber(index, TcpPortSuffix);
            WebSocketPort = Utility.GetPortNumber(index, WebSocketPortSuffix);
            RpcPort = Utility.GetPortNumber(index, RpcPortSuffix);
            Wallet = wallet;
        }
    }
}
