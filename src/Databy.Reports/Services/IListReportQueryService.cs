using Databy.Reports.Catalog;
using Databy.Reports.Input;

namespace Databy.Reports.Services;

/// <summary>Прогресс выполнения отчёта по списку.</summary>
public sealed record ListQueryProgress(long ProcessedRows, long TotalRows, int BatchNumber, int BatchCount);

/// <summary>
/// Отчёт по списку значений: отправляет ключи пакетами и собирает итог
/// (все исходные колонки + колонки БД + статус) в обычный <see cref="ReportQueryResult"/>.
/// Входной <paramref name="input"/> принадлежит вызывающему и не освобождается.
/// </summary>
public interface IListReportQueryService
{
    Task<ReportQueryResult> ExecuteAsync(ReportDefinition report,
        IReadOnlyDictionary<string, object?> parameters,
        InputStage input,
        IProgress<ListQueryProgress>? progress,
        CancellationToken cancellationToken);
}
