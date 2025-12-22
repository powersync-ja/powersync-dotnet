namespace PowerSync.Common.Tests.Client.Sync;

using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.DB.Schema;

class TestData
{
    public static OplogEntry putAsset1_1 = OplogEntry.FromRow(new OplogEntryJSON
    {
        OpId = "1",
        Op = new OpType(OpTypeEnum.PUT).ToJSON(),
        ObjectType = "assets",
        ObjectId = "O1",
        Data = JsonConvert.SerializeObject(new { description = "bar" }),
        Checksum = 1
    });

    public static OplogEntry putAsset2_2 = OplogEntry.FromRow(new OplogEntryJSON
    {
        OpId = "2",
        Op = new OpType(OpTypeEnum.PUT).ToJSON(),
        ObjectType = "assets",
        ObjectId = "O2",
        Data = JsonConvert.SerializeObject(new { description = "bar" }),
        Checksum = 2
    });

    public static OplogEntry putAsset1_3 = OplogEntry.FromRow(new OplogEntryJSON
    {
        OpId = "3",
        Op = new OpType(OpTypeEnum.PUT).ToJSON(),
        ObjectType = "assets",
        ObjectId = "O1",
        Data = JsonConvert.SerializeObject(new { description = "bard" }),
        Checksum = 3
    });

    public static OplogEntry removeAsset1_4 = OplogEntry.FromRow(new OplogEntryJSON
    {
        OpId = "4",
        Op = new OpType(OpTypeEnum.REMOVE).ToJSON(),
        ObjectType = "assets",
        ObjectId = "O1",
        Checksum = 4
    });

    public static OplogEntry removeAsset1_5 = OplogEntry.FromRow(new OplogEntryJSON
    {
        OpId = "5",
        Op = new OpType(OpTypeEnum.REMOVE).ToJSON(),
        ObjectType = "assets",
        ObjectId = "O1",
        Checksum = 5
    });
}

public class BucketStorageTests : IAsyncLifetime
{
    private PowerSyncDatabase db = default!;
    private IBucketStorageAdapter bucketStorage = default!;

