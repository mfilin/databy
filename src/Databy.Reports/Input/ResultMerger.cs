using System.Data;
using System.Globalization;

namespace Databy.Reports.Input;

/// <summary>Итог пакета: исходные строки и строки из БД, сгруппированные по ключу.</summary>
public sealed class BatchResult
{
    public BatchResult(IReadOnlyList<InputRow> sourceRows, IReadOnlyList<BatchRow> databaseRows)
    {
        SourceRows = sourceRows;
        DatabaseRows = databaseRows;
    }

    public IReadOnlyList<InputRow> SourceRows { get; }
    public IReadOnlyList<BatchRow> DatabaseRows { get; }
}

/// <summary>Строка из БД: ключ результата и значения колонок (без ключа).</summary>
public sealed record BatchRow(int RowId, IReadOnlyList<string> Values);

/// <summary>
/// Чистое слияние исходных строк списка с результатом БД. Хранит только
/// строки текущего пакета, поэтому память не зависит от размера списка.
/// </summary>
public sealed class ResultMerger
{
    public const string NotFoundStatus = "Не найдено";
    public const string FoundStatus = "Найдено";
    public const string InputErrorPrefix = "Ошибка во входных данных: ";
    public const string DatabaseColumnSuffix = " (БД)";

    private readonly List<string> _sourceColumns;
    private readonly List<string> _databaseColumns;
    private readonly string[] _outputColumns;

    public ResultMerger(IReadOnlyList<string> sourceColumns, IReadOnlyList<string> databaseColumns)
    {
        _sourceColumns = [.. sourceColumns];
        _databaseColumns = [.. databaseColumns];
        _outputColumns = BuildOutputColumns(_sourceColumns, _databaseColumns);
    }

    public IReadOnlyList<string> OutputColumns => _outputColumns;

    /// <summary>Совпадает ли набор колонок БД с уже известным (для проверки пакетов).</summary>
    public bool Matches(IReadOnlyList<string> databaseColumns) =>
        _databaseColumns.Count == databaseColumns.Count &&
        _databaseColumns.Select((name, index) => name == databaseColumns[index]).All(match => match);

    public static string[] BuildOutputColumns(IReadOnlyList<string> sourceColumns, IReadOnlyList<string> databaseColumns)
    {
        var source = new HashSet<string>(sourceColumns, StringComparer.Ordinal);
        var result = new List<string>(sourceColumns.Count + databaseColumns.Count + 1);
        result.AddRange(sourceColumns);
        foreach (var column in databaseColumns)
        {
            if (source.Contains(column)) result.Add(column + DatabaseColumnSuffix);
            else result.Add(column);
        }
        return [.. result];
    }

    /// <summary>
    /// Пишет итоговые строки пакета в stage стазу в исходном порядке.
    /// Возвращает число записанных строк.
    /// </summary>
    public long Write(TextWriter writer, BatchResult batch)
    {
        var lookup = new Dictionary<int, List<BatchRow>>(batch.DatabaseRows.Count);
        foreach (var row in batch.DatabaseRows)
        {
            if (!lookup.TryGetValue(row.RowId, out var list))
            {
                list = [];
                lookup[row.RowId] = list;
            }
            list.Add(row);
        }

        long written = 0;
        foreach (var source in batch.SourceRows)
        {
            if (source.Error is { } error)
            {
                WriteRecord(writer, source.Cells, null, InputErrorPrefix + error);
                written++;
                continue;
            }

            if (lookup.TryGetValue(source.RowId, out var matches))
            {
                foreach (var match in matches)
                {
                    WriteRecord(writer, source.Cells, match.Values, FoundStatus);
                    written++;
                }
                continue;
            }

            WriteRecord(writer, source.Cells, null, NotFoundStatus);
            written++;
        }

        return written;
    }

    private void WriteRecord(TextWriter writer, IReadOnlyList<string> cells, IReadOnlyList<string>? databaseValues, string status)
    {
        var values = new string[_sourceColumns.Count + _databaseColumns.Count + 1];
        for (var i = 0; i < _sourceColumns.Count; i++)
            values[i] = i < cells.Count ? cells[i] : "";
        for (var i = 0; i < _databaseColumns.Count; i++)
            values[_sourceColumns.Count + i] = databaseValues is not null && i < databaseValues.Count ? databaseValues[i] : "";
        values[^1] = status;
        WriteRecord(writer, values);
    }

    private static void WriteRecord(TextWriter writer, IEnumerable<string> values) =>
        writer.WriteLine(string.Join(';', values.Select(Field)));

    private static string Field(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>Форматирование значения из БД в текст stage (пустое = пустая строка).</summary>
    public static string FormatValue(object? value) => value switch
    {
        null or DBNull => "",
        bool flag => flag ? "True" : "False",
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };
}
