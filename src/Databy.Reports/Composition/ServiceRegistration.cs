using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Databy.Reports.Composition;

public static class ServiceRegistration
{
    public static IServiceProvider BuildServiceProvider(string? baseDirectory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationSettings>(_ => new ApplicationSettings(baseDirectory));
        services.AddSingleton<ICurrentUser, CurrentUser>();
        services.AddSingleton<IReportAuditService, SerilogReportAuditService>();
        services.AddSingleton<ReportCatalogLoader>();
        services.AddSingleton<IReportCatalogService, ReportCatalogService>();
        services.AddSingleton<IReportQueryService, PostgresReportQueryService>();
        services.AddSingleton<ReportExportWriter>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        services.AddTransient<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
