using Databy.Reports.Services;
using Serilog;
using Serilog.Filters;

namespace Databy.Reports.Composition;

public static class ApplicationLogging
{
    private const string GeneralLogTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    private const string AuditLogTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {User} | {Message:lj}{NewLine}{Exception}";

    public static void Configure(string? baseDirectory = null)
    {
        var logDirectory = ResolveLogDirectory(baseDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "databy-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: GeneralLogTemplate)
            .WriteTo.Logger(audit => audit
                .Filter.ByIncludingOnly(Matching.WithProperty("Audit"))
                .WriteTo.File(
                    Path.Combine(logDirectory, "audit-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    shared: true,
                    outputTemplate: AuditLogTemplate))
            .CreateLogger();

        Log.Information("Application started. Log directory: {LogDirectory}", logDirectory);
    }

    public static void Shutdown() => Log.CloseAndFlush();

    private static string ResolveLogDirectory(string? baseDirectory)
    {
        var configured = new ApplicationSettings(baseDirectory).LogDirectory;
        try
        {
            Directory.CreateDirectory(configured);
            return configured;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = Path.Combine(Path.GetTempPath(), "Databy", "logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
