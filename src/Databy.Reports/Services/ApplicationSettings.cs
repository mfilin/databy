using System.Text.Json;

namespace Databy.Reports.Services;

public sealed class ApplicationSettings : IApplicationSettings
{
    public const string ConnectionVariable = "DATABY_CONNECTION";
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=db1;Username=db1;Timeout=10;Command Timeout=60";
    private readonly string _baseDirectory;

    public ApplicationSettings(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        ConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable) ?? ReadConnectionString() ?? DefaultConnectionString;
        CatalogRoot = ResolveCatalogRoot(_baseDirectory);
        LogDirectory = Path.Combine(_baseDirectory, "logs");
    }

    public string ConnectionString { get; }
    public string CatalogRoot { get; }
    public string LogDirectory { get; }

    private string? ReadConnectionString()
    {
        var file = Path.Combine(_baseDirectory, "appsettings.local.json");
        if (!File.Exists(file)) return null;
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        return json.RootElement.TryGetProperty("ConnectionString", out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
    }

    private static string ResolveCatalogRoot(string baseDirectory)
    {
        var direct = Path.Combine(baseDirectory, "catalog");
        if (Directory.Exists(direct) || string.Equals(Path.GetFullPath(baseDirectory), Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            return direct;
        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "catalog");
            if (Directory.Exists(candidate)) return candidate;
        }
        return direct;
    }
}