namespace PowerSync.Common.Tests.Client.Sync;

using PowerSync.Common.Client;
using PowerSync.Common.Tests.Utils.Sync;


/// <summary>
/// dotnet test -v n --framework net8.0 --filter "SyncTests"
/// </summary>
public class SyncTests : IAsyncLifetime
{

    MockSyncService syncService = null!;
    PowerSyncDatabase db = null!;

    public async Task InitializeAsync()
    {
        syncService = new MockSyncService();
        db = syncService.CreateDatabase();
    }

    public async Task DisposeAsync()
    {
        syncService.Close();
        await db.DisconnectAndClear();
        await db.Close();
    }

    private readonly string[] syncInitialWithListCreation = [
            """{ "checkpoint":{ "last_op_id":"1","buckets":[{ "bucket":"global[]","checksum":1185818774,"count":1,"priority":3,"subscriptions":[{ "default":0}]}],"streams":[{ "name":"global","is_default":true,"errors":[]}]} }""",
            """{ "data":{ "bucket":"global[]","after":"0","has_more":false,"data":[{ "op_id":"1","op":"PUT","object_type":"lists","object_id":"03eeea1d-e395-4380-b047-113266239d46","checksum":1185818774,"subkey":"6979f7218c196e459f53b5be/af847f09-fe3b-50d3-b8e7-8c9ae65abebb","data":"{\"id\":\"03eeea1d-e395-4380-b047-113266239d46\",\"created_at\":\"2026-01-28 11:47:26Z\",\"name\":\"test_list\",\"owner_id\":\"3b36441e-f9c5-4fa0-a501-31e47244b3b2\"}"}],"next_after":"1"} }""",
            """{ "checkpoint_complete":{ "last_op_id":"1"} }"""
        ];

    private readonly string[] syncListDeletion = [
            """{"checkpoint_diff":{"last_op_id":"2","removed_buckets":[],"updated_buckets":[{"bucket":"global[]","checksum":1076933015,"count":2,"priority":3,"subscriptions":[{"default":0}]}]}}""",
            """{"data":{"bucket":"global[]","after":"1","has_more":false,"data":[{"op_id":"2","op":"REMOVE","object_type":"lists","object_id":"03eeea1d-e395-4380-b047-113266239d46","checksum":4186081537,"subkey":"6979f7218c196e459f53b5be/af847f09-fe3b-50d3-b8e7-8c9ae65abebb","data":null}],"next_after":"2"}}""",
            """{ "checkpoint_complete":{ "last_op_id":"2"} }"""
        ];

    [Fact]
    public async Task SyncCreateOperationTest()
    {
        await db.Connect(new TestConnector());

        foreach (var line in syncInitialWithListCreation)
        {
            syncService.PushLine(line);
        }

        await db.WaitForFirstSync();

        var result = await db.GetAll<dynamic>("SELECT * FROM lists");
        Assert.Single(result);
        Assert.Equal("test_list", (string)result[0].name);
    }

    [Fact]
    public async Task SyncDeleteOperationTest()
    {
        await db.Connect(new TestConnector());

        foreach (var line in syncInitialWithListCreation)
        {
            syncService.PushLine(line);
        }

        await db.WaitForFirstSync();

        foreach (var line in syncListDeletion)
        {
            syncService.PushLine(line);
        }

        await Task.Delay(500); // Wait for sync to process

        var result = await db.GetAll<dynamic>("SELECT * FROM lists");
        Assert.Empty(result);
    }

    [Fact]
    public async Task SyncLocalCreateOperationTest()
    {
        string[] syncInitialEmpty = [
              """{"checkpoint":{"last_op_id":"0","buckets":[{"bucket":"global[]","count":0,"checksum":0,"priority":3,"subscriptions":[{"default":0}]}],"streams":[{"name":"global","is_default":true,"errors":[]}]}}""",
              """{"checkpoint_complete":{"last_op_id":"0"}}"""
        ];

        string[] syncAfterLocalCreate = [
            """{"checkpoint_diff":{"last_op_id":"1","removed_buckets":[],"updated_buckets":[{"bucket":"global[]","checksum":1494650203,"count":1,"priority":3,"subscriptions":[{"default":0}]}]}}""",
            """{"data":{"bucket":"global[]","after":"0","has_more":false,"data":[{"op_id":"1","op":"PUT","object_type":"lists","object_id":"16029af5-3f4d-46c4-8d43-6d45595bfc51","checksum":1494650203,"subkey":"6979fe8a58c3cc977bebb4e7/f76d6b5f-2b96-5634-9424-aab506f8fe80","data":"{\"id\":\"16029af5-3f4d-46c4-8d43-6d45595bfc51\",\"created_at\":\"2026-01-28 12:19:35Z\",\"name\":\"New User\",\"owner_id\":\"78bb787c-ff0b-41b2-a297-6a7701648f4a\"}"}],"next_after":"1"}}""",
            """{"checkpoint_complete":{"last_op_id":"1"}}"""
        ];

        await db.Connect(new TestConnector());

        foreach (var line in syncInitialEmpty)
        {
            syncService.PushLine(line);
        }

        await db.WaitForFirstSync();

        await db.Execute("insert into lists (id, name, owner_id, created_at) values (uuid(), 'New User', ?, datetime())", ["78bb787c-ff0b-41b2-a297-6a7701648f4a"]);

        await Task.Delay(500); // Wait for local change to be registered
        Assert.Null(db.CurrentStatus.DataFlowStatus.UploadError);

        foreach (var line in syncAfterLocalCreate)
        {
            syncService.PushLine(line);
        }

        await Task.Delay(500); // Wait for sync to process
        Assert.Null(db.CurrentStatus.DataFlowStatus.DownloadError);
    }

}
