namespace Common.Client.Sync.Bucket;


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Common.DB;
using Common.DB.Crud;

// public class SqliteBucketStorage(IDBAdapter db) : IBucketStorageAdapter
// {
//     private readonly IDBAdapter db = db;
//     private bool hasCompletedSync = false;
//     private bool pendingBucketDeletes = true;
//     private readonly HashSet<string> _tableNames = new();
//     private Task<string>? _clientIdTask;

//     private static int COMPACT_OPERATION_INTERVAL = 1000;
//     private int compactCounter = COMPACT_OPERATION_INTERVAL;

//     public async Task Init()
//     {
//         hasCompletedSync = false;
//         var existingTableRows = await db.ReadTransaction(async tx =>
//             await tx.GetAll<Dictionary<string, string>>(
//                 "SELECT name FROM sqlite_master WHERE type='table' AND name GLOB 'ps_data_*'"));

//         foreach (var row in existingTableRows ?? new List<Dictionary<string, string>>())
//         {
//             _tableNames.Add(row["name"]);
//         }
//     }

//     public void Dispose()
//     {
//         Console.WriteLine("Disposing SqliteBucketStorage...");
//     }

//     private async Task<string> _GetClientId()
//     {
//         var row = await db.ReadTransaction(async tx =>
//             await tx.Get<Dictionary<string, string>>("SELECT powersync_client_id() as client_id"));
//         return row["client_id"];
//     }

//     public Task<string> GetClientId()
//     {
//         return _clientIdTask ??= _GetClientId(

//         );
//     }

//     public string GetMaxOpId()
//     {
//         return "MAX_OP_ID"; // Placeholder for MAX_OP_ID constant.
//     }

//     public void StartSession() { }

//     public async Task<List<BucketState>> GetBucketStates()
//     {
//         return await db.ReadTransaction(async tx =>
//             await tx.GetAll<BucketState>(
//                 "SELECT name as bucket, cast(last_op as TEXT) as op_id FROM ps_buckets WHERE pending_delete = 0 AND name != '$local'"));
//     }

//     public async Task SaveSyncData(SyncDataBatch batch)
//     {
//         // Func<ITransaction, Task<T>>
//         Func<ITransaction, Task<T>> x = async <T>(ITransaction tx) =>
//         {
//             int count = 0;
//             foreach (var b in batch.Buckets)
//             {
//                 await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
//                     new object[] { "save", JsonSerializer.Serialize(new { buckets = new[] { b.ToJSON() } }) });
//                 Console.WriteLine("saveSyncData: Saved batch.");
//                 count += b.Data.Count;
//             }
//             compactCounter += count;
//         };

//         await db.WriteTransaction(async tx =>
//         {
//             int count = 0;
//             foreach (var b in batch.Buckets)
//             {
//                 await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
//                     new object[] { "save", JsonSerializer.Serialize(new { buckets = new[] { b.ToJSON() } }) });
//                 Console.WriteLine("saveSyncData: Saved batch.");
//                 count += b.Data.Count;
//             }
//             compactCounter += count;
//             return null;
//         });
//         // await db.WriteTransaction(async tx =>
//         // {
//         //     int count = 0;
//         //     foreach (var b in batch.Buckets)
//         //     {
//         //         await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
//         //             new object[] { "save", JsonSerializer.Serialize(new { buckets = new[] { b.ToJSON() } }) });
//         //         Console.WriteLine("saveSyncData: Saved batch.");
//         //         count += b.Data.Count;
//         //     }
//         //     compactCounter += count;
//         // });
//     }

//     public async Task RemoveBuckets(List<string> buckets)
//     {
//         foreach (var bucket in buckets)
//         {
//             await DeleteBucket(
//                 bucket);
//         }
//     }

//     private async Task DeleteBucket(string bucket)
//     {
//         await db.WriteTransaction(async tx =>
//         {
//             await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES(?, ?)",
//                 new object[] { "delete_bucket", bucket });
//         });

//         Console.WriteLine("Done deleting bucket.");
//         pendingBucketDeletes = true;
//     }

//     public async Task<bool> HasCompletedSync()
//     {
//         if (hasCompletedSync) return true;

//         var result = await db.ReadTransaction(async tx =>
//             await tx.Get<Dictionary<string, string>>("SELECT powersync_last_synced_at() as synced_at"));

//         hasCompletedSync = result["synced_at"] != null;
//         return hasCompletedSync;
//     }

//     public async Task<SyncLocalDatabaseResult> SyncLocalDatabase(Checkpoint checkpoint)
//     {
//         var validation = await ValidateChecksums(
//             checkpoint);
//         if (!validation.CheckpointValid)
//         {
//             Console.WriteLine($"Checksums failed for: {JsonSerializer.Serialize(validation.CheckpointFailures)}");
//             foreach (var failedBucket in validation.CheckpointFailures ?? new List<string>())
//             {
//                 await DeleteBucket(
//                     failedBucket);
//             }
//             return new SyncLocalDatabaseResult { Ready = false, CheckpointValid = false, CheckpointFailures = validation.CheckpointFailures };
//         }

//         await db.WriteTransaction(async tx =>
//         {
//             await tx.Execute(
//                 "UPDATE ps_buckets SET last_op = ? WHERE name IN (SELECT json_each.value FROM json_each(?))",
//                 new object[] { checkpoint.LastOpId, JsonSerializer.Serialize(checkpoint.Buckets.Select(b => b.Bucket)) });

//             if (checkpoint.WriteCheckpoint != null)
//             {
//                 await tx.Execute("UPDATE ps_buckets SET last_op = ? WHERE name = '$local'",
//                     new object[] { checkpoint.WriteCheckpoint });
//             }
//         });

