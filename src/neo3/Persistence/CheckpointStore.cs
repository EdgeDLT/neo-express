using Neo;
using Neo.Cryptography.ECC;
using Neo.IO.Wrappers;
using Neo.Ledger;
using NeoExpress.Abstractions.Utility;
using RocksDbSharp;
using System;
using System.Collections.Generic;

namespace Neo3Express.Persistence
{
    internal partial class CheckpointStore : Neo.Persistence.Store, IDisposable
    {
        private readonly RocksDb db;

        private readonly DataCache<UInt256, TrimmedBlock> blocks;
        private readonly DataCache<UInt256, TransactionState> transactions;
        private readonly DataCache<UInt160, ContractState> contracts;
        private readonly DataCache<StorageKey, StorageItem> storages;
        private readonly DataCache<UInt32Wrapper, HeaderHashList> headerHashList;
        private readonly MetaDataCache<HashIndexState> blockHashIndex;
        private readonly MetaDataCache<HashIndexState> headerHashIndex;

        private static DataCache<TKey, TValue> GetDataCache<TKey, TValue>(RocksDb db, string familyName)
            where TKey : IEquatable<TKey>, Neo.IO.ISerializable, new()
            where TValue : class, Neo.IO.ICloneable<TValue>, Neo.IO.ISerializable, new()
        {
            var columnFamily = db.GetColumnFamily(familyName);
            return new DataCache<TKey, TValue>(db, columnFamily);
        }

        private static MetaDataCache<T> GetMetaDataCache<T>(
            RocksDb db, byte key)
            where T : class, Neo.IO.ICloneable<T>, Neo.IO.ISerializable, new()
        {
            var columnFamily = db.GetColumnFamily(RocksDbStore.METADATA_FAMILY);
            var keyArray = new byte[1] { key };
            return new MetaDataCache<T>(db, keyArray, columnFamily);
        }

        public CheckpointStore(string path)
        {
            db = RocksDb.OpenReadOnly(new DbOptions(), path, RocksDbStore.ColumnFamilies, false);

            blocks = GetDataCache<UInt256, TrimmedBlock>(db, RocksDbStore.BLOCK_FAMILY);
            transactions = GetDataCache<UInt256, TransactionState>(db, RocksDbStore.TX_FAMILY);
            contracts = GetDataCache<UInt160, ContractState>(db, RocksDbStore.CONTRACT_FAMILY);
            storages = GetDataCache<StorageKey, StorageItem>(db, RocksDbStore.STORAGE_FAMILY);
            headerHashList = GetDataCache<UInt32Wrapper, HeaderHashList>(db, RocksDbStore.HEADER_HASH_LIST_FAMILY);
            blockHashIndex = GetMetaDataCache<HashIndexState>(db, RocksDbStore.CURRENT_BLOCK_KEY);
            headerHashIndex = GetMetaDataCache<HashIndexState>(db, RocksDbStore.CURRENT_HEADER_KEY);
        }

        public void Dispose()
        {
            db.Dispose();
        }

        public override Neo.Persistence.Snapshot GetSnapshot()
        {
            return new Snapshot(this);
        }

        public override Neo.IO.Caching.DataCache<UInt256, TrimmedBlock> GetBlocks() => blocks;
        public override Neo.IO.Caching.DataCache<UInt256, TransactionState> GetTransactions() => transactions;
        public override Neo.IO.Caching.DataCache<UInt160, ContractState> GetContracts() => contracts;
        public override Neo.IO.Caching.DataCache<StorageKey, StorageItem> GetStorages() => storages;
        public override Neo.IO.Caching.DataCache<UInt32Wrapper, HeaderHashList> GetHeaderHashList() => headerHashList;
        public override Neo.IO.Caching.MetaDataCache<HashIndexState> GetBlockHashIndex() => blockHashIndex;
        public override Neo.IO.Caching.MetaDataCache<HashIndexState> GetHeaderHashIndex() => headerHashIndex;

        private readonly Dictionary<byte[], byte[]> generalStorage = new Dictionary<byte[], byte[]>(new ByteArrayComparer());

        public override byte[] Get(byte[] key)
        {
            if (generalStorage.TryGetValue(key, out var value))
            {
                return value;
            }

            var columnFamily = db.GetColumnFamily(RocksDbStore.GENERAL_STORAGE_FAMILY);
            return db.Get(key, columnFamily);
        }

        public override void Put(byte[] key, byte[] value)
        {
            generalStorage[key] = value;
        }

        public override void PutSync(byte[] key, byte[] value)
        {
            Put(key, value);
        }
    }
}
