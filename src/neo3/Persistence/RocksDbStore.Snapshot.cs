using Neo;
using Neo.Cryptography.ECC;
using Neo.IO.Caching;
using Neo.IO.Wrappers;
using Neo.Ledger;
using RocksDbSharp;

namespace Neo3Express.Persistence
{
    internal partial class RocksDbStore
    {
        private class Snapshot : Neo.Persistence.Snapshot
        {
            private readonly RocksDb db;
            private readonly RocksDbSharp.Snapshot snapshot;
            private readonly ReadOptions readOptions;
            private readonly WriteBatch writeBatch;

            public Snapshot(RocksDb db)
            {
                this.db = db;
                snapshot = db.CreateSnapshot();
                readOptions = new ReadOptions().SetSnapshot(snapshot).SetFillCache(false);
                writeBatch = new WriteBatch();

                Blocks = GetDataCache<UInt256, TrimmedBlock>(
                    db, BLOCK_FAMILY, readOptions, writeBatch);
                Transactions = GetDataCache<UInt256, TransactionState>(
                    db, TX_FAMILY, readOptions, writeBatch);
                Contracts = GetDataCache<UInt160, ContractState>(
                    db, CONTRACT_FAMILY, readOptions, writeBatch);
                Storages = GetDataCache<StorageKey, StorageItem>(
                    db, STORAGE_FAMILY, readOptions, writeBatch);
                HeaderHashList = GetDataCache<UInt32Wrapper, HeaderHashList>(
                    db, HEADER_HASH_LIST_FAMILY, readOptions, writeBatch);
                BlockHashIndex = GetMetaDataCache<HashIndexState>(
                    db, CURRENT_BLOCK_KEY, readOptions, writeBatch);
                HeaderHashIndex = GetMetaDataCache<HashIndexState>(
                    db, CURRENT_HEADER_KEY, readOptions, writeBatch);
            }

            public override void Dispose()
            {
                snapshot.Dispose();
            }

            public override void Commit()
            {
                base.Commit();
                db.Write(writeBatch);
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
