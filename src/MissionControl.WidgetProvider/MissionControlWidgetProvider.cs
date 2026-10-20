using Microsoft.Windows.Widgets.Providers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace MissionControl.WidgetProvider;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("4CAB3B4A-71D7-4C8E-9C02-2C6D850CE9AF")]
public sealed class MissionControlWidgetProvider : IWidgetProvider
{
    public const string DefinitionId = "MissionControlLaunchWidget";

    private static readonly ConcurrentDictionary<string, LaunchWidgetInstance> Instances =
        new(StringComparer.Ordinal);

    public MissionControlWidgetProvider()
    {
        RecoverExistingWidgets();
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        if (!string.Equals(widgetContext.DefinitionId, DefinitionId, StringComparison.Ordinal))
        {
            WidgetManager.GetDefault().DeleteWidget(widgetContext.Id);
            return;
        }

        var instance = new LaunchWidgetInstance(widgetContext.Id);
        if (Instances.TryAdd(widgetContext.Id, instance))
        {
            WidgetProviderLifetime.WidgetCreated();
            instance.RefreshNow();
        }
        else
        {
            instance.Dispose();
        }
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        if (Instances.TryRemove(widgetId, out var instance))
        {
            instance.Dispose();
            WidgetProviderLifetime.WidgetDeleted();
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        if (string.Equals(actionInvokedArgs.Verb, "refresh", StringComparison.Ordinal) &&
            Instances.TryGetValue(actionInvokedArgs.WidgetContext.Id, out var instance))
        {
            instance.RefreshNow();
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        if (Instances.TryGetValue(contextChangedArgs.WidgetContext.Id, out var instance))
        {
            instance.RefreshNow();
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        if (Instances.TryGetValue(widgetContext.Id, out var instance))
        {
            instance.Activate();
        }
    }

    public void Deactivate(string widgetId)
    {
        if (Instances.TryGetValue(widgetId, out var instance))
        {
            instance.Deactivate();
        }
    }

    private static void RecoverExistingWidgets()
    {
        try
        {
            foreach (var widgetInfo in WidgetManager.GetDefault().GetWidgetInfos())
            {
                var context = widgetInfo.WidgetContext;
                if (!string.Equals(context.DefinitionId, DefinitionId, StringComparison.Ordinal))
                {
                    continue;
                }

                var instance = new LaunchWidgetInstance(context.Id);
                if (Instances.TryAdd(context.Id, instance))
                {
                    WidgetProviderLifetime.WidgetCreated();
                    instance.RefreshNow();
                }
                else
                {
                    instance.Dispose();
                }
            }
        }
        catch (Exception exception) when (
            exception is COMException or InvalidOperationException)
        {
            // The host can call CreateWidget after provider activation; wait for
            // that callback rather than inventing a widget identity.
        }
    }
}
