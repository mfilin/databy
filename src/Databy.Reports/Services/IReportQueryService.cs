using Databy.Reports.Catalog;
using Databy.Reports.Export;

namespace Databy.Reports.Services;

public interface IReportQueryService
{
    Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default);
}

public sealed record ReportQueryResult(ReportPreview Preview, TemporaryCsvStage Stage, long RowCount)
    : IDisposable
{
    public void Dispose() => Stage.Dispose();
}

public sealed record ReportPreview(System.Data.DataTable Table, long TotalRows);
