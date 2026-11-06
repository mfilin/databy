using Databy.Reports.Catalog;

namespace Databy.Reports.Services;

public sealed class ReportCatalogService : IReportCatalogService
{
    private readonly IApplicationSettings _settings;
    private readonly ReportCatalogLoader _loader;

    public ReportCatalogService(IApplicationSettings settings, ReportCatalogLoader? loader = null)
    {
        _settings = settings;
        _loader = loader ?? new ReportCatalogLoader();
    }

    public ReportCatalog Load() => _loader.Load(_settings.CatalogRoot);
}
