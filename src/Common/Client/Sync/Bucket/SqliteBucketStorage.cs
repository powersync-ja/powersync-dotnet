namespace Common.Client.Sync.Bucket;


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.DB;
using Common.DB.Crud;
using Newtonsoft.Json;

public class SqliteBucketStorage(IDBAdapter db) : IBucketStorageAdapter
{
    private readonly IDBAdapter db = db;
    private bool hasCompletedSync = false;
    private bool pendingBucketDeletes = true;
    private readonly HashSet<string> tableNames = [];
    private string? clientId;

    private static readonly int COMPACT_OPERATION_INTERVAL = 1000;
    private int compactCounter = COMPACT_OPERATION_INTERVAL;

    private record ExistingTableRowsResult(string name);
    public async Task Init()
    {

        hasCompletedSync = false;
        var existingTableRows = await db.GetAll<ExistingTableRowsResult>("SELECT name FROM sqlite_master WHERE type='table' AND name GLOB 'ps_data_*'");

        foreach (var row in existingTableRows)
        {
            tableNames.Add(row.name);
        }
    }

    public void Dispose()
    {
        Console.WriteLine("Disposing SqliteBucketStorage...");
    }
    private record ClientIdResult(string? client_id);
    public async Task<string> GetClientId()
    {
        if (clientId == null)
        {
            var row = await db.Get<ClientIdResult>("SELECT powersync_client_id() as client_id");
            clientId = row.client_id ?? "";
        }

        return clientId;
    }

    public string GetMaxOpId()
    {
        return "MAX_OP_ID"; // Placeholder for MAX_OP_ID constant.
    }

    public void StartSession() { }

    public async Task<BucketState[]> GetBucketStates()
    {
        return
            await db.GetAll<BucketState>("SELECT name as bucket, cast(last_op as TEXT) as op_id FROM ps_buckets WHERE pending_delete = 0 AND name != '$local'");
    }

