using System.Text;

namespace Databy.Reports.Input;

/// <summary>
/// Временный файл списка: все исходные колонки + служебные <c>__row_id</c> и <c>__error</c>.
/// Чтение потоковое, файл удаляется при Dispose.
/// </summary>
public sealed class InputStage : IDisposable
{
    public const string RowIdColumn = "__row_id";
    public const string ErrorColumn = "__error";

    private readonly string _path;

    internal InputStage(string path, int sourceColumnCount)
    {
        _path = path;
        SourceColumnCount = sourceColumnCount;
    }

    public string Path => _path;
    public int SourceColumnCount { get; }

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
