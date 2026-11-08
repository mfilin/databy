using System.Text;

namespace Databy.Reports.Input;

/// <summary>
/// Потоковый построчный читатель RFC 4180: кавычки, удвоенные кавычки,
/// переводы строк внутри значений (\r\n, \n, \r).
/// </summary>
internal sealed class CsvRecordReader
{
    private readonly TextReader _reader;
    private readonly char _delimiter;
    private readonly StringBuilder _field = new(128);
    private readonly List<string> _fields = new(16);

    public CsvRecordReader(TextReader reader, char? delimiter)
    {
        _reader = reader;
        _delimiter = delimiter ?? '\u0000';
    }

    /// <summary>Читает следующую непустую запись; null — конец потока.</summary>
    public List<string>? ReadRecord()
    {
        while (true)
        {
            var record = ReadRawRecord();
            if (record is null) return null;
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0])) continue;
            return record;
        }
    }

    private List<string>? ReadRawRecord()
    {
        _fields.Clear();
        _field.Clear();
        var inQuotes = false;
        var readAny = false;

        while (true)
        {
            var next = _reader.Read();
            if (next < 0)
            {
                if (!readAny) return null;
                _fields.Add(_field.ToString());
                return new List<string>(_fields);
            }

            readAny = true;
            var ch = (char)next;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (_reader.Peek() == '"') { _reader.Read(); _field.Append('"'); }
                    else inQuotes = false;
                }
                else _field.Append(ch);
                continue;
            }

            if (ch == '"' && _field.Length == 0) { inQuotes = true; continue; }

            if (_delimiter != '\u0000' && ch == _delimiter)
            {
                _fields.Add(_field.ToString());
                _field.Clear();
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && _reader.Peek() == '\n') _reader.Read();
                _fields.Add(_field.ToString());
                return new List<string>(_fields);
            }

            _field.Append(ch);
        }
    }
}
