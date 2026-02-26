namespace PowerSync.Common.Tests.Utils;

[AttributeUsage(AttributeTargets.Method)]
public class FactAttribute : Xunit.FactAttribute
{
    public FactAttribute()
    {
        Timeout = 5000;
    }
}
