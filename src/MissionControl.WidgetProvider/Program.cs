using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace MissionControl.WidgetProvider;

internal static class Program
{
    [MTAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("-RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ComWrappersSupport.InitializeComWrappers();
        using var registration = WidgetProviderRegistration.Register();
        WidgetProviderLifetime.WaitForLastWidgetToBeRemoved();
    }
}
