using System.Data;
using System.Globalization;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Input;
using Npgsql;
using NpgsqlTypes;

namespace Databy.Reports.Services;

/// <summary>
/// Отчёт по списку значений: отправляет ключи пакетами в функцию PostgreSQL
/// и собирает итог (исходные колонки + колонки БД + статус).
/// </summary>
public sealed class ListReportQueryService : IListReportQueryService
{
    private const int PreviewLimit = PostgresReportQueryService.PreviewRowLimit;

    private readonly IApplicationSettings _settings;

    public ListReportQueryService(IApplicationSettings settings) => _settings = settings;

    public async Task<ReportQueryResult> ExecuteAsync(ReportDefinition report,
        IReadOnlyDictionary<string, object?> parameters,
        InputStage input,
        IProgress<ListQueryProgress>? progress,
        CancellationToken cancellationToken)
    {
        var definition = report.Input ?? throw new InvalidOperationException(
            $"Отчёт {report.Code} не описывает список значений (секция input)");

        var staged = new TemporaryCsvStage();
        var table = new DataTable();
        var rowsWritten = 0L;
        var processed = 0L;
        long total;

        try
        {
            total = CountRows(input);
            await using var connection = new NpgsqlConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            var arguments = await RoutineCatalog.LoadArgumentsAsync(connection, report, cancellationToken);
            ValidateContract(report, definition, arguments);

            var sourceColumns = Enumerable.Range(1, input.SourceColumnCount).Select(index => "Колонка " + index).ToArray();
            var merger = new ResultMerger(sourceColumns, []);
            var schemaKnown = false;
            var batchNumber = 0;
            var batchCount = (int)Math.Ceiling(total / (double)definition.BatchSize);

            await using (var writer = staged.OpenWriter())
            {
                var batch = new List<InputRow>(definition.BatchSize);
                foreach (var row in input.ReadRows())
                {
                    batch.Add(row);
                    if (batch.Count < definition.BatchSize) continue;

                    batchNumber++;
                    await FlushBatchAsync(batch, batchNumber);
                    batch.Clear();
                }

                if (batch.Count > 0)
                {
                    batchNumber++;
                    await FlushBatchAsync(batch, batchNumber);
                }

                if (!schemaKnown)
                {
                    var empty = await QueryBatchAsync(connection, report, definition, parameters, [], 1, cancellationToken);
                    merger = new ResultMerger(sourceColumns, empty.Columns);
                    WriteHeader();
                    AddColumns(empty.Columns);
                }

                if (total == 0)
                {
                    progress?.Report(new ListQueryProgress(0, 0, 0, 0));
                }

                return new ReportQueryResult(new ReportPreview(table, rowsWritten), staged, rowsWritten);

                async Task FlushBatchAsync(List<InputRow> rows, int number)
                {
                    var valid = rows.Where(row => row.Error is null).ToArray();
                    var database = await QueryBatchAsync(connection, report, definition, parameters, valid, number, cancellationToken);
                    if (!schemaKnown)
                    {
                        merger = new ResultMerger(sourceColumns, database.Columns);
                        WriteHeader();
                        AddColumns(database.Columns);
                        schemaKnown = true;
                    }
                    else if (!merger.Matches(database.Columns))
                    {
                        throw new InvalidOperationException(
                            $"Функция {report.Routine.Name}: набор колонок пакета {number} отличается от предыдущих");
                    }

                    ValidateKeys(report, database, valid, number);

                    var buffer = new StringWriter(CultureInfo.InvariantCulture);
                    merger.Write(buffer, new BatchResult(rows.ToArray(), database.Rows));
                    foreach (var line in buffer.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        writer.WriteLine(line);
                        rowsWritten++;
                        if (table.Rows.Count < PreviewLimit) AppendPreviewRow(line);
                    }

                    processed += rows.Count;
                    progress?.Report(new ListQueryProgress(processed, total, number, batchCount));
                }

                void WriteHeader() =>
                    ReportExportWriter.WriteCsvRecord(writer, merger.OutputColumns.Append(definition.StatusColumn));

                void AddColumns(string[] databaseColumns)
                {
                    foreach (var column in sourceColumns) table.Columns.Add(column, typeof(string));
                    for (var i = 0; i < databaseColumns.Count(); i++) table.Columns.Add(merger.OutputColumns[sourceColumns.Length + i], typeof(string));
                    table.Columns.Add(definition.StatusColumn, typeof(string));
                }

                void AppendPreviewRow(string line)
                {
                    var values = SplitRecord(line);
                    if (values.Length != table.Columns.Count) return;
                    table.Rows.Add(values.Cast<object>().ToArray());
                }
            }
        }
        catch
        {
            staged.Dispose();
            throw;
        }
    }

