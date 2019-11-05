using Microsoft.Extensions.Configuration;
using Neo;
using Neo.Wallets;
using Neo3Express.Models;
using Neo3Express.Persistence;
using NeoExpress.Abstractions;
using NeoExpress.Abstractions.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Neo3Express
{
    public class BlockchainOperations : IBlockchainOperations
    {
        public Task<JArray> Claim(ExpressChain chain, string asset, ExpressWalletAccount address)
        {
            throw new NotImplementedException();
        }

        public ExpressChain CreateBlockchain(int count)
        {
            var wallets = new List<(DevWallet wallet, Neo.Wallets.WalletAccount account)>(count);

            for (var i = 1; i <= count; i++)
            {
                var wallet = new DevWallet($"node{i}");
                var account = wallet.CreateAccount();
                account.IsDefault = true;
                wallets.Add((wallet, account));
            }

            var keys = wallets.Select(t => t.account.GetKey().PublicKey).ToArray();

            var contract = Neo.SmartContract.Contract.CreateMultiSigContract((keys.Length * 2 / 3) + 1, keys);

            foreach (var (wallet, account) in wallets)
            {
                var multiSigContractAccount = wallet.CreateAccount(contract, account.GetKey());
                multiSigContractAccount.Label = "MultiSigContract";
            }

            var nodes = new List<ExpressConsensusNode>(count);
            for (var i = 0; i < count; i++)
            {
                nodes.Add(new ExpressConsensusNode(wallets[i].wallet.ToExpressWallet(), i));
            }

            return new ExpressChain(nodes);
        }

        public void CreateCheckpoint(ExpressChain chain, string blockChainStoreDirectory, string checkPointFileName)
        {
            throw new NotImplementedException();
        }

        public Task<JToken?> CreateCheckpointOnline(ExpressChain chain, string checkPointFileName)
        {
            throw new NotImplementedException();
        }

        public ExpressWallet CreateWallet(string name)
        {
            throw new NotImplementedException();
        }

        public Task<JArray> DeployContract(ExpressChain chain, ExpressContract contract, ExpressWalletAccount account)
        {
            throw new NotImplementedException();
        }

        public void ExportBlockchain(ExpressChain chain, string folder, string password, Action<string> writeConsole)
        {
            throw new NotImplementedException();
        }

        public void ExportWallet(ExpressWallet wallet, string filename, string password)
        {
            throw new NotImplementedException();
        }

        public Task<JArray> InvokeContract(ExpressChain chain, ExpressContract contract, IEnumerable<JObject> @params, ExpressWalletAccount? account)
        {
            throw new NotImplementedException();
        }

        public ExpressContract LoadContract(string filepath, Func<string, bool, bool> promptYesNo)
        {
            throw new NotImplementedException();
        }

        public void RestoreCheckpoint(ExpressChain chain, string chainDirectory, string checkPointDirectory)
        {
            throw new NotImplementedException();
        }

        private static bool InitializeProtocolSettings(ExpressChain chain, uint secondsPerBlock = 0)
        {
            secondsPerBlock = secondsPerBlock == 0 ? 15 : secondsPerBlock;

            IEnumerable<KeyValuePair<string, string>> settings()
            {
                yield return new KeyValuePair<string, string>(
                    "ProtocolConfiguration:Magic", $"{chain.Magic}");
                yield return new KeyValuePair<string, string>(
                    "ProtocolConfiguration:AddressVersion", $"{(byte)0x17}");
                yield return new KeyValuePair<string, string>(
                    "ProtocolConfiguration:MillisecondsPerBlock", $"{secondsPerBlock * 1000}");

                foreach (var (node, index) in chain.ConsensusNodes.Select((n, i) => (n, i)))
                {
                    var privateKey = node.Wallet.Accounts
                        .Select(a => a.PrivateKey)
                        .Distinct().Single().HexToBytes();
                    var encodedPublicKey = new KeyPair(privateKey).PublicKey
                        .EncodePoint(true).ToHexString();
                    yield return new KeyValuePair<string, string>(
                        $"ProtocolConfiguration:StandbyValidators:{index}", encodedPublicKey);
                    yield return new KeyValuePair<string, string>(
                        $"ProtocolConfiguration:SeedList:{index}", $"{IPAddress.Loopback}:{node.TcpPort}");
                }
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings())
                .Build();

            return ProtocolSettings.Initialize(config);
        }

        public Task RunBlockchainAsync(string directory, ExpressChain chain, int index, uint secondsPerBlock, TextWriter writer, CancellationToken cancellationToken)
        {
            InitializeProtocolSettings(chain, secondsPerBlock);

            var node = chain.ConsensusNodes[index];

#pragma warning disable IDE0067 // NodeUtility.RunAsync disposes the store when it's done
            return NodeUtility.RunAsync(new RocksDbStore(directory), node, writer, cancellationToken);
#pragma warning restore IDE0067 // Dispose objects before losing scope
        }

        public Task RunCheckpointAsync(string directory, ExpressChain chain, uint secondsPerBlock, TextWriter writer, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<JArray> Transfer(ExpressChain chain, string asset, string quantity, ExpressWalletAccount sender, ExpressWalletAccount receiver)
        {
            throw new NotImplementedException();
        }
    }
}
