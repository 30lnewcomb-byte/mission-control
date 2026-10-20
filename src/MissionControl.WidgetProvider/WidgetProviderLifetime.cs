namespace MissionControl.WidgetProvider;

internal static class WidgetProviderLifetime
{
    private static readonly ManualResetEventSlim FirstWidgetCreated = new(false);
    private static readonly ManualResetEventSlim NoWidgetsRemain = new(true);
    private static int _widgetCount;

    public static void WidgetCreated()
    {
        Interlocked.Increment(ref _widgetCount);
        FirstWidgetCreated.Set();
        NoWidgetsRemain.Reset();
    }

    public static void WidgetDeleted()
    {
        var remaining = Interlocked.Decrement(ref _widgetCount);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _widgetCount, 0);
            NoWidgetsRemain.Set();
        }
    }

    public static void WaitForLastWidgetToBeRemoved()
    {
        FirstWidgetCreated.Wait();
        NoWidgetsRemain.Wait();
    }
}
