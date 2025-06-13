using TodoSQLite.Views;

namespace TodoSQLite;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(TodoListPage), typeof(TodoListPage));
		Routing.RegisterRoute(nameof(SqlConsolePage), typeof(SqlConsolePage));
	}
}