    private static void ValidateContract(ReportDefinition report, InputDefinition definition,
        IReadOnlyList<RoutineArgument> arguments)
    {
        foreach (var parameter in report.Parameters)
            RoutineCatalog.RequireInput(report, arguments, parameter.Name, ScalarType(parameter.Type));

        RoutineCatalog.RequireInput(report, arguments, definition.RowIdParameter, "int4[]");

        foreach (var column in definition.Columns)
            RoutineCatalog.RequireInput(report, arguments, column.Name, ArrayType(column.Type));
    }

    private static void ValidateKeys(ReportDefinition report, BatchRows batch, IReadOnlyList<InputRow> source, int batchNumber)
    {
        var keys = batch.Rows.Select(row => row.RowId).ToArray();
        for (var i = 1; i < keys.Length; i++)
        {
            if (keys[i] < keys[i - 1])
                throw new InvalidOperationException(
                    $"Функция {report.Routine.Name}: результат пакета {batchNumber} не отсортирован по row_id (row_id={keys[i]})");
        }

        var sent = source.Select(row => row.RowId).ToArray();
        var position = 0;
        foreach (var key in keys)
        {
            while (position < sent.Length && sent[position] < key) position++;
            if (position >= sent.Length || sent[position] != key)
                throw new InvalidOperationException(
                    $"Функция {report.Routine.Name}: пакет {batchNumber} вернул чужой row_id={key}");
        }
    }

