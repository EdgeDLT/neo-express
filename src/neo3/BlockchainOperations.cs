using Microsoft.Extensions.Configuration;
using Neo;
using Neo.Wallets;
using Neo3Express.Models;
using Neo3Express.Persistence;
using NeoExpress.Abstractions;
using NeoExpress.Abstractions.Models;
using Newtonsoft.Json;
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

        private const string ADDRESS_FILENAME = "ADDRESS.neo-3-express";

        private static string GetAddressFilePath(string directory) =>
            Path.Combine(directory, ADDRESS_FILENAME);

        internal static void CreateCheckpoint(RocksDbStore db, string checkPointFileName, long magic, string scriptHash)
        {
            string tempPath;
            do
            {
                tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            }
            while (Directory.Exists(tempPath));

            try
            {
                db.CheckPoint(tempPath);

                using (var stream = File.OpenWrite(GetAddressFilePath(tempPath)))
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine(magic);
                    writer.WriteLine(scriptHash);
                }

                if (File.Exists(checkPointFileName))
                {
                    throw new InvalidOperationException(checkPointFileName + " checkpoint file already exists");
                }
                System.IO.Compression.ZipFile.CreateFromDirectory(tempPath, checkPointFileName);
            }
            finally
            {
                Directory.Delete(tempPath, true);
            }
        }

        public void CreateCheckpoint(ExpressChain chain, string blockChainStoreDirectory, string checkPointFileName)
        {
            using var db = new RocksDbStore(blockChainStoreDirectory);
            CreateCheckpoint(db, checkPointFileName, chain.Magic, chain.ConsensusNodes[0].Wallet.DefaultAccount.ScriptHash);
        }

        public Task<JToken?> CreateCheckpointOnline(ExpressChain chain, string checkPointFileName)
        {
            var uri = chain.GetUri();
            return NeoRpcClient.ExpressCreateCheckpoint(uri, checkPointFileName);
        }

        public ExpressWallet CreateWallet(string name)
        {
            var wallet = new DevWallet(name);
            var account = wallet.CreateAccount();
            account.IsDefault = true;
            return wallet.ToExpressWallet();
        }

        public Task<JArray> DeployContract(ExpressChain chain, ExpressContract contract, ExpressWalletAccount account)
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

        public void ExportBlockchain(ExpressChain chain, string folder, string password, Action<string> writeConsole)
        {
            void WriteNodeConfigJson(ExpressConsensusNode _node, string walletPath)
            {
                using var stream = File.Open(Path.Combine(folder, $"{_node.Wallet.Name}.config.json"), FileMode.Create, FileAccess.Write);
                using var writer = new JsonTextWriter(new StreamWriter(stream)) { Formatting = Formatting.Indented };

                writer.WriteStartObject();
                writer.WritePropertyName("ApplicationConfiguration");
                writer.WriteStartObject();

                writer.WritePropertyName("Paths");
                writer.WriteStartObject();
                writer.WritePropertyName("Chain");
                writer.WriteValue("Chain_{0}");
                writer.WriteEndObject();

                writer.WritePropertyName("P2P");
                writer.WriteStartObject();
                writer.WritePropertyName("Port");
                writer.WriteValue(_node.TcpPort);
                writer.WritePropertyName("WsPort");
                writer.WriteValue(_node.WebSocketPort);
                writer.WriteEndObject();

                writer.WritePropertyName("RPC");
                writer.WriteStartObject();
                writer.WritePropertyName("BindAddress");
                writer.WriteValue("127.0.0.1");
                writer.WritePropertyName("Port");
                writer.WriteValue(_node.RpcPort);
                writer.WritePropertyName("SslCert");
                writer.WriteValue("");
                writer.WritePropertyName("SslCertPassword");
                writer.WriteValue("");
                writer.WritePropertyName("MasGasInvoke");
                writer.WriteValue(10);
                writer.WriteEndObject();

                writer.WritePropertyName("UnlockWallet");
                writer.WriteStartObject();
                writer.WritePropertyName("Path");
                writer.WriteValue(walletPath);
                writer.WritePropertyName("Password");
                writer.WriteValue(password);
                writer.WritePropertyName("StartConsensus");
                writer.WriteValue(true);
                writer.WritePropertyName("IsActive");
                writer.WriteValue(true);
                writer.WriteEndObject();

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            void WriteProtocolJson()
            {
                using var stream = File.Open(Path.Combine(folder, "protocol.json"), FileMode.Create, FileAccess.Write);
                using var writer = new JsonTextWriter(new StreamWriter(stream)) { Formatting = Formatting.Indented };

                writer.WriteStartObject();
                writer.WritePropertyName("ProtocolConfiguration");
                writer.WriteStartObject();

                writer.WritePropertyName("Magic");
                writer.WriteValue(chain.Magic);
                writer.WritePropertyName("AddressVersion");
                writer.WriteValue(23);
                writer.WritePropertyName("MillisecondsPerBlock");
                writer.WriteValue(15000);

                writer.WritePropertyName("StandbyValidators");
                writer.WriteStartArray();
                for (int i = 0; i < chain.ConsensusNodes.Count; i++)
                {
                    var account = DevWalletAccount.FromExpressWalletAccount(chain.ConsensusNodes[i].Wallet.DefaultAccount);
                    var key = account.GetKey();
                    if (key != null)
                    {
                        writer.WriteValue(key.PublicKey.EncodePoint(true).ToHexString());
                    }
                }
                writer.WriteEndArray();

                writer.WritePropertyName("SeedList");
                writer.WriteStartArray();
                foreach (var node in chain.ConsensusNodes)
                {
                    writer.WriteValue($"{IPAddress.Loopback}:{node.TcpPort}");
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            for (var i = 0; i < chain.ConsensusNodes.Count; i++)
            {
                var node = chain.ConsensusNodes[i];
                writeConsole($"Exporting {node.Wallet.Name} Conensus Node wallet");

                var walletPath = Path.Combine(folder, $"{node.Wallet.Name}.wallet.json");
                if (File.Exists(walletPath))
                {
                    File.Delete(walletPath);
                }

                ExportWallet(node.Wallet, walletPath, password);
                WriteNodeConfigJson(node, walletPath);
            }

            WriteProtocolJson();
        }

        public void ExportWallet(ExpressWallet wallet, string filename, string password)
        {
            var devWallet = DevWallet.FromExpressWallet(wallet);
            devWallet.Export(filename, password);
        }

        private static void ValidateCheckpoint(string checkPointDirectory, long magic, ExpressWalletAccount account)
        {
            var addressFile = GetAddressFilePath(checkPointDirectory);
            if (!File.Exists(addressFile))
            {
                throw new Exception("Invalid Checkpoint");
            }

            long checkPointMagic;
            string scriptHash;
            using (var stream = File.OpenRead(addressFile))
            using (var reader = new StreamReader(stream))
            {
                checkPointMagic = long.Parse(reader.ReadLine() ?? string.Empty);
                scriptHash = reader.ReadLine() ?? string.Empty;
            }

            if (magic != checkPointMagic || scriptHash != account.ScriptHash)
            {
                throw new Exception("Invalid Checkpoint");
            }
        }

        public void RestoreCheckpoint(ExpressChain chain, string chainDirectory, string checkPointDirectory)
        {
            var node = chain.ConsensusNodes[0];
            ValidateCheckpoint(checkPointDirectory, chain.Magic, node.Wallet.DefaultAccount);

            var addressFile = GetAddressFilePath(checkPointDirectory);
            if (!File.Exists(addressFile))
            {
                File.Delete(addressFile);
            }

            Directory.Move(checkPointDirectory, chainDirectory);
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
            InitializeProtocolSettings(chain, secondsPerBlock);

            var node = chain.ConsensusNodes[0];

#pragma warning disable IDE0067 // NodeUtility.RunAsync disposes the store when it's done
            return NodeUtility.RunAsync(new RocksDbStore(directory), node, writer, cancellationToken);
#pragma warning restore IDE0067 // Dispose objects before losing scope
        }

        public Task<JArray> Transfer(ExpressChain chain, string asset, string quantity, ExpressWalletAccount sender, ExpressWalletAccount receiver)
        {
            throw new NotImplementedException();
        }
    }
}
