namespace Databy.Reports.Services;

public interface IApplicationSettings
{
    string ConnectionString { get; }
    IReadOnlyDictionary<string, ConnectionSettings> Connections { get; }
    string DefaultConnectionName { get; }
    ConnectionSettings GetConnection(string? name);
    string CatalogRoot { get; }
    string LogDirectory { get; }
}
