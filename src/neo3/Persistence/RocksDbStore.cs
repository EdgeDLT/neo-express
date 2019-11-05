using Neo;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.IO.Wrappers;
using Neo.Ledger;
using RocksDbSharp;
using System;

namespace Neo3Express.Persistence
{
    internal partial class RocksDbStore : Neo.Persistence.Store, IDisposable
    {
        public const string BLOCK_FAMILY = "data:block";
        public const string TX_FAMILY = "data:transaction";
        public const string CONTRACT_FAMILY = "st:contract";
        public const string STORAGE_FAMILY = "st:storage";
        public const string HEADER_HASH_LIST_FAMILY = "ix:header-hash-list";
        public const string METADATA_FAMILY = "metadata";
        public const string GENERAL_STORAGE_FAMILY = "general-storage";
        public const byte CURRENT_BLOCK_KEY = 0xc0;
        public const byte CURRENT_HEADER_KEY = 0xc1;

        public static ColumnFamilies ColumnFamilies => new ColumnFamilies {
                { BLOCK_FAMILY, new ColumnFamilyOptions() },
                { TX_FAMILY, new ColumnFamilyOptions() },
                { CONTRACT_FAMILY, new ColumnFamilyOptions() },
                { STORAGE_FAMILY, new ColumnFamilyOptions() },
                { HEADER_HASH_LIST_FAMILY, new ColumnFamilyOptions() },
                { METADATA_FAMILY, new ColumnFamilyOptions() },
                { GENERAL_STORAGE_FAMILY, new ColumnFamilyOptions() }};

        private static DataCache<TKey, TValue> GetDataCache<TKey, TValue>(
            RocksDb db, string familyName, ReadOptions? readOptions = null, WriteBatch? writeBatch = null)
            where TKey : IEquatable<TKey>, ISerializable, new()
            where TValue : class, ICloneable<TValue>, ISerializable, new()
        {
            var columnFamily = db.GetColumnFamily(familyName);
            return new DataCache<TKey, TValue>(db, columnFamily, readOptions, writeBatch);
        }

        private static MetaDataCache<T> GetMetaDataCache<T>(
            RocksDb db, byte key, ReadOptions? readOptions = null, WriteBatch? writeBatch = null)
            where T : class, ICloneable<T>, ISerializable, new()
        {
            var columnFamily = db.GetColumnFamily(METADATA_FAMILY);
            var keyArray = new byte[1] { key };
            return new MetaDataCache<T>(db, keyArray, columnFamily, readOptions, writeBatch);
        }

        private readonly RocksDb db;

        private readonly DataCache<UInt256, TrimmedBlock> blocks;
        private readonly DataCache<UInt256, TransactionState> transactions;
        private readonly DataCache<UInt160, ContractState> contracts;
        private readonly DataCache<StorageKey, StorageItem> storages;
        private readonly DataCache<UInt32Wrapper, HeaderHashList> headerHashList;
        private readonly MetaDataCache<HashIndexState> blockHashIndex;
        private readonly MetaDataCache<HashIndexState> headerHashIndex;

        public RocksDbStore(string path)
        {
            var options = new DbOptions()
                .SetCreateIfMissing(true)
                .SetCreateMissingColumnFamilies(true);

            db = RocksDb.Open(options, path, ColumnFamilies);

            blocks = GetDataCache<UInt256, TrimmedBlock>(db, BLOCK_FAMILY);
            transactions = GetDataCache<UInt256, TransactionState>(db, TX_FAMILY);
            contracts = GetDataCache<UInt160, ContractState>(db, CONTRACT_FAMILY);
            storages = GetDataCache<StorageKey, StorageItem>(db, STORAGE_FAMILY);
            headerHashList = GetDataCache<UInt32Wrapper, HeaderHashList>(db, HEADER_HASH_LIST_FAMILY);
            blockHashIndex = GetMetaDataCache<HashIndexState>(db, CURRENT_BLOCK_KEY);
            headerHashIndex = GetMetaDataCache<HashIndexState>(db, CURRENT_HEADER_KEY);

            var writeBatch = new WriteBatch();
            var readOptions = new ReadOptions().SetFillCache(true);
            using (Iterator it = db.NewIterator(readOptions: readOptions))
            {
                for (it.SeekToFirst(); it.Valid(); it.Next())
                {
                    writeBatch.Delete(it.Key());
                }
            }
            db.Write(writeBatch);
        }

        public void Dispose()
        {
            db.Dispose();
        }

        public void CheckPoint(string path)
        {
            using (var checkpoint = db.Checkpoint())
            {
                checkpoint.Save(path);
            }
        }

        public override Neo.Persistence.Snapshot GetSnapshot() => new Snapshot(db);

        public override Neo.IO.Caching.DataCache<UInt256, TrimmedBlock> GetBlocks() => blocks;
        public override Neo.IO.Caching.DataCache<UInt256, TransactionState> GetTransactions() => transactions;
        public override Neo.IO.Caching.DataCache<UInt160, ContractState> GetContracts() => contracts;
        public override Neo.IO.Caching.DataCache<StorageKey, StorageItem> GetStorages() => storages;
        public override Neo.IO.Caching.DataCache<UInt32Wrapper, HeaderHashList> GetHeaderHashList() => headerHashList;
        public override Neo.IO.Caching.MetaDataCache<HashIndexState> GetBlockHashIndex() => blockHashIndex;
        public override Neo.IO.Caching.MetaDataCache<HashIndexState> GetHeaderHashIndex() => headerHashIndex;

        public static byte[] GetKey(byte prefix, byte[] key)
        {
            var tempKey = new byte[key.Length + 1];
            tempKey[0] = prefix;
            key.CopyTo(tempKey, 1);
            return tempKey;
        }

        public override byte[] Get(byte[] key)
        {
            var columnFamily = db.GetColumnFamily(GENERAL_STORAGE_FAMILY);
            return db.Get(key, columnFamily);
        }

        public override void Put(byte[] key, byte[] value)
        {
            var columnFamily = db.GetColumnFamily(GENERAL_STORAGE_FAMILY);
            db.Put(key, value, columnFamily);
        }

        public override void PutSync(byte[] key, byte[] value)
        {
            var columnFamily = db.GetColumnFamily(GENERAL_STORAGE_FAMILY);
            db.Put(key, value, columnFamily,
                new WriteOptions().SetSync(true));
        }
    }
}