    public async Task InitializeAsync()
    {
        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "powersync.db" },
            Schema = TestSchema.AppSchema,
        });
        await db.Init();
        bucketStorage = new SqliteBucketStorage(db.Database, createLogger());

    }

    public async Task DisposeAsync()
    {
        await db.DisconnectAndClear();
        await db.Close();
        bucketStorage.Close();
    }

    private record IdResult(string id);
    private record DescriptionResult(string description);
    private record AssetResult(string id, string description, string? make = null);

    static async Task ExpectAsset1_3(PowerSyncDatabase database)
    {
        var result = await database.GetAll<AssetResult>("SELECT id, description, make FROM assets WHERE id = 'O1'");
        Assert.Equal(new AssetResult("O1", "bard", null), result[0]);
    }

    static async Task ExpectNoAsset1(PowerSyncDatabase database)
    {
        var result = await database.GetAll<AssetResult>("SELECT id, description, make FROM assets WHERE id = 'O1'");
        Assert.Empty(result);
    }

    static async Task ExpectNoAssets(PowerSyncDatabase database)
    {
        var result = await database.GetAll<AssetResult>("SELECT id, description, make FROM assets");
        Assert.Empty(result);
    }

    async Task SyncLocalChecked(Checkpoint checkpoint)
    {
        var result = await bucketStorage.SyncLocalDatabase(checkpoint);
        Assert.Equal(new SyncLocalDatabaseResult { Ready = true, CheckpointValid = true }, result);
    }

    private ILogger createLogger()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Enable console logging
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        return loggerFactory.CreateLogger("TestLogger");
    }

    [Fact]
    public async Task BasicSetup()
    {
        await db.WaitForReady();
        var initialBucketStates = await bucketStorage.GetBucketStates();
        Assert.Empty(initialBucketStates);

        await bucketStorage.SaveSyncData(new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)]));

        var bucketStates = await bucketStorage.GetBucketStates();

        Assert.Collection(bucketStates, state =>
        {
            Assert.Equal("bucket1", state.Bucket);
            Assert.Equal("3", state.OpId);
        });

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        await ExpectAsset1_3(db);
    }

    [Fact]
    public async Task ShouldGetObjectFromMultipleBuckets()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [new SyncDataBucket("bucket1", [TestData.putAsset1_3], false), new SyncDataBucket("bucket2", [TestData.putAsset1_3], false)])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 3 }, new BucketChecksum { Bucket = "bucket2", Checksum = 3 }]
        });

        await ExpectAsset1_3(db);
    }

    [Fact]
    public async Task ShouldPrioritizeLaterUpdates()
    {
        // Test behavior when the same object is present in multiple buckets.
        // In this case, there are two different versions in the different buckets.
        // While we should not get this with our server implementation, the client still specifies this behavior:
        // The largest op_id wins.

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [new SyncDataBucket("bucket1", [TestData.putAsset1_3], false), new SyncDataBucket("bucket2", [TestData.putAsset1_1], false)])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 3 }, new BucketChecksum { Bucket = "bucket2", Checksum = 1 }]
        });

        await ExpectAsset1_3(db);
    }

    [Fact]
    public async Task ShouldIgnoreRemoveFromOneBucket()
    {
        // When we have 1 PUT and 1 REMOVE, the object must be kept.);   
        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_3], false), new SyncDataBucket("bucket2", [TestData.putAsset1_3, TestData.removeAsset1_4], false)])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "4",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 3 }, new BucketChecksum { Bucket = "bucket2", Checksum = 7 }]
        });

        await ExpectAsset1_3(db);
    }

    [Fact]
    public async Task ShouldRemoveWhenRemovedFromAllBuckets()
    {
        // When we only have REMOVE left for an object, it must be deleted.
        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_3, TestData.removeAsset1_5], false), new SyncDataBucket("bucket2", [TestData.putAsset1_3, TestData.removeAsset1_4], false)])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "5",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 8 }, new BucketChecksum { Bucket = "bucket2", Checksum = 7 }]
        });

        await ExpectNoAssets(db);
    }

    [Fact]
    public async Task ShouldUseSubkeys()
    {
        // Subkeys cause this to be treated as a separate entity in the oplog,
        // but the same entity in the local database.

        var put4 = OplogEntry.FromRow(new OplogEntryJSON
        {
            OpId = "4",
            Op = new OpType(OpTypeEnum.PUT).ToJSON(),
            Subkey = "b",
            ObjectType = "assets",
            ObjectId = "O1",
            Data = JsonConvert.SerializeObject(new { description = "B" }),
            Checksum = 4
        });

        var remove5 = OplogEntry.FromRow(new OplogEntryJSON
        {
            OpId = "5",
            Op = new OpType(OpTypeEnum.REMOVE).ToJSON(),
            Subkey = "b",
            ObjectType = "assets",
            ObjectId = "O1",
            Checksum = 5
        });

        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset1_3, put4], false)])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "4",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 8 }]
        });

        var result = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets WHERE id = 'O1'");
        Assert.Equal(new AssetResult("O1", "B", null), result[0]);

        await bucketStorage.SaveSyncData(new SyncDataBatch([new SyncDataBucket("bucket1", [remove5], false)]));

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "5",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 13 }]
        });

        await ExpectAsset1_3(db);
    }

    [Fact]
    public async Task ShouldFailChecksumValidation()
    {
        // Simple checksum validation
        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)])
        );

        var result = await bucketStorage.SyncLocalDatabase(new Checkpoint
        {
            LastOpId = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 10 }, new BucketChecksum { Bucket = "bucket2", Checksum = 1 }]
        });

        var expected = new SyncLocalDatabaseResult
        {
            Ready = false,
            CheckpointValid = false,
            CheckpointFailures = ["bucket1", "bucket2"]
        };

        Assert.Equal(expected, result);

        await ExpectNoAssets(db);
    }

    [Fact]
    public async Task ShouldDeleteBuckets()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [new SyncDataBucket("bucket1", [TestData.putAsset1_3], false), new SyncDataBucket("bucket2", [TestData.putAsset1_3], false)])
        );

        await bucketStorage.RemoveBuckets(["bucket2"]);
        // The delete only takes effect after syncLocal.

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 3 }]
        });

        // Bucket is deleted, but object is still present in other buckets.
        await ExpectAsset1_3(db);

        await bucketStorage.RemoveBuckets(["bucket1"]);
        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = []
        });

        // Both buckets deleted - object removed.
        await ExpectNoAssets(db);
    }

    [Fact]
    public async Task ShouldDeleteAndRecreateBuckets()
    {
        // Save some data
        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_1], false)])
        );

        // Delete the bucket
        await bucketStorage.RemoveBuckets(["bucket1"]);

        // Save some data again
        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset1_3], false)])
        );

        // Delete again
        await bucketStorage.RemoveBuckets(["bucket1"]);

        // Final save of data
        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset1_3], false)])
        );

        // Check that the data is there
        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 4 }]
        });

        await ExpectAsset1_3(db);

        // Now final delete
        await bucketStorage.RemoveBuckets(["bucket1"]);
        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = []
        });

        await ExpectNoAssets(db);
    }

    [Fact]
    public async Task ShouldHandleMove()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1",
                [
                    OplogEntry.FromRow(new OplogEntryJSON
                    {
                        OpId = "1",
                        Op = new OpType(OpTypeEnum.MOVE).ToJSON(),
                        Checksum = 1
                    })
                ], false)
            ])
        );

        await bucketStorage.SaveSyncData(
            new SyncDataBatch([new SyncDataBucket("bucket1", [TestData.putAsset1_3], false)])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 4 }]
        });

        await ExpectAsset1_3(db);
    }

    [Fact]
    public async Task ShouldHandleClear()
    {
        // Save some data
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "1",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 1 }
        ]
        });

        // CLEAR, then save new data
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1",
                [
                    OplogEntry.FromRow(new OplogEntryJSON
                    {
                        OpId = "2",
                        Op = new OpType(OpTypeEnum.CLEAR).ToJSON(),
                        Checksum = 2
                    }),
                    OplogEntry.FromRow(new OplogEntryJSON
                    {
                        OpId = "3",
                        Op = new OpType(OpTypeEnum.PUT).ToJSON(),
                        Checksum = 3,
                        Data = TestData.putAsset2_2.Data,
                        ObjectId = TestData.putAsset2_2.ObjectId,
                        ObjectType = TestData.putAsset2_2.ObjectType
                    })
                ], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            // 2 + 3. 1 is replaced with 2.
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 5 }]
        });

        await ExpectNoAsset1(db);

        var result = await db.Get<AssetResult>("SELECT id, description FROM assets WHERE id = 'O2'");

        Assert.Equal(new AssetResult("O2", "bar"), result);
    }

    [Fact]
    public async Task UpdateWithNewTypes()
    {
        var dbName = "test-bucket-storage-new-types.db";
        var powersync = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = new Schema([]),
        });
        await powersync.Init();
        bucketStorage = new SqliteBucketStorage(powersync.Database);

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "4",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        // Ensure an exception is thrown due to missing table
        await Assert.ThrowsAsync<SqliteException>(async () =>
            await powersync.GetAll<AssetResult>("SELECT * FROM assets"));

        await powersync.Close();

        powersync = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = TestSchema.AppSchema,
        });
        await powersync.Init();

        await ExpectAsset1_3(powersync);

        await powersync.DisconnectAndClear();
        await powersync.Close();
    }

    [Fact]
    public async Task ShouldRemoveTypes()
    {
        var dbName = "test-bucket-storage-remove-types.db";

        // Create database with initial schema
        var powersync = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = TestSchema.AppSchema,
        });

        await powersync.Init();
        bucketStorage = new SqliteBucketStorage(powersync.Database);

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        await ExpectAsset1_3(powersync);
        await powersync.Close();

        // Now open another instance with an empty schema
        powersync = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = new Schema([]),
        });
        await powersync.Init();

        await Assert.ThrowsAsync<SqliteException>(async () =>
            await powersync.Execute("SELECT * FROM assets"));

        await powersync.Close();

        // Reopen database with the original schema
        powersync = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = TestSchema.AppSchema,
        });
        await powersync.Init();

        await ExpectAsset1_3(powersync);

        await powersync.DisconnectAndClear();
        await powersync.Close();
    }

    private record OplogStats(string Type, string Id, int Count);

    [Fact]
    public async Task ShouldCompact()
    {
        // Test compacting behavior.
        // This test relies heavily on internals and will have to be updated when the compact implementation is updated.

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.removeAsset1_4], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "4",
            WriteCheckpoint = "4",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 7 }]
        });

        await bucketStorage.ForceCompact();

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "4",
            WriteCheckpoint = "4",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 7 }]
        });

        var stats = await db.GetAll<OplogStats>(
            "SELECT row_type as Type, row_id as Id, count(*) as Count FROM ps_oplog GROUP BY row_type, row_id ORDER BY row_type, row_id"
        );

        var expectedStats = new List<OplogStats> { new("assets", "O2", 1) };

        Assert.Equal(expectedStats, stats);
    }

    [Fact]
    public async Task ShouldNotSyncLocalDbWithPendingCrud_ServerRemoved()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        // Local save
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

        var insertedResult = await db.GetAll<IdResult>("SELECT id FROM assets WHERE id = 'O3'");
        Assert.Equal(new IdResult("O3"), insertedResult[0]);

        // At this point, we have data in the CRUD table and are not able to sync the local DB.
        var result = await bucketStorage.SyncLocalDatabase(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        var expectedResult = new SyncLocalDatabaseResult
        {
            Ready = false,
            CheckpointValid = true
        };

        Assert.Equal(expectedResult, result);

        var batch = await bucketStorage.GetCrudBatch();
        if (batch != null)
        {
            await batch.Complete("");
        }

        await bucketStorage.UpdateLocalTarget(() => Task.FromResult("4"));

        // At this point, the data has been uploaded but not synced back yet.
        var result3 = await bucketStorage.SyncLocalDatabase(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        Assert.Equal(expectedResult, result3);

        // The data must still be present locally.
        var stillPresentResult = await db.GetAll<IdResult>("SELECT id FROM assets WHERE id = 'O3'");
        Assert.Equal(new IdResult("O3"), stillPresentResult[0]);

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
            new SyncDataBucket("bucket1", Array.Empty<OplogEntry>(), false)
            ])
        );

        // Now we have synced the data back (or lack of data in this case),
        // so we can do a local sync.
        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "5",
            WriteCheckpoint = "5",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        // Since the object was not in the sync response, it is deleted.
        var deletedResult = await db.GetAll<IdResult>("SELECT id FROM assets WHERE id = 'O3'");
        Assert.Empty(deletedResult);
    }

    [Fact]
    public async Task ShouldNotSyncLocalDbWithPendingCrud_WhenMoreCrudIsAdded_1()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        // Local save
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

        var batch = await bucketStorage.GetCrudBatch();
        if (batch != null)
        {
            await batch.Complete("");
        }

        await bucketStorage.UpdateLocalTarget(() => Task.FromResult("4"));

        var result3 = await bucketStorage.SyncLocalDatabase(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        var expectedResult = new SyncLocalDatabaseResult
        {
            Ready = false,
            CheckpointValid = true
        };

        Assert.Equal(expectedResult, result3);

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", Array.Empty<OplogEntry>(), false)
            ])
        );

        // Add more data before SyncLocalDatabase.
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O4"]);

        var result4 = await bucketStorage.SyncLocalDatabase(new Checkpoint
        {
            LastOpId = "5",
            WriteCheckpoint = "5",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        Assert.Equal(expectedResult, result4);
    }

    [Fact]
    public async Task ShouldNotSyncLocalDbWithPendingCrud_WhenMoreCrudIsAdded_2()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
              new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        // Local save
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

        var batch = await bucketStorage.GetCrudBatch();

        // Add more data before calling complete()
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O4"]);
        if (batch != null)
        {
            await batch.Complete("");
        }

        await bucketStorage.UpdateLocalTarget(() => Task.FromResult("4"));

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
            new SyncDataBucket("bucket1", [], false)
            ])
        );

        var result4 = await bucketStorage.SyncLocalDatabase(new Checkpoint
        {
            LastOpId = "5",
            WriteCheckpoint = "5",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        var expected = new SyncLocalDatabaseResult
        {
            Ready = false,
            CheckpointValid = true
        };

        Assert.Equal(expected, result4);
    }

    [Fact]
    public async Task ShouldNotSyncLocalDbWithPendingCrud_UpdateOnServer()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        // Local save
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

        var batch = await bucketStorage.GetCrudBatch();
        if (batch != null)
        {
            await batch.Complete("");
        }

        await bucketStorage.UpdateLocalTarget(() => Task.FromResult("4"));

        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
            new SyncDataBucket("bucket1",
            [
                OplogEntry.FromRow(new OplogEntryJSON
                {
                    OpId = "5",
                    Op = new OpType(OpTypeEnum.PUT).ToJSON(),
                    ObjectType = "assets",
                    ObjectId = "O3",
                    Checksum = 5,
                    Data = JsonConvert.SerializeObject(new { description = "server updated" })
                })
            ], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "5",
            WriteCheckpoint = "5",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 11 }
        ]
        });

        var updatedResult = await db.GetAll<DescriptionResult>("SELECT description FROM assets WHERE id = 'O3'");
        Assert.Equal(new DescriptionResult("server updated"), updatedResult[0]);
    }

    [Fact]
    public async Task ShouldRevertAFailingInsert()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        // Local insert, later rejected by server
        await db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", ["O3", "inserted"]);

        var batch = await bucketStorage.GetCrudBatch();
        if (batch != null)
        {
            await batch.Complete("");
        }

        await bucketStorage.UpdateLocalTarget(() => Task.FromResult("4"));

        var insertedResult = await db.GetAll<DescriptionResult>("SELECT description FROM assets WHERE id = 'O3'");
        Assert.Equal(new DescriptionResult("inserted"), insertedResult[0]);

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "4",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        var revertedResult = await db.GetAll<DescriptionResult>("SELECT description FROM assets WHERE id = 'O3'");
        Assert.Empty(revertedResult);
    }

    [Fact]
    public async Task ShouldRevertAFailingDelete()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        // Local delete, later rejected by server
        await db.Execute("DELETE FROM assets WHERE id = ?", ["O2"]);

        var deletedResult = await db.GetAll<DescriptionResult>("SELECT description FROM assets WHERE id = 'O2'");
        Assert.Empty(deletedResult); // Ensure the record is deleted locally

        // Simulate a permissions error when uploading - data should be preserved
        var batch = await bucketStorage.GetCrudBatch();
        if (batch != null)
        {
            await batch.Complete("");
        }

        await bucketStorage.UpdateLocalTarget(() => Task.FromResult("4"));

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "4",
            Buckets = [new BucketChecksum { Bucket = "bucket1", Checksum = 6 }]
        });

        var revertedResult = await db.GetAll<DescriptionResult>("SELECT description FROM assets WHERE id = 'O2'");
        Assert.Equal(new DescriptionResult("bar"), revertedResult[0]);
    }

    [Fact]
    public async Task ShouldRevertAFailingUpdate()
    {
        await bucketStorage.SaveSyncData(
            new SyncDataBatch(
            [
                new SyncDataBucket("bucket1", [TestData.putAsset1_1, TestData.putAsset2_2, TestData.putAsset1_3], false)
            ])
        );

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "3",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        // Local update, later rejected by server
        await db.Execute("UPDATE assets SET description = ? WHERE id = ?", ["updated", "O2"]);

        var updatedResult = await db.GetAll<DescriptionResult>("SELECT description FROM assets WHERE id = 'O2'");
        Assert.Equal(new DescriptionResult("updated"), updatedResult[0]);

        // Simulate a permissions error when uploading - data should be preserved
        var batch = await bucketStorage.GetCrudBatch();
        if (batch != null)
        {
            await batch.Complete("");
        }

        await bucketStorage.UpdateLocalTarget(async () => await Task.FromResult("4"));

        await SyncLocalChecked(new Checkpoint
        {
            LastOpId = "3",
            WriteCheckpoint = "4",
            Buckets =
            [
            new BucketChecksum { Bucket = "bucket1", Checksum = 6 }
        ]
        });

        var revertedResult = await db.GetAll<DescriptionResult>("SELECT description FROM assets WHERE id = 'O2'");
        Assert.Equal(new DescriptionResult("bar"), revertedResult[0]);
    }
}
