using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ExcelDataReader;

namespace XlsxWriters;

public sealed record VerifyReport(int Rows, int Columns, IReadOnlyList<string> Checks, int? SchemaErrors);

/// <summary>Независимая проверка xlsx через ExcelDataReader + OpenXmlValidator.</summary>
public static class Verifier
{
    public static VerifyReport Verify(string path, int expectedRows)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var checks = new List<string>();
        var rows = new List<object?[]>();

        using (var stream = File.OpenRead(path))
        using (var reader = ExcelReaderFactory.CreateReader(stream))
        {
            while (reader.Read())
            {
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(values);
            }
        }

        var dataRows = rows.Count - 1;
        if (dataRows != expectedRows) throw new InvalidDataException($"Ожидалось {expectedRows} строк данных, получено {dataRows}");

        var header = rows[0];
        if (header.Length != StageSchema.Columns.Length) throw new InvalidDataException($"Ожидалось {StageSchema.Columns.Length} колонок, получено {header.Length}");

        for (var c = 0; c < StageSchema.Columns.Length; c++)
        {
            var title = Convert.ToString(header[c], CultureInfo.InvariantCulture);
            if (title != StageSchema.Columns[c].Title) throw new InvalidDataException($"Заголовок колонки {c}: '{title}' != '{StageSchema.Columns[c].Title}'");
        }
        checks.Add($"headers: {string.Join(" | ", header.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)))}");

        var sampleIndexes = new[] { 1, rows.Count / 2, rows.Count - 1 };
        foreach (var index in sampleIndexes)
        {
            var row = rows[index];
            checks.Add($"row {index}: {string.Join(" | ", row.Select(Format))}");
            AssertTypedRow(row, index);
        }

        // NULL-колонки: значение либо null (пустая ячейка), либо строка — но ячейка должна быть пустой.
        var nullCells = rows.Skip(1).Count(row => row[7] is null && row[8] is null && row[9] is null);
        var totalRows = rows.Count - 1;
        if (nullCells == 0) throw new InvalidDataException("Не найдено ни одной строки с NULL в колонках null_text/null_int/null_decimal");
        checks.Add($"nulls: строк с пустыми null-колонками = {nullCells} из {totalRows}");
        foreach (var row in rows.Skip(1))
        {
            var allNull = row[7] is null && row[8] is null && row[9] is null;
            var noneNull = row[7] is not null && row[8] is not null && row[9] is not null;
            if (!allNull && !noneNull) throw new InvalidDataException("NULL-колонки не согласованы в одной строке");
        }

        return new VerifyReport(dataRows, header.Length, checks, ValidateSchema(path));
    }

    private static void AssertTypedRow(object?[] row, int index)
    {
        // id: int
        Assert.IsType<double>(row[0], index, "id");
        // amount: decimal
        Assert.IsType<double>(row[3], index, "amount");
        // date: DateTime
        Assert.IsType<DateTime>(row[4], index, "date");
        // date_time: DateTime
        Assert.IsType<DateTime>(row[5], index, "date_time");
        // flag: bool
        Assert.IsType<bool>(row[6], index, "flag");
        // tail_text: string
        Assert.IsType<string>(row[10], index, "tail_text");
        Assert.IsType<double>(row[11], index, "tail_int");
    }

    

    private static string Format(object? value) => value switch
    {
        null => "<null>",
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static int? ValidateSchema(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document).ToList();
        foreach (var error in errors.Take(5))
            Console.WriteLine($"   schema: {error.Path?.XPath} — {error.Description}");
        return errors.Count;
    }

    private static class Assert
    {
        public static void IsType<T>(object? value, int row, string column)
        {
            if (value is T) return;
            throw new InvalidDataException($"Строка {row}, колонка '{column}': ожидался {typeof(T).Name}, получен {value?.GetType().Name ?? "null"}");
        }
    }
}