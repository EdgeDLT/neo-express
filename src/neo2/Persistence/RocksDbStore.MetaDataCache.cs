﻿using Neo.IO;
using RocksDbSharp;
using System;

namespace Neo2Express.Persistence
{
    internal partial class RocksDbStore
    {
        private class MetaDataCache<T> : Neo.IO.Caching.MetaDataCache<T>
            where T : class, ICloneable<T>, ISerializable, new()
        {
            private readonly RocksDb db;
            private readonly byte[] key;
            private readonly ColumnFamilyHandle familyHandle;
            private readonly ReadOptions? readOptions;
            private readonly WriteBatch? writeBatch;

            public MetaDataCache(RocksDb db, byte[] key, ColumnFamilyHandle familyHandle, ReadOptions? readOptions, WriteBatch? writeBatch, Func<T>? factory = null)
                : base(factory)
            {
                this.db = db;
                this.key = key;
                this.familyHandle = familyHandle;
                this.readOptions = readOptions;
                this.writeBatch = writeBatch;
            }

#pragma warning disable CS8609 // Nullability of reference types in return type doesn't match overridden member.
            // Neo 2.x is not compiled with C# 8, so not sure why C# compiler thinks
            // TryGetInternal can't return null. But it can so supress the warning.
            protected override T? TryGetInternal()
#pragma warning restore CS8609 // Nullability of reference types in return type doesn't match overridden member.
            {
                return db.TryGet<T>(key, familyHandle, readOptions);
            }

            protected override void AddInternal(T item)
            {
                UpdateInternal(item);
            }

            protected override void UpdateInternal(T item)
            {
                writeBatch?.Put(key, item.ToArray(), familyHandle);
            }
        }
    }
}
