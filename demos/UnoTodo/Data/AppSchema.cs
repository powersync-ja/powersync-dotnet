using PowerSync.Common.Attachments;
using PowerSync.Common.DB.Schema;

namespace UnoTodo.Data;

class AppSchema
{
    public static Table Todos = new(typeof(TodoItem));
    public static Table Lists = new(typeof(TodoList));
    public static Table Attachments = new(typeof(Attachment));

    public static Schema PowerSyncSchema = new(Todos, Lists, Attachments);
}
