using System;
using System.Collections.Generic;
using System.Text;

namespace NeoExpress.Abstractions.Models
{
    public static class Utility
    {
        public static ushort GetPortNumber(int index, ushort portNumber) => (ushort)((49000 + (index * 1000)) + portNumber);

        public static Uri GetUri(this ExpressChain chain, int node = 0) 
            => new Uri($"http://localhost:{chain.ConsensusNodes[node].RpcPort}");
    }
}
