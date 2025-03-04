namespace Common.Utils;

using System;
using System.Threading.Tasks;

public class ControlledExecutorOptions
{
    /// <summary>
    /// If throttling is enabled, it ensures only one task runs at a time,
    /// and only one additional task can be scheduled to run after the current task completes.
    /// The pending task will be overwritten by the latest task.
    /// Enabled by default.
    /// </summary>
    public bool? ThrottleEnabled { get; set; }
}

public class ControlledExecutor<T>(Func<T, Task> task, ControlledExecutorOptions? options = null)
{
    private readonly Func<T, Task> task = task;
    private Task? runningTask;
    private T? pendingTaskParam;
    private readonly bool isThrottling = options?.ThrottleEnabled ?? true;
    private bool closed = false;

    public void Schedule(T param)
    {
        if (closed)
        {
            return;
        }

        if (!isThrottling)
        {
            _ = task(param);
            return;
        }

        if (pendingTaskParam != null)
        {
            // set or replace the pending task param with latest one
            pendingTaskParam = param;
            return;
        }

        Execute(param);

    }

    public void Dispose()
    {
        closed = true;
        runningTask = null;
    }

    private async void Execute(T param)
    {
        runningTask = task(param);
        await runningTask;
        runningTask = null;

        if (pendingTaskParam != null)
        {
            var pendingParam = pendingTaskParam;
            pendingTaskParam = default;

            Execute(pendingParam);
        }
    }
}
