using Microsoft.Windows.Widgets.Providers;
using System.Runtime.InteropServices;

namespace MissionControl.WidgetProvider;

internal sealed class WidgetProviderRegistration : IDisposable
{
    private readonly uint _cookie;
    private bool _disposed;

    private WidgetProviderRegistration(uint cookie)
    {
        _cookie = cookie;
    }

    public static WidgetProviderRegistration Register()
    {
        var classId = typeof(MissionControlWidgetProvider).GUID;
        var factory = new WidgetProviderFactory<MissionControlWidgetProvider>();
        var result = CoRegisterClassObject(classId, factory, 0x4, 0x1, out var cookie);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return new WidgetProviderRegistration(cookie);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var result = CoRevokeClassObject(_cookie);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid classId,
        [MarshalAs(UnmanagedType.IUnknown)] object classFactory,
        uint classContext,
        uint registrationFlags,
        out uint registrationCookie);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoRevokeClassObject(uint registrationCookie);
}
