using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Npgsql;
using NpgsqlTypes;

namespace Databy.Reports.Services;

public sealed partial class PostgresReportQueryService : IReportQueryService
{
    public const int PreviewRowLimit = 5000;
    private readonly IApplicationSettings _settings;

    public PostgresReportQueryService(IApplicationSettings settings) => _settings = settings;

    public async Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, ReportFilter filter, CancellationToken cancellationToken = default)
    {
        if (!RoutineNamePattern().IsMatch(report.Routine.Name))
            throw new InvalidOperationException("Недопустимое имя функции в каталоге");
        EnsureSupportedParameters(report);

        await using var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureRoutineExistsAsync(connection, report, cancellationToken);

        await using var command = new NpgsqlCommand($"SELECT * FROM {report.Routine.Name}(@p_from, @p_to)", connection)
        {
            CommandTimeout = report.Routine.TimeoutSec
        };
        command.Parameters.Add("p_from", NpgsqlDbType.Date).Value = filter.From;
        command.Parameters.Add("p_to", NpgsqlDbType.Date).Value = filter.To;

        var table = new DataTable();
        var staged = new TemporaryCsvStage();
        long rowCount = 0;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            for (var c = 0; c < reader.FieldCount; c++)
                table.Columns.Add(reader.GetName(c), Nullable.GetUnderlyingType(reader.GetFieldType(c)) ?? reader.GetFieldType(c));

            await using (var writer = staged.OpenWriter())
            {
                ReportExportWriter.WriteCsvRecord(writer, Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
                while (await reader.ReadAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    ReportExportWriter.WriteCsvRecord(writer, Enumerable.Range(0, reader.FieldCount)
                        .Select(i => reader.IsDBNull(i) ? ReportExportWriter.NullMarker : Convert.ToString(values[i], CultureInfo.InvariantCulture) ?? ""));
                    rowCount++;
                    if (table.Rows.Count < PreviewRowLimit) table.Rows.Add(values);
                }
            }
            return new ReportQueryResult(new ReportPreview(table, rowCount), staged, rowCount);
        }
        catch
        {
            staged.Dispose();
            throw;
        }
    }

    private static void EnsureSupportedParameters(ReportDefinition report)
    {
        var parameters = report.Parameters;
        if (parameters.Count != 2 || parameters[0].Name != "p_from" || parameters[1].Name != "p_to")
            throw new NotSupportedException("В демо-клиенте настроены параметры типа date: p_from и p_to");
    }

    private static async Task EnsureRoutineExistsAsync(NpgsqlConnection connection, ReportDefinition report, CancellationToken cancellationToken)
    {
        await using var check = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=@schema AND p.proname=@name AND p.prokind='f')",
            connection);
        var parts = report.Routine.Name.Split('.');
        check.Parameters.AddWithValue("schema", parts[0]);
        check.Parameters.AddWithValue("name", parts[1]);
        if (!(bool)(await check.ExecuteScalarAsync(cancellationToken))!)
            throw new InvalidOperationException($"Функция {report.Routine.Name} отсутствует в PostgreSQL");
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*\.[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex RoutineNamePattern();
}