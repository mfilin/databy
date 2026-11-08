using System.Globalization;
using System.Text;
using Databy.Reports.Catalog;
using ExcelDataReader;

namespace Databy.Reports.Input;

/// <summary>
/// Превращает пользовательский список (буфер, csv, txt, tsv, xlsx) в потоковый
/// временный stage-файл. Весь список в памяти не держится: сохраняются только
/// предпросмотр и первые ошибки.
/// </summary>
public sealed class ListInputParser
{
    static ListInputParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public InputParseResult ParseText(string text, InputParseOptions options, CancellationToken cancellationToken = default,
        IProgress<long>? progress = null)
    {
        using var reader = new StringReader(text);
        return ParseText(reader, options, cancellationToken, progress);
    }

    public InputParseResult ParseFile(string path, InputParseOptions options, CancellationToken cancellationToken = default,
        IProgress<long>? progress = null)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => ParseWorkbook(path, options, cancellationToken, progress),
            ".csv" or ".txt" or ".tsv" => ParseTextFile(path, options, cancellationToken, progress),
            _ => throw new NotSupportedException($"Формат '{extension}' не поддерживается (csv, txt, tsv, xlsx)")
        };
    }

    private InputParseResult ParseTextFile(string path, InputParseOptions options, CancellationToken cancellationToken, IProgress<long>? progress)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = InputEncoding.Detect(bytes, bytes.Length);
        using var memory = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(memory, encoding, detectEncodingFromByteOrderMarks: true);
        return ParseText(reader, options, cancellationToken, progress);
    }

    private InputParseResult ParseText(TextReader reader, InputParseOptions options, CancellationToken cancellationToken, IProgress<long>? progress)
    {
        var text = reader.ReadToEnd();
        var delimiter = options.Delimiter == InputDelimiter.Auto
            ? DetectDelimiter(FirstLine(text))
            : DelimiterChar(options.Delimiter);

        var csv = new CsvRecordReader(new StringReader(text), delimiter);

        string[]? header = null;
        var firstRecord = csv.ReadRecord();
        if (firstRecord is null) throw new InvalidOperationException("Пустой список значений");

        int sourceColumnCount;
        List<string>? firstData = firstRecord;
        if (options.HasHeader)
        {
            header = NormalizeHeader(firstRecord);
            sourceColumnCount = header.Length;
            firstData = csv.ReadRecord();
        }
        else sourceColumnCount = firstRecord.Count;

        return ParseRecords(csv, firstData, header, sourceColumnCount, options, cancellationToken, progress);
    }

    private static string FirstLine(string text)
    {
        var newline = text.IndexOfAny(['\r', '\n']);
        return newline < 0 ? text : text[..newline];
    }

    /// <summary>
    /// Автоопределение разделителя по первой непустой строке (вне кавычек):
    /// табуляция, затем ';', затем ','. Иначе — одна колонка.
    /// </summary>
    public static char? DetectDelimiter(string firstLine)
    {
        var inQuotes = false;
        var hasSemicolon = false;
        var hasComma = false;
        foreach (var ch in firstLine)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (inQuotes) continue;
            if (ch == '\t') return '\t';
            if (ch == ';') hasSemicolon = true;
            else if (ch == ',') hasComma = true;
        }
        if (hasSemicolon) return ';';
        if (hasComma) return ',';
        return null;
    }

    private static char? DelimiterChar(InputDelimiter delimiter) => delimiter switch
    {
        InputDelimiter.Tab => '\t',
        InputDelimiter.Semicolon => ';',
        InputDelimiter.Comma => ',',
        InputDelimiter.None => null,
        _ => null
    };

    private static string[] NormalizeHeader(List<string> record)
    {
        var names = new string[record.Count];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < record.Count; i++)
        {
            var name = record[i].Trim();
            if (name.Length == 0) name = "Колонка " + (i + 1);
            if (seen.TryGetValue(name, out var count))
            {
                var suffix = count + 1;
                seen[name] = suffix;
                name = $"{name} ({suffix})";
            }
            else seen[name] = 1;
            names[i] = name;
        }
        return names;
    }

    private InputParseResult ParseRecords(CsvRecordReader csv, List<string>? pending, string[]? header, int sourceColumnCount,
        InputParseOptions options, CancellationToken cancellationToken, IProgress<long>? progress)
    {
        var stage = new InputStage(InputStage.CreatePath(), sourceColumnCount);
        var preview = new List<InputPreviewRow>(Math.Min(options.PreviewSize, 32));
        var errors = new List<InputRowError>(Math.Min(options.MaxErrors, 32));
        long total = 0, valid = 0, errorRows = 0;
        var rowId = 0;
        var sourceNames = header ?? BuildDefaultNames(sourceColumnCount);
        var mapped = ResolveMapping(options);

        try
        {
            using var writer = stage.OpenWriter();
            InputStage.WriteRecord(writer, sourceNames.Concat([InputStage.RowIdColumn, InputStage.ErrorColumn]));

            var record = pending;
            while (record is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsEmpty(record))
                {
                    rowId++;
                    if (rowId > options.MaxRows)
                        throw new InvalidOperationException($"Превышен лимит строк списка: {options.MaxRows}");

                    var cells = Align(record, sourceColumnCount);
                    var error = Validate(cells, mapped);
                    InputStage.WriteRecord(writer, cells.Concat([rowId.ToString(CultureInfo.InvariantCulture), error?.Reason ?? ""]));

                    Count(error, errors, options, rowId, ref valid, ref errorRows, preview, cells);
                    total++;
                    if (total % options.CancellationCheckInterval == 0) progress?.Report(total);
                }
                record = csv.ReadRecord();
            }

            progress?.Report(total);
            return new InputParseResult(stage, sourceNames, preview, errors, total, valid, errorRows);
        }
        catch
        {
            stage.Dispose();
            throw;
        }
    }

    private InputParseResult ParseWorkbook(string path, InputParseOptions options, CancellationToken cancellationToken, IProgress<long>? progress)
    {
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        return ParseWorkbook(reader, options, cancellationToken, progress);
    }

    private InputParseResult ParseWorkbook(IExcelDataReader reader, InputParseOptions options, CancellationToken cancellationToken, IProgress<long>? progress)
    {
        var hasRows = reader.Read();
        var width = reader.FieldCount;

        string[]? header = null;
        if (options.HasHeader && hasRows)
        {
            header = new string[width];
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < width; i++)
            {
                var name = ConvertToText(reader.GetValue(i)).Trim();
                if (name.Length == 0) name = "Колонка " + (i + 1);
                if (seen.TryGetValue(name, out var count))
                {
                    var suffix = count + 1;
                    seen[name] = suffix;
                    name = $"{name} ({suffix})";
                }
                else seen[name] = 1;
                header[i] = name;
            }
            hasRows = reader.Read();
        }

        var stage = new InputStage(InputStage.CreatePath(), width);
        var preview = new List<InputPreviewRow>(Math.Min(options.PreviewSize, 32));
        var errors = new List<InputRowError>(Math.Min(options.MaxErrors, 32));
        long total = 0, valid = 0, errorRows = 0;
        var rowId = 0;
        var sourceNames = header ?? BuildDefaultNames(width);
        var mapped = ResolveMapping(options);

        try
        {
            using var writer = stage.OpenWriter();
            InputStage.WriteRecord(writer, sourceNames.Concat([InputStage.RowIdColumn, InputStage.ErrorColumn]));

            while (hasRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cells = new string[width];
                var any = false;
                for (var i = 0; i < width; i++)
                {
                    cells[i] = ConvertToText(reader.GetValue(i));
                    if (!string.IsNullOrWhiteSpace(cells[i])) any = true;
                }

                if (any)
                {
                    rowId++;
                    if (rowId > options.MaxRows)
                        throw new InvalidOperationException($"Превышен лимит строк списка: {options.MaxRows}");

                    var error = Validate(cells, mapped);
                    InputStage.WriteRecord(writer, cells.Concat([rowId.ToString(CultureInfo.InvariantCulture), error?.Reason ?? ""]));
                    Count(error, errors, options, rowId, ref valid, ref errorRows, preview, cells);
                    total++;
                    if (total % options.CancellationCheckInterval == 0) progress?.Report(total);
                }

                hasRows = reader.Read();
            }

            progress?.Report(total);
            return new InputParseResult(stage, sourceNames, preview, errors, total, valid, errorRows);
        }
        catch
        {
            stage.Dispose();
            throw;
        }
    }

    private static void Count(CellError? error, List<InputRowError> errors, InputParseOptions options, int rowId,
        ref long valid, ref long errorRows, List<InputPreviewRow> preview, string[] cells)
    {
        if (error is not null)
        {
            errorRows++;
            if (errors.Count < options.MaxErrors) errors.Add(new InputRowError(rowId, error.Column, error.Reason));
        }
        else valid++;

        if (preview.Count < options.PreviewSize) preview.Add(new InputPreviewRow(rowId, cells));
    }

    private static (InputColumnDefinition Definition, int Index)[] ResolveMapping(InputParseOptions options) =>
        options.ColumnMapping
            .Where(pair => pair.Value >= 0)
            .Select(pair => (Definition: options.Columns.FirstOrDefault(column => column.Name == pair.Key), pair.Value))
            .Where(pair => pair.Definition is not null)
            .Select(pair => (pair.Definition!, pair.Value))
            .ToArray();

    private static string[] Align(List<string> record, int count)
    {
        var cells = new string[count];
        for (var i = 0; i < count; i++) cells[i] = i < record.Count ? record[i] : "";
        return cells;
    }

    private static string[] BuildDefaultNames(int count)
    {
        var names = new string[count];
        for (var i = 0; i < count; i++) names[i] = "Колонка " + (i + 1);
        return names;
    }

    private static bool IsEmpty(List<string> record) => record.All(string.IsNullOrWhiteSpace);

    private static CellError? Validate(string[] cells, (InputColumnDefinition Definition, int Index)[] mapped)
    {
        foreach (var (definition, index) in mapped)
        {
            if (index >= cells.Length) continue;
            var value = cells[index];
            if (string.IsNullOrWhiteSpace(value))
            {
                if (definition.Required) return new CellError(definition.Name, "пустое значение обязательной колонки");
                continue;
            }

            if (!InputValue.TryParse(value, definition.Type, out _))
                return new CellError(definition.Name, $"не удалось разобрать значение как {definition.Type}");
        }
        return null;
    }

    private static string ConvertToText(object? value) => value switch
    {
        null => "",
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        double number when number == Math.Floor(number) && Math.Abs(number) < 1e15 => ((long)number).ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        float number when number == MathF.Floor(number) => ((long)number).ToString(CultureInfo.InvariantCulture),
        float number => number.ToString(CultureInfo.InvariantCulture),
        decimal number when number == decimal.Truncate(number) => decimal.Truncate(number).ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private sealed record CellError(string Column, string Reason);
}
