using Microsoft.Windows.Widgets.Providers;
using System.Runtime.InteropServices;
using WinRT;

namespace MissionControl.WidgetProvider;

internal static class ComGuids
{
    public const string ClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string Unknown = "00000000-0000-0000-C000-000000000046";
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(ComGuids.ClassFactory)]
internal interface IComClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr outerUnknown, ref Guid interfaceId, out IntPtr instance);

    [PreserveSig]
    int LockServer(bool lockServer);
}

internal sealed class WidgetProviderFactory<TProvider> : IComClassFactory
    where TProvider : IWidgetProvider, new()
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int NoInterface = unchecked((int)0x80004002);

    public int CreateInstance(IntPtr outerUnknown, ref Guid interfaceId, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outerUnknown != IntPtr.Zero)
        {
            return ClassENoAggregation;
        }

        if (interfaceId != typeof(TProvider).GUID && interfaceId != Guid.Parse(ComGuids.Unknown))
        {
            return NoInterface;
        }

        instance = MarshalInspectable<IWidgetProvider>.FromManaged(new TProvider());
        return 0;
    }

    public int LockServer(bool lockServer) => 0;
}
