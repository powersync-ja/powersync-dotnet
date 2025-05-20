namespace PowerSync.Common.Client.Sync.Bucket;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Newtonsoft.Json;

using PowerSync.Common.DB;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;

public class SqliteBucketStorage : EventStream<BucketStorageEvent>, IBucketStorageAdapter
{

    public static readonly string MAX_OP_ID = "9223372036854775807";

    private readonly IDBAdapter db;
    private bool hasCompletedSync;
    private bool pendingBucketDeletes;
    private readonly HashSet<string> tableNames;
    private string? clientId;

    private static readonly int COMPACT_OPERATION_INTERVAL = 1000;
    private int compactCounter = COMPACT_OPERATION_INTERVAL;

    private ILogger logger;

    private CancellationTokenSource updateCts;

    private record ExistingTableRowsResult(string name);

    public SqliteBucketStorage(IDBAdapter db, ILogger? logger = null)
    {
        this.db = db;
        this.logger = logger ?? NullLogger.Instance; ;
        hasCompletedSync = false;
        pendingBucketDeletes = true;
        tableNames = [];

        updateCts = new CancellationTokenSource();

        var _ = Task.Run(() =>
        {
            foreach (var update in db.Listen(updateCts.Token))
            {
                if (update.TablesUpdated != null)
                {
                    var tables = DBAdapterUtils.ExtractTableUpdates(update.TablesUpdated);
                    if (tables.Contains(PSInternalTable.CRUD))
                    {
                        Emit(new BucketStorageEvent { CrudUpdate = true });
                    }
                }
            }
        });
    }

    public async Task Init()
    {

        hasCompletedSync = false;
        var existingTableRows = await db.GetAll<ExistingTableRowsResult>("SELECT name FROM sqlite_master WHERE type='table' AND name GLOB 'ps_data_*'");

        foreach (var row in existingTableRows)
        {
            tableNames.Add(row.name);
        }
    }

    public new void Close()
    {
        updateCts.Cancel();
        base.Close();
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
        return MAX_OP_ID;
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
                var result = await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
                    ["save", JsonConvert.SerializeObject(new { buckets = new[] { b.ToJSON() } })]);
                logger.LogDebug("saveSyncData {message}", JsonConvert.SerializeObject(result));
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

        logger.LogDebug("Done deleting bucket");
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
            logger.LogError("Checksums failed for {failures}", JsonConvert.SerializeObject(validation.CheckpointFailures));
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
            logger.LogDebug("Not at a consistent checkpoint - cannot update local db");
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

        logger.LogDebug("validateChecksums result item {message}", JsonConvert.SerializeObject(result));

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
    private record SequenceResult(int seq);

    public async Task<bool> UpdateLocalTarget(Func<Task<string>> callback)
    {
        var rs1 = await db.GetAll<TargetOpResult>(
            "SELECT target_op FROM ps_buckets WHERE name = '$local' AND target_op = CAST(? as INTEGER)",
            [GetMaxOpId()]
        );

        if (rs1.Length == 0)
        {
            // Nothing to update
            return false;
        }

        var rs = await db.GetAll<SequenceResult>(
            "SELECT seq FROM sqlite_sequence WHERE name = 'ps_crud'"
        );

        if (rs.Length == 0)
        {
            // Nothing to update
            return false;
        }

        int seqBefore = rs[0].seq;
        string opId = await callback();

        logger.LogDebug("[updateLocalTarget] Updating target to checkpoint {message}", opId);

        return await db.WriteTransaction(async tx =>
        {
            var anyData = await tx.Execute("SELECT 1 FROM ps_crud LIMIT 1");
            if (anyData.RowsAffected > 0)
            {
                logger.LogDebug("[updateLocalTarget] ps crud is not empty");
                return false;
            }

            var rsAfter = await tx.GetAll<SequenceResult>(
                "SELECT seq FROM sqlite_sequence WHERE name = 'ps_crud'"
            );

            if (rsAfter.Length == 0)
            {
                throw new Exception("SQLite Sequence should not be empty");
            }

            int seqAfter = rsAfter[0].seq;
            logger.LogDebug("[updateLocalTarget] seqAfter: {seq}", seqAfter);

            if (seqAfter != seqBefore)
            {
                logger.LogDebug("[updateLocalTarget] seqAfter ({seqAfter}) != seqBefore ({seqBefore})", seqAfter, seqBefore);
                return false;
            }

            var response = await tx.Execute(
               "UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
               [opId]
           );

            logger.LogDebug("[updateLocalTarget] Response from updating target_op: {response}", JsonConvert.SerializeObject(response));
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
        CompleteCallback: async (string? writeCheckpoint) =>
        {
            await db.WriteTransaction(async tx =>
            {
                await tx.Execute("DELETE FROM ps_crud WHERE id <= ?", [last.ClientId]);

                if (!string.IsNullOrEmpty(writeCheckpoint))
                {
                    var crudResult = await tx.GetAll<object>("SELECT 1 FROM ps_crud LIMIT 1");
                    if (crudResult?.Length > 0)
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
        await Task.CompletedTask;
    }

    record ControlResult(string? value);
    public async Task<string> Control(string op, object? payload)
    {
        return await db.WriteTransaction(async tx =>
        {
              var result = await tx.Get<ControlResult>("SELECT powersync_control(?, ?)", [op, payload]);
              return "5";
        }); 
    }
}
