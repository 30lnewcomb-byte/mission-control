# Native Windows Widgets provider

The Windows Widgets-board provider is a packaged Win32 app in `src/MissionControl.WidgetProvider`. The Windows Widgets host starts the out-of-process COM server when it needs the provider.

The card uses `MissionControl.Core.LaunchWidgetPayloadBuilder`, `LaunchLibrary2Client`, and `CountdownEngine` shared with the desktop dashboard. It exposes a manual refresh action, refreshes about every ten minutes while activated, and retains last-known schedule data if refresh fails.

**Status trust boundary:** LL2 provides schedule metadata only. Its “Launch in Flight” label isn't sufficient to start T+. The card uses deterministic countdown logic and only emits an open action for HTTPS links on `ll.thespacedevs.com`.

## Build

On Windows with the .NET 8 SDK and Windows App SDK prerequisites:

```powershell
dotnet test tests/MissionControl.Core.Tests/MissionControl.Core.Tests.csproj --configuration Release
dotnet build src/MissionControl.WidgetProvider/MissionControl.WidgetProvider.csproj --configuration Release
```

A successful build alone does not register the widget in the Widgets board. Widget providers must be packaged as MSIX; local testing also requires a certificate matching the package identity. Sideloading may require Windows Developer Mode. This feature branch is not merged into the desktop preview until the provider builds and package/install behavior is verified.

References: [Microsoft C# widget provider guide](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs), [provider manifest schema](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-provider-manifest), [Windows Widgets samples](https://learn.microsoft.com/en-us/samples/microsoft/windowsappsdk-samples/widgets/).
