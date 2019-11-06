﻿using Microsoft.Extensions.Configuration;
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

        public async Task<JArray> DeployContract(ExpressChain chain, ExpressContract contract, ExpressWalletAccount account)
        {
            var uri = chain.GetUri();
            var result = await NeoRpcClient.ExpressDeployContract(uri, contract, account).ConfigureAwait(false);

            return await SignResult(result, chain, account).ConfigureAwait(false);        }

        public Task<JArray> InvokeContract(ExpressChain chain, ExpressContract contract, IEnumerable<JObject> @params, ExpressWalletAccount? account)
        {
            throw new NotImplementedException();
        }

        public ExpressContract LoadContract(string filepath, Func<string, bool, bool> promptYesNo)
        {
            static string GetContractFile(string path)
            {
                if (Directory.Exists(path))
                {
                    var nefFiles = Directory.EnumerateFiles(path, "*.nef");
                    var nefFileCount = nefFiles.Count();

                    if (nefFileCount == 0)
                    {
                        throw new ArgumentException($"There are no .nef files in {path}");
                    }

                    if (nefFileCount > 1)
                    {
                        throw new ArgumentException($"There are more than one .nef files in {path}. Please specify file name directly");
                    }

                    return nefFiles.Single();
                }

                if (!File.Exists(path) || Path.GetExtension(path) != ".nef")
                {
                    throw new ArgumentException($"{path} is not an .nef file.");
                }

                return path;
            }

            static Neo.SmartContract.NefFile GetNefFile(string path)
            {
                using var reader = new BinaryReader(File.OpenRead(path), System.Text.Encoding.UTF8, false);
                return Neo.IO.Helper.ReadSerializable<Neo.SmartContract.NefFile>(reader);
            }

            static ExpressContract.Function ToExpressContractFunction(AbiContract.Function function) => new ExpressContract.Function
            {
                Name = function.Name,
                ReturnType = function.ReturnType,
                Parameters = function.Parameters.Select(p => new ExpressContract.Parameter
                {
                    Name = p.Name,
                    Type = p.Type
                }).ToList()
            };

            var nefFilePath = GetContractFile(filepath);
            System.Diagnostics.Debug.Assert(File.Exists(nefFilePath));

            string abiFile = Path.ChangeExtension(nefFilePath, ".abi.json");
            if (!File.Exists(abiFile))
            {
                throw new ArgumentException($"there is no .abi.json file for {nefFilePath}.");
            }

            AbiContract abiContract;
            var serializer = new JsonSerializer();
            using (var stream = File.OpenRead(abiFile))
            using (var reader = new JsonTextReader(new StreamReader(stream)))
            {
                abiContract = serializer.Deserialize<AbiContract>(reader);
            }

            var properties = new Dictionary<string, string>();

            if (promptYesNo("Does this contract use storage?", false))
            {
                properties["has-storage"] = "true";
            }

            if (promptYesNo("Is this contract payable?", false))
            {
                properties["is-payable"] = "true";
            }

            var nefFile = GetNefFile(nefFilePath);
            var name = Path.GetFileNameWithoutExtension(nefFilePath);
            return new ExpressContract()
            {
                Name = name,
                Hash = abiContract.Hash, // nefFile.ScriptHash?
                EntryPoint = abiContract.Entrypoint.Name,
                ContractData = nefFile.Script.ToHexString(),
                Functions = abiContract.Functions.Append(abiContract.Entrypoint).Select(ToExpressContractFunction).ToList(),
                Events = abiContract.Events.Select(ToExpressContractFunction).ToList(),
                Properties = properties,
            };        
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

        static JObject Sign(ExpressWalletAccount account, byte[] data)
        {
            var devAccount = DevWalletAccount.FromExpressWalletAccount(account);

            var key = devAccount.GetKey();
            if (key == null)
                throw new InvalidOperationException();

            var publicKey = key.PublicKey.EncodePoint(false).AsSpan().Slice(1).ToArray();
            var signature = Neo.Cryptography.Crypto.Default.Sign(data, key.PrivateKey, publicKey);

            return new JObject
            {
                ["signature"] = signature.ToHexString(),
                ["public-key"] = key.PublicKey.EncodePoint(true).ToHexString(),
                ["contract"] = new JObject
                {
                    ["script"] = account.Contract.Script,
                    ["parameters"] = new JArray(account.Contract.Parameters)
                }
            };
        }

        static IEnumerable<JObject> Sign(ExpressWallet wallet, IEnumerable<string> hashes, byte[] data)
        {
            foreach (var hash in hashes)
            {
                var account = wallet.Accounts.SingleOrDefault(a => a.ScriptHash == hash);
                if (account == null || string.IsNullOrEmpty(account.PrivateKey))
                    continue;

                yield return Sign(account, data);
            }
        }

        static byte[] ToByteArray(string value)
        {
            if (value == null || value.Length == 0)
                return Array.Empty<byte>();
            if (value.Length % 2 == 1)
                throw new FormatException();
            byte[] result = new byte[value.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = byte.Parse(value.Substring(i * 2, 2), System.Globalization.NumberStyles.AllowHexSpecifier);
            return result;
        }

        static JArray Sign(ExpressWalletAccount account, IEnumerable<ExpressConsensusNode> nodes, JToken? json)
        {
            if (json == null)
            {
                throw new ArgumentException(nameof(json));
            }

            var data = ToByteArray(json.Value<string>("hash-data"));

            // TODO: better way to identify the genesis account?
            if (account.Label == "MultiSigContract")
            {
                var hashes = json["script-hashes"].Select(t => t.Value<string>());
                var signatures = nodes.SelectMany(n => Sign(n.Wallet, hashes, data));
                return new JArray(signatures);
            }
            else
            {
                return new JArray(Sign(account, data));
            }
        }

        static async Task<JArray> SignResult(JToken? result, ExpressChain chain, ExpressWalletAccount account)
        {
            var txid = result?["txid"];
            if (txid == null)
            {
                var uri = chain.GetUri();
                var signatures = Sign(account, chain.ConsensusNodes, result);
                var submitSignaturesResult = await NeoRpcClient.ExpressSubmitSignatures(uri, result?["contract-context"], signatures);

                return new JArray(result, submitSignaturesResult);
            }
            else
            {
                return new JArray(result);
            }
        }

        public async Task<JArray> Transfer(ExpressChain chain, string asset, string quantity, ExpressWalletAccount sender, ExpressWalletAccount receiver)
        {
            var uri = chain.GetUri();
            var result = await NeoRpcClient.ExpressTransfer(uri, asset, quantity, sender, receiver)
                .ConfigureAwait(false);

            return await SignResult(result, chain, sender).ConfigureAwait(false);
        }
    }
}
