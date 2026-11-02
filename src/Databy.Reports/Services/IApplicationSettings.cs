namespace Databy.Reports.Services;

public interface IApplicationSettings
{
    string ConnectionString { get; }
    string CatalogRoot { get; }
    string LogDirectory { get; }
}