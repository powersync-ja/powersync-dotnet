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
        this.logger = logger ?? NullLogger.Instance;
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
        var existingTableRows =
            await db.GetAll<ExistingTableRowsResult>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name GLOB 'ps_data_*'");

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
                logger.LogDebug("[updateLocalTarget] seqAfter ({seqAfter}) != seqBefore ({seqBefore})", seqAfter,
                    seqBefore);
                return false;
            }

            var response = await tx.Execute(
                "UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
                [opId]
            );

            logger.LogDebug("[updateLocalTarget] Response from updating target_op: {response}",
                JsonConvert.SerializeObject(response));
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
    

    record ControlResult(string? r);

    public async Task<string> Control(string op, object? payload)
    {
        return await db.WriteTransaction(async tx =>
        {
            var result = await tx.Get<ControlResult>("SELECT powersync_control(?, ?) AS r", [op, payload]);


            return result.r;
        });
    }
}