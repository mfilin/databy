using System.Collections.Concurrent;
using Databy.Reports.Services;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Tests.Integration;

/// <summary>Один контейнер PostgreSQL на весь T2-набор.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private static readonly string[] CleanupScripts = ["init.sql", "demo.sql", "pq_functions.sql", "list_functions.sql"];

    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .WithDatabase("reports_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    public string ConnectionString => Container.GetConnectionString();
    public string TempDirectory { get; } = Path.Combine(Path.GetTempPath(), "databy-t2-" + Guid.NewGuid().ToString("N"));
    public IApplicationSettings Settings => new TestApplicationSettings(ConnectionString, CatalogRoot, TempDirectory);
    public string CatalogRoot { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures", "catalog_t2");

    public int StagedFilesOnStart { get; private set; }

    public int StagedFilesOnFinish { get; private set; }

    public async Task InitializeAsync()
    {
        StagedFilesOnStart = TempStageTracker.CountStagedFiles();
        Directory.CreateDirectory(TempDirectory);
        await Container.StartAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var script in CleanupScripts)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "db", script);
            var sql = await File.ReadAllTextAsync(path);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        StagedFilesOnFinish = TempStageTracker.CountStagedFiles();
        await Container.DisposeAsync();
        try { Directory.Delete(TempDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public PostgresReportQueryService CreateService() => new(Settings);
}

/// <summary>Настройки для тестов: строка подключения из контейнера, без env и appsettings.</summary>
public sealed class TestApplicationSettings : IApplicationSettings
{
    private readonly ConnectionSettings _connection;
    private readonly IReadOnlyDictionary<string, ConnectionSettings> _connections;

    public TestApplicationSettings(string connectionString, string catalogRoot, string logDirectory)
    {
        _connection = new ConnectionSettings("main", ConnectionTypes.Postgres, connectionString);
        _connections = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ConnectionSettings>(
            new Dictionary<string, ConnectionSettings>(StringComparer.OrdinalIgnoreCase) { ["main"] = _connection });
        CatalogRoot = catalogRoot;
        LogDirectory = logDirectory;
    }

    public string ConnectionString => _connection.ConnectionString;
    public IReadOnlyDictionary<string, ConnectionSettings> Connections => _connections;
    public string DefaultConnectionName => "main";

    public ConnectionSettings GetConnection(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("main", StringComparison.OrdinalIgnoreCase))
            return _connection;
        throw new InvalidOperationException(
            $"Подключение '{name}' не настроено. Известные подключения: main.");
    }

    public string CatalogRoot { get; }
    public string LogDirectory { get; }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres-t2";
}

internal static class TempStageTracker
{
    public static int CountStagedFiles() =>
        Directory.EnumerateFiles(Path.GetTempPath(), "databy-*.csv").Count();
}
