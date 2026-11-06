using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Databy.Reports;

[assembly: AvaloniaTestApplication(typeof(Tests.Unit.HeadlessTestAppBuilder))]

namespace Tests.Unit;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