//         return new SyncLocalDatabaseResult { Ready = true, CheckpointValid = true };
//     }

//     public async Task<SyncLocalDatabaseResult> ValidateChecksums(
//         Checkpoint checkpoint)
//     {
//         var result = await db.ReadTransaction(async tx =>
//             await tx.Get<Dictionary<string, string>>("SELECT powersync_validate_checkpoint(?) as result",
//                 new object[] { JsonSerializer.Serialize(checkpoint) }));

//         Console.WriteLine("validateChecksums result item: " + JsonSerializer.Serialize(result));

//         if (result == null) return new SyncLocalDatabaseResult { CheckpointValid = false, Ready = false };

//         var parsedResult = JsonSerializer.Deserialize<Dictionary<string, object>>(result["result"]);

//         return parsedResult != null && parsedResult["valid"].ToString() == "true"
//             ? new SyncLocalDatabaseResult { Ready = true, CheckpointValid = true }
//             : new SyncLocalDatabaseResult
//             {
//                 CheckpointValid = false,
//                 Ready = false,
//                 CheckpointFailures = JsonSerializer.Deserialize<List<string>>(parsedResult["failed_buckets"].ToString() ?? "[]")
//             };
//     }

//     /// <summary>
//     /// Force a compact operation, primarily for testing purposes.
//     /// </summary>
//     public async Task ForceCompact()
//     {
//         compactCounter = COMPACT_OPERATION_INTERVAL;
//         pendingBucketDeletes = true;

//         await AutoCompact();
//     }

//     public async Task AutoCompact()
//     {
//         await DeletePendingBuckets();
//         await ClearRemoveOps();
//     }

//     private async Task DeletePendingBuckets()
//     {
//         if (!pendingBucketDeletes) return;

//         await db.WriteTransaction(async tx =>
//         {
//             await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES (?, ?)",
//                 new object[] { "delete_pending_buckets", "" });
//         });

//         pendingBucketDeletes = false;
//     }

//     private async Task ClearRemoveOps()
//     {
//         if (compactCounter < COMPACT_OPERATION_INTERVAL) return;

//         await db.WriteTransaction(async tx =>
//         {
//             await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES (?, ?)",
//                 new object[] { "clear_remove_ops", "" });
//         });

//         compactCounter = 0;
//     }

//     public async Task<bool> UpdateLocalTarget(Func<Task<string>> callback)
//     {
//         var rs1 = await db.ReadTransaction(async tx =>
//             await tx.GetAll<Dictionary<string, string>>(
//                 "SELECT target_op FROM ps_buckets WHERE name = '$local' AND target_op = CAST(? as INTEGER)",
//                 new object[] { GetMaxOpId() }));

//         if (!rs1.Any()) return false;

//         string opId = await callback();
//         Console.WriteLine($"[updateLocalTarget] Updating target to checkpoint {opId}");

//         return await db.WriteTransaction(async tx =>
//         {
//             var result = await tx.Execute("UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
//                 new object[] { opId });

//             Console.WriteLine("[updateLocalTarget] Response: " + JsonSerializer.Serialize(result));
//             return true;
//         });
//     }

//     /// <summary>
//     /// Get a batch of objects to send to the server.
//     /// When the objects are successfully sent to the server, call .Complete().
//     /// </summary>
//     public async Task<CrudBatch?> GetCrudBatchAsync(int limit = 100)
//     {
//         if (!await HasCrud())
//         {
//             return null;
//         }

//         var crudResult = await db.ReadTransaction(async tx =>
//             await tx.GetAll<CrudEntryJSON>("SELECT * FROM ps_crud ORDER BY id ASC LIMIT ?", new object[] { limit }));

//         var all = crudResult.Select(CrudEntry.FromRow).ToList();

//         if (all.Count == 0)
//         {
//             return null;
//         }

//         var last = all[^1]; // Equivalent to `all[all.Count - 1

//         return new CrudBatch(
//         Crud: all,
//         HaveMore: true,
//         Complete: async (string? writeCheckpoint) =>
//         {
//             await db.WriteTransaction(async tx =>
//             {
//                 await tx.Execute("DELETE FROM ps_crud WHERE id <= ?", new object[] { last.ClientId });

//                 if (!string.IsNullOrEmpty(writeCheckpoint))
//                 {
//                     var crudResult = await tx.Execute("SELECT 1 FROM ps_crud LIMIT 1");
//                     if (crudResult.Rows?.Length > 0)
//                     {
//                         await tx.Execute(
//                             "UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
//                             new object[] { writeCheckpoint });
//                     }
//                 }
//                 else
//                 {
//                     await tx.Execute(
//                         "UPDATE ps_buckets SET target_op = CAST(? as INTEGER) WHERE name='$local'",
//                         new object[] { GetMaxOpId() });
//                 }
//             });
//         }
//     );
//     }

//     public async Task<CrudEntry?> NextCrudItem()
//     {
//         var next = await db.ReadTransaction(async tx =>
//             await tx.GetOptional<CrudEntryJSON>("SELECT * FROM ps_crud ORDER BY id ASC LIMIT 1"));

//         return next != null ? CrudEntry.FromRow(next) : null;
//     }

//     public record HasPSCrudResult(string? ps_crud);
//     public async Task<bool> HasCrud()
//     {
//         return await db.ReadTransaction(async tx =>
//             await tx.GetOptional<object>("SELECT 1 FROM ps_crud LIMIT 1")) != null;
//     }

//     public async Task SetTargetCheckpoint(Checkpoint checkpoint)
//     {
//         // No Op
//     }
// }
