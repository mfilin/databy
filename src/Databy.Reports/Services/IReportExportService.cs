using Databy.Reports.Catalog;
using Databy.Reports.Export;

namespace Databy.Reports.Services;

public interface IReportExportService
{
    ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath);
}
