using System.Data;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Databy.Reports.Catalog;
using Microsoft.VisualBasic.FileIO;

namespace Databy.Reports.Export;

public sealed record ExportResult(string Path, string Format, long Rows, bool ExcelLimitExceeded);

public sealed class TemporaryCsvStage : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "databy-" + Guid.NewGuid().ToString("N") + ".csv");
    public StreamWriter OpenWriter() => new(Path, false, new UTF8Encoding(true));
    public void Dispose() { try { File.Delete(Path); } catch { } }
}

/// <summary>Builds an XLSX or semicolon separated UTF-8 CSV from the single-pass staged result.</summary>
public sealed class ReportExportWriter
{
    public const int DefaultExcelDataRowLimit = 1_048_575;
    public const string NullMarker = "\\N";

    public ExportResult Save(string stagedCsv, DataTable schema, string targetPath, string? sheetName,
            ResultDefinition? result, long rowCount,
            int excelDataRowLimit = DefaultExcelDataRowLimit)
    {
        if (!File.Exists(stagedCsv)) throw new FileNotFoundException("Временная выгрузка не найдена", stagedCsv);

        var databaseColumns = schema.Columns.Cast<DataColumn>().Select(column => column.ColumnName);
        var visible = ResultColumns.Resolve(databaseColumns, result, out var unknownHidden);
        if (unknownHidden.Count > 0)
            Serilog.Log.Warning("Экспорт: в result.hide указаны отсутствующие колонки: {Columns}", string.Join(", ", unknownHidden));
        if (visible.Count == 0)
            throw new InvalidOperationException("Все колонки скрыты в YAML отчёта");

        var indexes = visible.Select(column => schema.Columns.IndexOf(column.Name)).ToArray();

        if (rowCount > excelDataRowLimit)
        {
            var csvPath = Path.ChangeExtension(targetPath, ".csv");
            WriteUserCsv(stagedCsv, schema, csvPath, visible, indexes);
            return new(csvPath, "CSV", rowCount, true);
        }
        if (string.Equals(Path.GetExtension(targetPath), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            WriteUserCsv(stagedCsv, schema, targetPath, visible, indexes);
            return new(targetPath, "CSV", rowCount, false);
        }
        WriteWorkbook(stagedCsv, schema, targetPath, sheetName, visible, indexes);
        return new(targetPath, "XLSX", rowCount, false);
    }

    private static void WriteUserCsv(string stagedCsv, DataTable schema, string targetPath,
        IReadOnlyList<ResultColumn> visible, IReadOnlyList<int> indexes)
    {
        using var writer = new StreamWriter(targetPath, false, new UTF8Encoding(true)) { NewLine = "\r\n" };
        using var parser = new TextFieldParser(stagedCsv, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");
        _ = parser.ReadFields(); // stage headers are database names; user headers come from YAML

        WriteUserCsvRecord(writer, visible.Select(column => column.Title));

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.Length != schema.Columns.Count) throw new InvalidDataException("Временная CSV-выгрузка повреждена");
            WriteUserCsvRecord(writer, indexes.Select(index => FormatUserField(fields[index], schema.Columns[index].DataType)));
        }
    }

    private static void WriteUserCsvRecord(TextWriter writer, IEnumerable<string> values) =>
        writer.WriteLine(string.Join(';', values.Select(EscapeUserField)));

    private static string FormatUserField(string value, Type type)
    {
        if (value == NullMarker) return string.Empty;
        if (type == typeof(DateOnly)) return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly) ? dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : value;
        if (type == typeof(DateTime))
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime)) return value;
            return dateTime.TimeOfDay == TimeSpan.Zero
                ? dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        return value;
    }

    private static string EscapeUserField(string value) =>
        value.IndexOfAny([';', '"', '\r', '\n']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

    public static string CsvField(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    public static void WriteCsvRecord(TextWriter writer, IEnumerable<string> values) =>
        writer.WriteLine(string.Join(';', values.Select(CsvField)));

    public static void WriteStageRow(TextWriter writer, DataRow row) =>
        WriteCsvRecord(writer, row.ItemArray.Select(value => value is DBNull ? NullMarker : Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));

    public static string CleanSheetName(string? value)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? "Отчёт" : value;
        foreach (var invalid in new[] { '\\', '/', '?', '*', '[', ']', ':' }) cleaned = cleaned.Replace(invalid, '_');
        cleaned = cleaned.Trim('\'', ' ');
        if (cleaned.Length == 0) cleaned = "Отчёт";
        return cleaned[..Math.Min(cleaned.Length, 31)];
    }

    private static void WriteWorkbook(string stagedCsv, DataTable schema, string targetPath, string? sheetName,
            IReadOnlyList<ResultColumn> visible, IReadOnlyList<int> indexes)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(CleanSheetName(sheetName));
        for (var c = 0; c < visible.Count; c++)
        {
            worksheet.Cell(1, c + 1).Value = visible[c].Title;
            worksheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        using var parser = new TextFieldParser(stagedCsv, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");
        _ = parser.ReadFields(); // headers are derived from the result schema and YAML
        var row = 2;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.Length != schema.Columns.Count) throw new InvalidDataException("Временная CSV-выгрузка повреждена");
            for (var c = 0; c < visible.Count; c++)
            {
                var sourceColumn = indexes[c];
                var field = fields[sourceColumn];
                if (field == NullMarker) continue;
                var cell = worksheet.Cell(row, c + 1);
                cell.Value = ToCellValue(field, schema.Columns[sourceColumn].DataType);
                if (!string.IsNullOrWhiteSpace(visible[c].Format))
                    cell.Style.NumberFormat.Format = visible[c].Format;
            }
            row++;
        }
        worksheet.SheetView.FreezeRows(1);
        if (visible.Count > 0) worksheet.Range(1, 1, Math.Max(1, row - 1), visible.Count).SetAutoFilter();
        workbook.SaveAs(targetPath);
    }

    private static XLCellValue ToCellValue(string value, Type type)
    {
        if (type == typeof(string)) return value;
        if (type == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(DateOnly)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(bool)) return bool.Parse(value);
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)) return long.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return double.Parse(value, CultureInfo.InvariantCulture);
        return value;
    }
}
