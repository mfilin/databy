using Databy.Reports.Catalog;
using Serilog;

namespace Databy.Reports.Services;

public sealed class SerilogReportAuditService : IReportAuditService
{
    private readonly string _userName;

    public SerilogReportAuditService(ICurrentUser currentUser)
    {
        _userName = currentUser.Name;
    }

    private ILogger Audit => Log.ForContext("Audit", true).ForContext("User", _userName);

    public void ReportStarted(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters) =>
        Audit.Information("Report started: {ReportCode} ({ReportTitle}); parameters: {Parameters}",
            report.Code, report.Title, FormatParameters(parameters));

    public void ReportSucceeded(ReportDefinition report, long rowCount, long durationMs) =>
        Audit.Information("Report succeeded: {ReportCode}; rows={RowCount}; durationMs={DurationMs}",
            report.Code, rowCount, durationMs);

    public void ReportFailed(ReportDefinition report, Exception exception, long durationMs) =>
        Audit.Error("Report failed: {ReportCode}; error={ErrorType}: {ErrorMessage}; durationMs={DurationMs}",
            report.Code, exception.GetType().Name, exception.Message, durationMs);

    public void ReportCanceled(ReportDefinition report, long durationMs) =>
        Audit.Information("Report canceled: {ReportCode}; durationMs={DurationMs}", report.Code, durationMs);

    private static string FormatParameters(IReadOnlyDictionary<string, object?> parameters) =>
        string.Join(", ", parameters
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value ?? "null"}"));
}
