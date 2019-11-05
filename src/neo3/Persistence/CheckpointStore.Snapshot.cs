﻿using System;
using Neo;
using Neo.Cryptography.ECC;
using Neo.IO.Wrappers;
using Neo.Ledger;
using RocksDbSharp;

namespace Neo3Express.Persistence
{
    internal partial class CheckpointStore
    {
        private class Snapshot : Neo.Persistence.Snapshot
        {
            private static DataCache<TKey, TValue> GetDataCache<TKey, TValue>(RocksDb db, string familyName, DataCache<TKey, TValue> master)
                where TKey : IEquatable<TKey>, Neo.IO.ISerializable, new()
                where TValue : class, Neo.IO.ICloneable<TValue>, Neo.IO.ISerializable, new()
            {
                var columnFamily = db.GetColumnFamily(familyName);
                return new DataCache<TKey, TValue>(db, columnFamily, master.Values, master.Updater);
            }

            private static MetaDataCache<T> GetMetaDataCache<T>(RocksDb db, byte key, MetaDataCache<T> master)
                where T : class, Neo.IO.ICloneable<T>, Neo.IO.ISerializable, new()
            {
                var columnFamily = db.GetColumnFamily(RocksDbStore.METADATA_FAMILY);
                var keyArray = new byte[1] { key };
                return new MetaDataCache<T>(db, keyArray, columnFamily, master.Value, master.Updater);
            }

            public Snapshot(CheckpointStore store)
            {
                Blocks = GetDataCache(store.db, RocksDbStore.BLOCK_FAMILY, store.blocks);
                Transactions = GetDataCache(store.db, RocksDbStore.TX_FAMILY, store.transactions);
                Contracts = GetDataCache(store.db, RocksDbStore.CONTRACT_FAMILY, store.contracts);
                Storages = GetDataCache(store.db, RocksDbStore.STORAGE_FAMILY, store.storages);
                HeaderHashList = GetDataCache(store.db, RocksDbStore.HEADER_HASH_LIST_FAMILY, store.headerHashList);

                BlockHashIndex = GetMetaDataCache<HashIndexState>(store.db, RocksDbStore.CURRENT_BLOCK_KEY, store.blockHashIndex);
                HeaderHashIndex = GetMetaDataCache<HashIndexState>(store.db, RocksDbStore.CURRENT_HEADER_KEY, store.headerHashIndex);
            }

            public override Neo.IO.Caching.DataCache<UInt256, TrimmedBlock> Blocks { get; }
            public override Neo.IO.Caching.DataCache<UInt256, TransactionState> Transactions { get; }
            public override Neo.IO.Caching.DataCache<UInt160, ContractState> Contracts { get; }
            public override Neo.IO.Caching.DataCache<StorageKey, StorageItem> Storages { get; }
            public override Neo.IO.Caching.DataCache<UInt32Wrapper, HeaderHashList> HeaderHashList { get; }
            public override Neo.IO.Caching.MetaDataCache<HashIndexState> BlockHashIndex { get; }
            public override Neo.IO.Caching.MetaDataCache<HashIndexState> HeaderHashIndex { get; }
        }
    }
}
