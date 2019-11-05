using Neo;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;
using Neo3Express.Models;
using NeoExpress.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Neo3Express
{
    internal static class NodeUtility
    {
        public static Task RunAsync(Store store, ExpressConsensusNode node, TextWriter writer, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            Task.Run(() =>
            {
                try
                {
                    var wallet = DevWallet.FromExpressWallet(node.Wallet);
                    using var system = new NeoSystem(store);

                    var logPlugin = new LogPlugin(writer);
                    var rpcPlugin = new ExpressNodeRpcPlugin(store);

                    var channelConfig = new Neo.Network.P2P.ChannelsConfig
                    {
                        Tcp = new IPEndPoint(IPAddress.Any, node.TcpPort),
                        WebSocket = new IPEndPoint(IPAddress.Any, node.WebSocketPort),
                        MinDesiredConnections = Neo.Network.P2P.Peer.DefaultMinDesiredConnections,
                        MaxConnections = Neo.Network.P2P.Peer.DefaultMaxConnections,
                        MaxConnectionsPerAddress = 3
                    };

                    system.StartNode(channelConfig);
                    system.StartConsensus(wallet);
                    system.StartRpc(IPAddress.Loopback, node.RpcPort, wallet);

                    cancellationToken.WaitHandle.WaitOne();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    if (store is IDisposable disp)
                    {
                        disp.Dispose();
                    }
                    tcs.TrySetResult(true);
                }
            });

            return tcs.Task;
        }

        public static UInt160 GetAssetId(string asset)
        {
            if (string.Compare("neo", asset, true) == 0)
                return NativeContract.NEO.Hash;

            if (string.Compare("gas", asset, true) == 0)
                return NativeContract.GAS.Hash;

            return UInt160.Parse(asset);
        }

        public static BigDecimal GetBalance(UInt160 assetId, UInt160 account, Snapshot? snapshot = null)
        {
            byte[] BuildScript()
            {
                using var sb = new ScriptBuilder();
                sb.EmitPush(0);
                sb.EmitAppCall(assetId, "balanceOf", account);
                sb.Emit(OpCode.ADD);
                sb.EmitAppCall(assetId, "decimals");
                return sb.ToArray();
            }

            var script = BuildScript();

            ApplicationEngine engine = snapshot == null
                ? ApplicationEngine.Run(script, extraGAS: 20000000L)
                : ApplicationEngine.Run(script, snapshot, extraGAS: 20000000L);

            if (engine.State.HasFlag(VMState.FAULT))
                return new BigDecimal(0, 0);
            byte decimals = (byte)engine.ResultStack.Pop().GetBigInteger();
            BigInteger amount = engine.ResultStack.Pop().GetBigInteger();
            return new BigDecimal(amount, decimals);
        }

        public static Transaction MakeTransaction(UInt160 sender, UInt160 receiver, UInt160 assetId, BigDecimal? quantity, byte[]? witnessScript)
        {
            byte[] BuildScript(BigDecimal _quantity)
            {
                using var sb = new ScriptBuilder();
                sb.EmitAppCall(assetId, "transfer", sender, receiver, _quantity.Value);
                sb.Emit(OpCode.THROWIFNOT);
                return sb.ToArray();
            }

            using Snapshot snapshot = Blockchain.Singleton.GetSnapshot();

            var balance = GetBalance(assetId, sender, snapshot);
            if (!quantity.HasValue)
            {
                quantity = balance;
            }

            if (balance.Value < quantity.Value.Value)
            {
                throw new InvalidOperationException("Insufficient balance");
            }

            var script = BuildScript(quantity.Value);
            var cosigners = new[]
            {
                new Cosigner()
                {
                    Scopes = WitnessScope.CalledByEntry,
                    Account = sender
                }
            };

            var gasBalance = assetId == NativeContract.GAS.Hash
                ? balance
                : GetBalance(NativeContract.GAS.Hash, sender, snapshot);

            return MakeTransaction(snapshot, sender, script, Array.Empty<TransactionAttribute>(), cosigners, gasBalance, witnessScript);
        }

        private static Transaction MakeTransaction(Snapshot snapshot, UInt160 sender, byte[] script, TransactionAttribute[] attributes, Cosigner[] cosigners, BigDecimal gasBalance, byte[]? witnessScript)
        {
            Random rand = new Random();

            Transaction tx = new Transaction
            {
                Version = 0,
                Nonce = (uint)rand.Next(),
                Script = script,
                Sender = sender,
                ValidUntilBlock = snapshot.Height + Transaction.MaxValidUntilBlockIncrement,
                Attributes = attributes,
                Cosigners = cosigners
            };

            using (ApplicationEngine engine = ApplicationEngine.Run(script, snapshot.Clone(), tx, testMode: true))
            {
                if (engine.State.HasFlag(VMState.FAULT))
                    throw new InvalidOperationException($"Failed execution for '{script.ToHexString()}'");
                tx.SystemFee = Math.Max(engine.GasConsumed - ApplicationEngine.GasFree, 0);
                if (tx.SystemFee > 0)
                {
                    long d = (long)NativeContract.GAS.Factor;
                    long remainder = tx.SystemFee % d;
                    if (remainder > 0)
                        tx.SystemFee += d - remainder;
                    else if (remainder < 0)
                        tx.SystemFee -= remainder;
                }
            }

            UInt160[] hashes = tx.GetScriptHashesForVerifying(snapshot);
            if (hashes.Length != 1 || hashes[0] != sender)
            {
                throw new InvalidOperationException();
            }

            int size = Transaction.HeaderSize + attributes.GetVarSize() + cosigners.GetVarSize() + script.GetVarSize() + Neo.IO.Helper.GetVarSize(hashes.Length);

            byte[]? witness_script = witnessScript ?? snapshot.Contracts.TryGet(sender)?.Script;
            if (witness_script != null) 
            { 
                if (witness_script.IsSignatureContract())
                {
                    size += 66 + witness_script.GetVarSize();
                    tx.NetworkFee += ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES64] + ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES33] + InteropService.GetPrice(InteropService.Neo_Crypto_CheckSig, null);
                }
                else if (witness_script.IsMultiSigContract(out int m, out int n))
                {
                    int size_inv = 65 * m;
                    size += Neo.IO.Helper.GetVarSize(size_inv) + size_inv + witness_script.GetVarSize();
                    tx.NetworkFee += ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES64] * m;
                    using (ScriptBuilder sb = new ScriptBuilder())
                        tx.NetworkFee += ApplicationEngine.OpCodePrices[(OpCode)sb.EmitPush(m).ToArray()[0]];
                    tx.NetworkFee += ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES33] * n;
                    using (ScriptBuilder sb = new ScriptBuilder())
                        tx.NetworkFee += ApplicationEngine.OpCodePrices[(OpCode)sb.EmitPush(n).ToArray()[0]];
                    tx.NetworkFee += InteropService.GetPrice(InteropService.Neo_Crypto_CheckSig, null) * n;
                }
                else
                {
                    //We can support more contract types in the future.
                }
            }
            tx.NetworkFee += size* NativeContract.Policy.GetFeePerByte(snapshot);
            if (gasBalance.Value >= tx.SystemFee + tx.NetworkFee) 
                return tx;

            throw new InvalidOperationException("Insufficient GAS");
        }
    }
}
