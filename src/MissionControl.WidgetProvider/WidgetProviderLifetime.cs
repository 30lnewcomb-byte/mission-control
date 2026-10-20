namespace MissionControl.WidgetProvider;

internal static class WidgetProviderLifetime
{
    private static readonly object Sync = new();
    private static readonly ManualResetEventSlim FirstWidgetCreated = new(false);
    private static readonly ManualResetEventSlim NoWidgetsRemain = new(true);
    private static int _widgetCount;

    public static void WidgetCreated()
    {
        lock (Sync)
        {
            _widgetCount++;
            FirstWidgetCreated.Set();
            NoWidgetsRemain.Reset();
        }
    }

    public static void WidgetDeleted()
    {
        lock (Sync)
        {
            if (_widgetCount == 0)
            {
                return;
            }

            _widgetCount--;
            if (_widgetCount == 0)
            {
                NoWidgetsRemain.Set();
            }
        }
    }

    public static void WaitForLastWidgetToBeRemoved()
    {
        FirstWidgetCreated.Wait();
        NoWidgetsRemain.Wait();
    }
}
