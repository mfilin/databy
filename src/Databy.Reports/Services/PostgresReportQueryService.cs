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

    public async Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        var routineCommand = BuildCommand(report, parameters);

        await using var connection = new NpgsqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureRoutineExistsAsync(connection, report, cancellationToken);

        await using var command = new NpgsqlCommand(routineCommand.Text, connection)
        {
            CommandTimeout = report.Routine.TimeoutSec
        };
        foreach (var parameter in routineCommand.Parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.NpgsqlType, parameter.Value ?? DBNull.Value);

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

    public static RoutineCommand BuildCommand(ReportDefinition report, IReadOnlyDictionary<string, object?> values)
    {
        if (!RoutineNamePattern().IsMatch(report.Routine.Name))
            throw new InvalidOperationException("Недопустимое имя функции в каталоге");
        if (!string.Equals(report.Routine.Kind, "function", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"routine.kind '{report.Routine.Kind}' пока не поддерживается");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        var parameters = new List<RoutineParameter>();
        foreach (var definition in report.Parameters)
        {
            if (!ParameterNamePattern().IsMatch(definition.Name))
                throw new ArgumentException($"Недопустимое имя параметра '{definition.Name}'", nameof(report));
            if (!seen.Add(definition.Name))
                throw new ArgumentException($"Повторяется имя параметра '{definition.Name}'", nameof(report));

            if (values.TryGetValue(definition.Name, out var value) && value is not null)
            {
                parameters.Add(new RoutineParameter(definition.Name, TypeOf(definition), value));
            }
            else if (definition.Required)
            {
                throw new ArgumentException($"Не задано значение обязательного параметра '{definition.Name}'", definition.Name);
            }
            else
            {
                parameters.Add(new RoutineParameter(definition.Name, TypeOf(definition), null));
            }

            names.Add($"{definition.Name} => @{definition.Name}");
        }

        var text = $"SELECT * FROM {report.Routine.Name}({string.Join(", ", names)})";
        return new RoutineCommand(text, parameters);
    }

    private static NpgsqlDbType TypeOf(ParameterDefinition definition) => definition.Type.ToLowerInvariant() switch
    {
        "date" => NpgsqlDbType.Date,
        "int" => NpgsqlDbType.Integer,
        "decimal" => NpgsqlDbType.Numeric,
        "string" => NpgsqlDbType.Text,
        "bool" => NpgsqlDbType.Boolean,
        _ => throw new NotSupportedException($"Тип параметра '{definition.Type}' пока не поддерживается")
    };

    private static async Task EnsureRoutineExistsAsync(NpgsqlConnection connection, ReportDefinition report, CancellationToken cancellationToken)
    {
        await using var check = new NpgsqlCommand(
            "SELECT p.proargnames FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=@schema AND p.proname=@name AND p.prokind='f'",
            connection);
        var parts = report.Routine.Name.Split('.');
        check.Parameters.AddWithValue("schema", parts[0]);
        check.Parameters.AddWithValue("name", parts[1]);
        var result = await check.ExecuteScalarAsync(cancellationToken);
        if (result is null)
            throw new InvalidOperationException($"Функция {report.Routine.Name} отсутствует в PostgreSQL");

        var argumentNames = result is DBNull ? [] : (string[])result;
        var missing = report.Parameters.Select(parameter => parameter.Name)
            .Where(name => !argumentNames.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Функция {report.Routine.Name} не содержит параметров: {string.Join(", ", missing)}");
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*\.[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex RoutineNamePattern();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex ParameterNamePattern();
}

public sealed record RoutineCommand(string Text, IReadOnlyList<RoutineParameter> Parameters);

public sealed record RoutineParameter(string Name, NpgsqlDbType NpgsqlType, object? Value);
