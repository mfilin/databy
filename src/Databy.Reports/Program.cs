using Avalonia;
using Databy.Reports.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Databy.Reports;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationLogging.Configure();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            ApplicationLogging.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
