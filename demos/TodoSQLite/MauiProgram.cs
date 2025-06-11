using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TodoSQLite.Data;
using TodoSQLite.Views;

namespace TodoSQLite;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
		builder.Logging.SetMinimumLevel(LogLevel.Debug);
		builder.Logging.AddDebug();
		builder.Services.AddSingleton<TodoItemDatabase>();
		builder.Services.AddTransient<ListsPage>();
		builder.Services.AddTransient<TodoListPage>();

		return builder.Build();
	}
}
