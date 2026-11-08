using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace XlsxWriters;

/// <summary>Потоковый читатель stage-CSV.</summary>
public interface IStageReader
{
    string Name { get; }

    /// <summary>Возвращает строки данных (без заголовка), каждая — массив полей как в stage.</summary>
    IEnumerable<string[]> Read(string path);
}

/// <summary>Читатель на TextFieldParser (как в продукте), включая заголовок.</summary>
public sealed class TextFieldParserStageReader : IStageReader
{
    public string Name => "TextFieldParser";

    public IEnumerable<string[]> Read(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");
        _ = parser.ReadFields();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is not null) yield return fields;
        }
    }
}

/// <summary>
/// Минимальный собственный читатель без лишних аллокаций:
/// один проход по строкам, ручной разбор кавычек, буфер строки переиспользуется.
/// </summary>
public sealed class FastStageReader : IStageReader
{
    public string Name => "FastReader";

    public IEnumerable<string[]> Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
        _ = reader.ReadLine();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            yield return ParseLine(line);
        }
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>(StageSchema.Columns.Length);
        var builder = new StringBuilder(128);
        var index = 0;
        var inQuotes = false;

        while (index < line.Length)
        {
            var ch = line[index];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"') { builder.Append('"'); index += 2; continue; }
                    inQuotes = false;
                    index++;
                    continue;
                }
                builder.Append(ch);
                index++;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    index++;
                    break;
                case ';':
                    fields.Add(builder.ToString());
                    builder.Clear();
                    index++;
                    break;
                default:
                    builder.Append(ch);
                    index++;
                    break;
            }
        }

        fields.Add(builder.ToString());
        return fields.ToArray();
    }
}