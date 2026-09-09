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

public class SqliteBucketStorage : IBucketStorageAdapter
{
    public BucketStorageEvents Events { get; } = new();

    private readonly IDBAdapter db;

    private string? clientId;

    private readonly ILogger logger;

    private readonly CancellationTokenSource updateCts;
    private readonly Task updateTask;

    public SqliteBucketStorage(IDBAdapter db, ILogger? logger = null)
    {
        this.db = db;
        this.logger = logger ?? NullLogger.Instance;

        updateCts = new CancellationTokenSource();

        updateTask = Task.Run(() =>
        {
            foreach (var update in db.Events.OnTablesUpdated.Listen(updateCts.Token))
            {
                var tables = DBAdapterUtils.ExtractTableUpdates(update.TablesUpdated);
                if (tables.Contains(PSInternalTable.CRUD))
                {
                    Events.Emit(new BucketStorageEvents.CrudUpdateEvent());
                }
            }
        });
    }

    public void Close()
    {
        updateCts.Cancel();
        try { updateTask.Wait(2000); } catch (Exception) { }
        Events.Close();
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

    /// <summary>
    /// Reads or updates the stored checkpoint request id.
    /// </summary>
    public Task<long?> ReadOrUpdateCheckpoint(string variant, long? update = null)
        => db.WriteTransaction(tx => ReadOrUpdateCheckpoint(tx, variant, update));

    /// <summary>
    /// Reads or updates the stored checkpoint request id using the given transaction.
    /// </summary>
    public static Task<long?> ReadOrUpdateCheckpoint(ITransaction tx, string variant, long? payload = null)
    {
        return tx.Get<long?>(
            "SELECT powersync_control(?, ?) AS r",
            [$"{variant}_checkpoint_request_id", payload]);
    }

    // This is called within existing transactions, therefore accept an ITransaction instead of creating a new one
    private static Task<long?> TargetCheckpointRequestId(ITransaction tx, long? update = null)
        => ReadOrUpdateCheckpoint(tx, "target", update);

    private record ResultResult(object result);

    public class ResultDetail
    {
        [JsonProperty("valid")]
        public bool Valid { get; set; }

        [JsonProperty("failed_buckets")]
        public List<string>? FailedBuckets { get; set; }
    }

    private record SequenceResult(long seq);

    public async Task<bool> UpdateLocalTarget(Func<Task<long>> callback)
    {
        var seqBeforeResult = await db.ReadTransaction(async tx =>
        {
            var currentTarget = await TargetCheckpointRequestId(tx);
            if (currentTarget != long.MaxValue)
            {
                // Nothing to update
                return (long?)null;
            }

            var rs = await tx.GetAll<SequenceResult>(
                "SELECT seq FROM main.sqlite_sequence WHERE name = 'ps_crud'"
            );

            return rs.Length == 0 ? null : rs[0].seq;
        });

        if (seqBeforeResult is not { } seqBefore)
        {
            // Nothing to update
            return false;
        }

        long opId = await callback();

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
                "SELECT seq FROM main.sqlite_sequence WHERE name = 'ps_crud'"
            );

            if (rsAfter.Length == 0)
            {
                throw new Exception("SQLite Sequence should not be empty");
            }

            long seqAfter = rsAfter[0].seq;
            logger.LogDebug("[updateLocalTarget] seqAfter: {seq}", seqAfter);

            if (seqAfter != seqBefore)
            {
                logger.LogDebug("[updateLocalTarget] seqAfter ({seqAfter}) != seqBefore ({seqBefore})", seqAfter,
                    seqBefore);
                return false;
            }

            await TargetCheckpointRequestId(tx, opId);
            return true;
        });
    }

    public Task HandleCrudCheckpoint(long lastClientId, long? writeCheckpoint = null)
    {
        return db.WriteTransaction(async tx =>
        {
            await tx.Execute($"DELETE FROM {PSInternalTable.CRUD} WHERE id <= ?", [lastClientId]);

            var crudRemaining = await tx.GetOptional<object>(
                $"SELECT 1 as ignore FROM {PSInternalTable.CRUD} LIMIT 1") != null;

            await TargetCheckpointRequestId(
                tx,
                writeCheckpoint is not null && !crudRemaining ? writeCheckpoint : long.MaxValue);
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
            CompleteCallback: writeCheckpoint => HandleCrudCheckpoint(last.ClientId, writeCheckpoint)
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

    private record ControlResult(string? r);

    public async Task<string> Control(string op, object? payload = null)
    {
        return await db.WriteTransaction(async tx =>
        {
            var result = await tx.Get<ControlResult>("SELECT powersync_control(?, ?) AS r", [op, payload]);
            return result.r!;
        });
    }
}
