using Databy.Reports.Catalog;

namespace Databy.Reports.Services;

public interface IReportAuditService
{
    void ReportStarted(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters);
    void ReportSucceeded(ReportDefinition report, long rowCount, long durationMs);
    void ReportFailed(ReportDefinition report, Exception exception, long durationMs);
    void ReportCanceled(ReportDefinition report, long durationMs);
}

public sealed class NullReportAuditService : IReportAuditService
{
    public static NullReportAuditService Instance { get; } = new();

    public void ReportStarted(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters) { }
    public void ReportSucceeded(ReportDefinition report, long rowCount, long durationMs) { }
    public void ReportFailed(ReportDefinition report, Exception exception, long durationMs) { }
    public void ReportCanceled(ReportDefinition report, long durationMs) { }
}