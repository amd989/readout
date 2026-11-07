namespace Readout.Services;

/// <summary>
/// The main thread, for work that must run there. MAUI's <see cref="MainThread"/> works on every
/// platform but Linux, where it throws: there the application's dispatcher, which the GTK
/// backend provides, posts to GTK's main loop.
/// </summary>
public static class UiThread
{
    public static void Post(Action action)
    {
#if LINUX
        if (Application.Current?.Dispatcher is { } dispatcher) dispatcher.Dispatch(action);
        else action(); // before the app exists there's no UI to protect
#else
        MainThread.BeginInvokeOnMainThread(action);
#endif
    }

    public static Task<T> InvokeAsync<T>(Func<T> work) =>
#if LINUX
        Dispatcher().DispatchAsync(work);
#else
        MainThread.InvokeOnMainThreadAsync(work);
#endif

    public static Task<T> InvokeAsync<T>(Func<Task<T>> work) =>
#if LINUX
        Dispatcher().DispatchAsync(work);
#else
        MainThread.InvokeOnMainThreadAsync(work);
#endif

#if LINUX
    private static IDispatcher Dispatcher() =>
        Application.Current?.Dispatcher ?? throw new InvalidOperationException("The app isn't running yet.");
#endif
}
