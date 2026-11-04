using Databy.Reports.Catalog;
using Databy.Reports.Export;

namespace Databy.Reports.Services;

public sealed class ReportExportService : IReportExportService
{
    private readonly ReportExportWriter _writer;

    public ReportExportService(ReportExportWriter? writer = null) => _writer = writer ?? new ReportExportWriter();

    public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath)
    {
        if (result.Stage is null || !File.Exists(result.Stage.Path))
            throw new InvalidOperationException("Временная выгрузка отчёта недоступна");
        if (result.Preview.Table.Columns.Count == 0)
            throw new InvalidOperationException("Схема результата отчёта недоступна");
        return _writer.Save(result.Stage.Path, result.Preview.Table, targetPath, report.Result?.Sheet,
            report.Result?.Columns, result.RowCount);
    }
}