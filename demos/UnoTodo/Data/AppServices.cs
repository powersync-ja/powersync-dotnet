namespace UnoTodo.Data;

public static class AppServices
{
    public static PowerSyncData PowerSyncData =>
        ((App)Application.Current).Host!.Services.GetRequiredService<PowerSyncData>();
}