    private async Task<BatchRows> QueryBatchAsync(NpgsqlConnection connection, ReportDefinition report,
        InputDefinition definition, IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyList<InputRow> rows, int batchNumber, CancellationToken cancellationToken)
    {
        var routineCommand = PostgresReportQueryService.BuildCommand(report, parameters);
        var arrays = BuildArrays(definition, rows).ToArray();
        var names = new List<string>();
        foreach (var parameter in report.Parameters) names.Add($"{parameter.Name} => @{parameter.Name}");
        names.Add($"{definition.RowIdParameter} => @{definition.RowIdParameter}");
        foreach (var column in definition.Columns) names.Add($"{column.Name} => @{column.Name}");

        await using var command = new NpgsqlCommand(
            $"SELECT * FROM {report.Routine.Name}({string.Join(", ", names)})", connection)
        {
            CommandTimeout = report.Routine.TimeoutSec
        };
        foreach (var parameter in routineCommand.Parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.NpgsqlType, parameter.Value ?? DBNull.Value);
        foreach (var (name, type, value) in arrays)
            command.Parameters.AddWithValue(name, type, value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<string>(Math.Max(0, reader.FieldCount - 1));
        var keyIndex = -1;
        for (var c = 0; c < reader.FieldCount; c++)
        {
            if (reader.GetName(c) == definition.ResultKey) keyIndex = c;
            else columns.Add(reader.GetName(c));
        }
        if (keyIndex < 0)
            throw new InvalidOperationException(
                $"Функция {report.Routine.Name}: нет колонки результата '{definition.ResultKey}' в пакете {batchNumber}");

        var rowsOut = new List<BatchRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(keyIndex))
                throw new InvalidOperationException(
                    $"Функция {report.Routine.Name}: пустой '{definition.ResultKey}' в пакете {batchNumber}");
            var key = Convert.ToInt32(reader.GetValue(keyIndex), CultureInfo.InvariantCulture);
            var values = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var index = i < keyIndex ? i : i + 1;
                values[i] = ResultMerger.FormatValue(reader.IsDBNull(index) ? null : reader.GetValue(index));
            }
            rowsOut.Add(new BatchRow(key, values));
        }

        return new BatchRows(columns.ToArray(), rowsOut);
    }

    private static IEnumerable<(string Name, NpgsqlDbType Type, object Value)> BuildArrays(
        InputDefinition definition, IReadOnlyList<InputRow> rows)
    {
        yield return (definition.RowIdParameter, NpgsqlDbType.Array | NpgsqlDbType.Integer,
            rows.Select(row => row.RowId).ToArray());

        for (var c = 0; c < definition.Columns.Count; c++)
        {
            var type = definition.Columns[c].Type.ToLowerInvariant();
            var index = c;
            object value = type switch
            {
                "string" => rows.Select(row => index < row.Cells.Count ? row.Cells[index] : "").ToArray(),
                "int" => rows.Select(row => int.TryParse(Cell(row, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null).ToArray(),
                "decimal" => rows.Select(row => decimal.TryParse(Cell(row, index).Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null).ToArray(),
                "date" => rows.Select(row => TryDate(Cell(row, index), out var parsed) ? parsed : (DateOnly?)null).ToArray(),
                "bool" => rows.Select(row => TryBool(Cell(row, index), out var parsed) ? parsed : (bool?)null).ToArray(),
                _ => throw new NotSupportedException($"Тип колонки '{definition.Columns[c].Type}' не поддерживается")
            };
            yield return (definition.Columns[c].Name, ArrayTypeOf(type), value);
        }
    }

    private static string Cell(InputRow row, int index) => index < row.Cells.Count ? row.Cells[index] : "";

    private static string[] ScalarType(string type) => type.ToLowerInvariant() switch
    {
        "string" => ["text", "varchar"],
        "int" => ["int4"],
        "decimal" => ["numeric"],
        "date" => ["date"],
        "bool" => ["bool"],
        _ => []
    };

    private static string[] ArrayType(string type) => type.ToLowerInvariant() switch
    {
        "string" => ["text[]", "varchar[]"],
        "int" => ["int4[]"],
        "decimal" => ["numeric[]"],
        "date" => ["date[]"],
        "bool" => ["bool[]"],
        _ => []
    };

    private static NpgsqlDbType ArrayTypeOf(string type) => type switch
    {
        "string" => NpgsqlDbType.Array | NpgsqlDbType.Text,
        "int" => NpgsqlDbType.Array | NpgsqlDbType.Integer,
        "decimal" => NpgsqlDbType.Array | NpgsqlDbType.Numeric,
        "date" => NpgsqlDbType.Array | NpgsqlDbType.Date,
        "bool" => NpgsqlDbType.Array | NpgsqlDbType.Boolean,
        _ => throw new NotSupportedException($"Тип колонки '{type}' не поддерживается")
    };

    private static long CountRows(InputStage input)
    {
        long count = 0;
        foreach (var _ in input.ReadRows()) count++;
        return count;
    }

    private static string[] SplitRecord(string line)
    {
        var fields = new List<string>();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { builder.Append('"'); i++; }
                    else inQuotes = false;
                }
                else builder.Append(ch);
                continue;
            }
            if (ch == '"') { inQuotes = true; continue; }
            if (ch == ';') { fields.Add(builder.ToString()); builder.Clear(); continue; }
            builder.Append(ch);
        }
        fields.Add(builder.ToString());
        return fields.ToArray();
    }

    private static bool TryDate(string value, out DateOnly date)
    {
        string[] formats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }
        date = default;
        return false;
    }

    private static bool TryBool(string value, out bool flag)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "да": flag = true; return true;
            case "false" or "0" or "нет": flag = false; return true;
            default: flag = false; return false;
        }
    }

    private sealed record BatchRows(string[] Columns, IReadOnlyList<BatchRow> Rows);
}
