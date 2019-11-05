using System;
using System.Linq;
using Akka.Actor;
using Microsoft.AspNetCore.Http;
using Neo;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;

namespace Neo3Express
{
    internal class ExpressNodeRpcPlugin : Plugin, IRpcPlugin
    {
        private readonly Neo.Persistence.Store store;

        public ExpressNodeRpcPlugin(Neo.Persistence.Store store)
        {
            this.store = store;
        }

        public override void Configure()
        {
        }

        public JObject? OnProcess(HttpContext context, string method, JArray @params) 
            => method switch
            {
                "express-create-checkpoint" => OnCheckpointCreate(@params),
                "express-show-coins" => OnExpressShowCoins(@params),
                "express-submit-signatures" => OnExpressSubmitSignatures(@params),
                "express-transfer" => OnExpressTransfer(@params),
                _ => null,
            };

        public JObject OnCheckpointCreate(JArray @params)
        {
            string filename = @params[0].AsString();

            if (ProtocolSettings.Default.StandbyValidators.Length > 1)
            {
                throw new Exception("Checkpoint create is only supported on single node express instances");
            }

            if (store is Persistence.RocksDbStore rocksDbStore)
            {
                var defaultAccount = System.RpcServer.Wallet.GetAccounts().Single(a => a.IsDefault);
                BlockchainOperations.CreateCheckpoint(
                    rocksDbStore,
                    filename,
                    ProtocolSettings.Default.Magic,
                    defaultAccount.ScriptHash.ToAddress());

                return filename;
            }
            else
            {
                throw new Exception("Checkpoint create is only supported for RocksDb storage implementation");
            }
        }

        private JObject? OnExpressSubmitSignatures(JArray @params)
        {
            var context = ContractParametersContext.FromJson(@params[0]);
            var signatures = (JArray)@params[1];

            foreach (var signature in signatures)
            {
                var signatureData = signature["signature"].AsString().HexToBytes();
                var publicKeyData = signature["public-key"].AsString().HexToBytes();
                var contractScript = signature["contract"]["script"].AsString().HexToBytes();
                var parameters = ((JArray)signature["contract"]["parameters"])
                    .Select(j => Enum.Parse<ContractParameterType>(j.AsString()));

                var publicKey = ECPoint.FromBytes(publicKeyData, ECCurve.Secp256r1);
                var contract = Contract.Create(parameters.ToArray(), contractScript);
                if (!context.AddSignature(contract, publicKey, signatureData))
                {
                    throw new Exception($"AddSignature failed for {signature["public-key"].AsString()}");
                }

                if (context.Completed)
                    break;
            }

            if (context.Verifiable is Transaction tx)
            {
                return CreateContextResponse(context, tx);
            }
            else
            {
                throw new Exception("Only support to relay transaction");
            }
        }

        private JObject? OnExpressTransfer(JArray @params)
        {
            static BigDecimal? GetQuantity(UInt160 assetId, string quantity)
            {
                if (quantity.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var assetDescriptor = new AssetDescriptor(assetId);
                if (BigDecimal.TryParse(quantity, assetDescriptor.Decimals, out var result))
                {
                    return result;
                }

                throw new ArgumentException(nameof(quantity));
            }

            var assetId = NodeUtility.GetAssetId(@params[0].AsString());
            var quantity = GetQuantity(assetId, @params[1].AsString());
            var sender = @params[2].AsString().ToScriptHash();
            var receiver = @params[3].AsString().ToScriptHash();
            var witnessScript = @params[4].AsString().HexToBytes();

            var tx = NodeUtility.MakeTransaction(sender, receiver, assetId, quantity, witnessScript);
            var context = new ContractParametersContext(tx);

            return CreateContextResponse(context, tx);
        }

        private JObject? OnExpressShowCoins(JArray @params)
        {
            var address = @params[0].AsString().ToScriptHash();

            using var snapshot = Blockchain.Singleton.GetSnapshot();
            var neoBalance = NodeUtility.GetBalance(NativeContract.NEO.Hash, address, snapshot);
            var gasBalance = NodeUtility.GetBalance(NativeContract.GAS.Hash, address, snapshot);

            var j = new JObject();
            j["NEO"] = neoBalance.ToString();
            j["GAS"] = gasBalance.ToString();
            return j;
        }

        private JObject CreateContextResponse(ContractParametersContext context, Transaction? tx)
        {
            static JObject ToJson(ContractParametersContext context)
            {
                var json = new JObject();
                json["contract-context"] = context.ToJson();
                json["script-hashes"] = new JArray(context.ScriptHashes
                    .Select(hash => new JString(hash.ToAddress())));
                json["hash-data"] = context.Verifiable.GetHashData().ToHexString();

                return json;
            }

            if (tx == null)
            {
                return new JObject();
            }

            if (context.Completed)
            {
                tx.Witnesses = context.GetWitnesses();

                System.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });

                JObject json = new JObject();
                json["txid"] = tx.Hash.ToString();
                return json;
            }
            else
            {
                return ToJson(context);
            }
        }

        public void PostProcess(HttpContext context, string method, JArray _params, JObject result)
        {
        }

        public void PreProcess(HttpContext context, string method, JArray _params)
        {
        }
    }
}
