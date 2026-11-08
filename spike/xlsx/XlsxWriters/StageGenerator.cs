using System.Globalization;
using System.Text;

namespace XlsxWriters;

/// <summary>
/// Генерирует stage-подобный CSV: заголовок и строки, все поля в кавычках,
/// разделитель ';', NULL как \N, значения в инвариантной культуре.
/// Формат совпадает с TemporaryCsvStage продукта.
/// </summary>
public static class StageGenerator
{
    private static readonly string LongCyrillic =
        string.Concat(Enumerable.Repeat("Поставка оборудования и монтаж систем ", 4))[..100];
    private static readonly string TailText = "хвостовая колонка";

    public static string Generate(string path, int rows, int seed = 42)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));

        WriteRecord(writer, StageSchema.Columns.Select(column => column.Name));

        var random = new Random(seed);
        for (var row = 1; row <= rows; row++)
        {
            var isNull = random.Next(100) < 20;
            var line = new StringBuilder(512);
            AppendField(line, row.ToString(CultureInfo.InvariantCulture), appendSeparator: false);
            AppendField(line, "Короткий текст #" + (row % 1000));
            AppendField(line, LongCyrillic);
            AppendField(line, ((row % 10_000) / 100.0).ToString("F2", CultureInfo.InvariantCulture));
            AppendField(line, new DateOnly(2026, 1, 1).AddDays(row % 365).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AppendField(line, new DateTime(2026, 1, 1, 8, 30, 0).AddMinutes(row % 720).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            AppendField(line, (row % 2 == 0).ToString());
            AppendField(line, isNull ? StageSchema.NullMarker : "значение " + row);
            AppendField(line, isNull ? StageSchema.NullMarker : (row * 3).ToString(CultureInfo.InvariantCulture));
            AppendField(line, isNull ? StageSchema.NullMarker : ((row % 1000) + 0.25m).ToString("F2", CultureInfo.InvariantCulture));
            AppendField(line, TailText);
            AppendField(line, (row % 97).ToString(CultureInfo.InvariantCulture));
            writer.WriteLine(line.ToString());
        }

        return path;
    }

    private static void WriteRecord(TextWriter writer, IEnumerable<string> values)
    {
        var line = new StringBuilder(256);
        var first = true;
        foreach (var value in values)
        {
            AppendField(line, value, appendSeparator: !first);
            first = false;
        }
        writer.WriteLine(line.ToString());
    }

    private static void AppendField(StringBuilder line, string value, bool appendSeparator = true)
    {
        if (appendSeparator) line.Append(';');
        line.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
    }
}