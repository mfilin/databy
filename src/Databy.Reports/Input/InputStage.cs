using System.Text;

namespace Databy.Reports.Input;

/// <summary>
/// Временный файл списка: все исходные колонки + служебные <c>__row_id</c> и <c>__error</c>.
/// Хранит имена колонок источника и сопоставление «ключевая колонка контракта → индекс
/// колонки источника», чтобы в БД уходили именно выбранные пользователем колонки.
/// Чтение потоковое, файл удаляется при Dispose.
/// </summary>
public sealed class InputStage : IDisposable
{
    public const string RowIdColumn = "__row_id";
    public const string ErrorColumn = "__error";

    private readonly string _path;
    private readonly Dictionary<string, int> _columnMapping;

    internal InputStage(string path, IReadOnlyList<string> sourceColumns,
        IReadOnlyDictionary<string, int> columnMapping)
    {
        _path = path;
        SourceColumns = [.. sourceColumns];
        _columnMapping = new Dictionary<string, int>(columnMapping, StringComparer.Ordinal);
    }

    public string Path => _path;
    public IReadOnlyList<string> SourceColumns { get; }
    public int SourceColumnCount => SourceColumns.Count;

    /// <summary>Число строк исходного списка, известное после разбора.</summary>
    public long TotalRows { get; internal set; }

    public IReadOnlyDictionary<string, int> ColumnMapping => _columnMapping;

    /// <summary>
    /// Индекс колонки источника для ключевой колонки контракта. Если сопоставление
    /// не сохранено (stage устаревшего формата), выдаётся понятная ошибка вместо
    /// молчаливой подстановки позиции.
    /// </summary>
    public int GetSourceIndex(string columnName)
    {
        if (SourceColumns.Count == 0)
            throw new InvalidOperationException(
                "Временный файл списка не содержит сведений о колонках источника. Загрузите список значений заново.");
        if (_columnMapping.TryGetValue(columnName, out var index) && index >= 0 && index < SourceColumnCount)
            return index;
        throw new InvalidOperationException(
            $"Для ключевой колонки '{columnName}' не сохранено сопоставление с колонкой источника. Загрузите список значений заново.");
    }

    public static string CreatePath() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "databy-input-" + Guid.NewGuid().ToString("N") + ".csv");

    internal StreamWriter OpenWriter() => new(_path, false, new UTF8Encoding(true));

    internal static void WriteRecord(TextWriter writer, IEnumerable<string> values) =>
        writer.WriteLine(string.Join(';', values.Select(Field)));

    internal static string Field(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>Потоковое чтение строк stage.</summary>
    public IEnumerable<InputRow> ReadRows()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
        var csv = new CsvRecordReader(reader, ';');
        _ = csv.ReadRecord();
        List<string>? fields;
        while ((fields = csv.ReadRecord()) is not null)
        {
            if (fields.Count < 2) continue;
            if (!int.TryParse(fields[^2], out var rowId)) continue;
            var error = fields[^1];
            yield return new InputRow(rowId, error.Length == 0 ? null : error, fields.Take(fields.Count - 2).ToArray());
        }
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }
}
