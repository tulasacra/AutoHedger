namespace AutoHedger;

public static class TaskExtensions
{
    // Like Task.ContinueWith(...), but faults propagate (ContinueWith alone can swallow them).
    public static async Task ContinueWithIfOk(this Task task, Action<Task> continuationAction)
    {
        await task;
        continuationAction(task);
    }

    public static async Task<TResult> ContinueWithIfOk<T, TResult>(this Task<T> task, Func<Task<T>, TResult> continuationFunction)
    {
        await task;
        return continuationFunction(task);
    }
}
