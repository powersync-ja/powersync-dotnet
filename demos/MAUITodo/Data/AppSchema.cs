using MAUITodo.Models;

using PowerSync.Common.Attachments;
using PowerSync.Common.DB.Schema;

class AppSchema
{
    public static Table Todos = new Table(typeof(TodoItem));
    public static Table Lists = new Table(typeof(TodoList));
    public static Table Attachments = new Table(typeof(Attachment));

    public static Schema PowerSyncSchema = new Schema(Todos, Lists, Attachments);
}
