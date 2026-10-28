namespace Readout.Mcp;

/// <summary>
/// Runs work on the thread that owns the meter session (the app's main thread). MCP requests
/// arrive on worker threads; the session and its log must only be touched from there.
/// </summary>
public interface IUiThread
{
    Task<T> InvokeAsync<T>(Func<T> work);
    Task<T> InvokeAsync<T>(Func<Task<T>> work);
}

/// <summary>Runs work right away, on the calling thread: for tests and single-threaded hosts.</summary>
public sealed class InlineUiThread : IUiThread
{
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.FromResult(work());
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return work();
    }
}
