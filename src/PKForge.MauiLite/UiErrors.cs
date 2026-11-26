namespace PKForge.MauiLite;

/// <summary>
/// Where MauiLite sends an exception from app code it called (a paint handler, a tap handler, posted work).
/// Under kaiHEN an uncaught exception just closes the app with no crash dump, so the frame or loop goes on instead.
/// </summary>
public static class UiErrors
{
    public static event Action<Exception, string>? Reported;

    public static void Report(Exception error, string where)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (Reported is { } handlers)
            handlers(error, where);
        else
            Console.Error.WriteLine($"MauiLite: {where}: {error}");
    }
}
