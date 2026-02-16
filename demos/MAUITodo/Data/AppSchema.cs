using MAUITodo.Models;

using PowerSync.Common.DB.Schema;

class AppSchema
{
    public static Table Todos = new Table(typeof(TodoItem));
    public static Table Lists = new Table(typeof(TodoList));

    public static Schema PowerSyncSchema = new Schema(Todos, Lists);
}
