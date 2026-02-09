using PowerSync.Common.DB.Schema;

using MAUITodo.Models;

class AppSchema
{
    public static Schema PowerSyncSchema = new Schema(typeof(List), typeof(Todo));
}
