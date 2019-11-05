using NeoExpress.Abstractions;
using NeoExpress.Abstractions.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace neo3
{
    public class BlockchainOperations : IBlockchainOperations
    {
        public Task<JArray> Claim(ExpressChain chain, string asset, ExpressWalletAccount address)
        {
            throw new NotImplementedException();
        }

        public ExpressChain CreateBlockchain(int count)
        {
            throw new NotImplementedException();
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

        public Task RunBlockchainAsync(string directory, ExpressChain chain, int index, uint secondsPerBlock, TextWriter writer, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
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