    public async Task SaveSyncData(SyncDataBatch batch)
    {
        await db.WriteTransaction(async tx =>
        {
            int count = 0;
            foreach (var b in batch.Buckets)
            {
                await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
                    ["save", JsonConvert.SerializeObject(new { buckets = new[] { b.ToJSON() } })]);
                Console.WriteLine("saveSyncData: Saved batch.");
                count += b.Data.Length;
            }
            compactCounter += count;
        });
    }

    public async Task RemoveBuckets(string[] buckets)
    {
        foreach (var bucket in buckets)
        {
            await DeleteBucket(bucket);
        }
    }

    private async Task DeleteBucket(string bucket)
    {
        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
                ["delete_bucket", bucket]);
        });

        Console.WriteLine("Done deleting bucket.");
        pendingBucketDeletes = true;
    }

    private record LastSyncedResult(string? synced_at);
    public async Task<bool> HasCompletedSync()
    {
        if (hasCompletedSync) return true;

        var result = await db.Get<LastSyncedResult>("SELECT powersync_last_synced_at() as synced_at");

        hasCompletedSync = result.synced_at != null;
        return hasCompletedSync;
    }

    public async Task<SyncLocalDatabaseResult> SyncLocalDatabase(Checkpoint checkpoint)
    {
        var validation = await ValidateChecksums(checkpoint);
        if (!validation.CheckpointValid)
        {
            Console.WriteLine($"Checksums failed for: {JsonConvert.SerializeObject(validation.CheckpointFailures)}");
            foreach (var failedBucket in validation.CheckpointFailures ?? [])
            {
                await DeleteBucket(failedBucket);
            }
            return new SyncLocalDatabaseResult
            {
                Ready = false,
                CheckpointValid = false,
                CheckpointFailures = validation.CheckpointFailures
            };
        }

        var bucketNames = checkpoint.Buckets.Select(b => b.Bucket).ToArray();
        await db.WriteTransaction(async tx =>
        {
            await tx.Execute(
                "UPDATE ps_buckets SET last_op = ? WHERE name IN (SELECT json_each.value FROM json_each(?))",
                [checkpoint.LastOpId, JsonConvert.SerializeObject(bucketNames)]
            );

            if (checkpoint.WriteCheckpoint != null)
            {
                await tx.Execute(
                    "UPDATE ps_buckets SET last_op = ? WHERE name = '$local'",
                    [checkpoint.WriteCheckpoint]
                );
            }
        });

        var valid = await UpdateObjectsFromBuckets(checkpoint);
        if (!valid)
        {
            Console.WriteLine("Not at a consistent checkpoint - cannot update local db");
            return new SyncLocalDatabaseResult
            {
                Ready = false,
                CheckpointValid = true
            };
        }

        await ForceCompact();

        return new SyncLocalDatabaseResult
        {
            Ready = true,
            CheckpointValid = true
        };
    }

    private async Task<bool> UpdateObjectsFromBuckets(Checkpoint checkpoint)
    {
        return await db.WriteTransaction(async tx =>
        {
            var result = await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
                                           ["sync_local", ""]);

            return result.InsertId == 1;
        });
    }

    private record ResultResult(object result);

    public class ResultDetail
    {
        [JsonProperty("valid")]
        public bool Valid { get; set; }

        [JsonProperty("failed_buckets")]
        public List<string>? FailedBuckets { get; set; }
    }

    public async Task<SyncLocalDatabaseResult> ValidateChecksums(
        Checkpoint checkpoint)
    {
        var result = await db.Get<ResultResult>("SELECT powersync_validate_checkpoint(?) as result",
                [JsonConvert.SerializeObject(checkpoint)]);

        Console.WriteLine("validateChecksums result item: " + result);


        if (result == null) return new SyncLocalDatabaseResult { CheckpointValid = false, Ready = false };

        var resultDetail = JsonConvert.DeserializeObject<ResultDetail>(result.result.ToString() ?? "{}");

        if (resultDetail?.Valid == true)
        {
            return new SyncLocalDatabaseResult { Ready = true, CheckpointValid = true };
        }
        else
        {
            return new SyncLocalDatabaseResult
            {
                CheckpointValid = false,
                Ready = false,
                CheckpointFailures = resultDetail?.FailedBuckets?.ToArray() ?? []
            };
        }
    }

    /// <summary>
    /// Force a compact operation, primarily for testing purposes.
    /// </summary>
    public async Task ForceCompact()
    {
        compactCounter = COMPACT_OPERATION_INTERVAL;
        pendingBucketDeletes = true;

        await AutoCompact();
    }

    public async Task AutoCompact()
    {
        await DeletePendingBuckets();
        await ClearRemoveOps();
    }

    private async Task DeletePendingBuckets()
    {
        if (!pendingBucketDeletes) return;

        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES (?, ?)",
                ["delete_pending_buckets", ""]);
        });

        pendingBucketDeletes = false;
    }

    private async Task ClearRemoveOps()
    {
        if (compactCounter < COMPACT_OPERATION_INTERVAL) return;

        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES (?, ?)",
                ["clear_remove_ops", ""]);
        });

        compactCounter = 0;
    }

    private record TargetOpResult(string target_op);
    public async Task<bool> UpdateLocalTarget(Func<Task<string>> callback)
    {
        var rs1 = await db.GetAll<TargetOpResult>(
                "SELECT target_op FROM ps_buckets WHERE name = '$local' AND target_op = CAST(? as INTEGER)",
                [GetMaxOpId()]);

        if (rs1.Length == 0) return false;

        string opId = await callback();
        Console.WriteLine($"[updateLocalTarget] Updating target to checkpoint {opId}");

        return await db.WriteTransaction(async tx =>
        {
            var result = await tx.Execute("UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
                [opId]);

            Console.WriteLine("[updateLocalTarget] Response: " + JsonConvert.SerializeObject(result));
            return true;
        });
    }

    /// <summary>
    /// Get a batch of objects to send to the server.
    /// When the objects are successfully sent to the server, call .Complete().
    /// </summary>
    public async Task<CrudBatch?> GetCrudBatch(int limit = 100)
    {
        if (!await HasCrud())
        {
            return null;
        }

        var crudResult = await db.GetAll<CrudEntryJSON>("SELECT * FROM ps_crud ORDER BY id ASC LIMIT ?", [limit]);

        var all = crudResult.Select(CrudEntry.FromRow).ToArray();

        if (all.Length == 0)
        {
            return null;
        }

        var last = all[all.Length - 1];

        return new CrudBatch(
        Crud: all,
        HaveMore: true,
        Complete: async (string? writeCheckpoint) =>
        {
            await db.WriteTransaction(async tx =>
            {
                await tx.Execute("DELETE FROM ps_crud WHERE id <= ?", [last.ClientId]);

                if (!string.IsNullOrEmpty(writeCheckpoint))
                {
                    var crudResult = await tx.Execute("SELECT 1 FROM ps_crud LIMIT 1");
                    if (crudResult.Rows?.Length > 0)
                    {
                        await tx.Execute(
                            "UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
                            [writeCheckpoint]);
                    }
                }
                else
                {
                    await tx.Execute(
                        "UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
                        [GetMaxOpId()]);
                }
            });
        }
    );
    }

    public async Task<CrudEntry?> NextCrudItem()
    {
        var next = await db.GetOptional<CrudEntryJSON>("SELECT * FROM ps_crud ORDER BY id ASC LIMIT 1");

        return next != null ? CrudEntry.FromRow(next) : null;
    }

    public async Task<bool> HasCrud()
    {
        return await db.GetOptional<object>("SELECT 1 as ignore FROM ps_crud LIMIT 1") != null;
    }

    public async Task SetTargetCheckpoint(Checkpoint checkpoint)
    {
        // No Op
    }
}
