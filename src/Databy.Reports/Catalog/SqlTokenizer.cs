using System.Text;

namespace Databy.Reports.Catalog;

public enum SqlTokenKind
{
    Word,
    QuotedIdentifier,
    String,
    Number,
    Punctuation,
    Placeholder,
    InvalidPlaceholder
}

public sealed record SqlToken(
    SqlTokenKind Kind,
    string Text,
    int Line,
    int Column,
    string? Name = null,
    string? TypeText = null);

public sealed class SqlSyntaxException(string message, int line, int column)
    : Exception($"{message} (строка {line}, позиция {column})")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

/// <summary>
/// Лексер ClickHouse-подобного SQL. Комментарии пропускаются, строки и идентификаторы
/// в кавычках не участвуют в проверке ключевых слов. Плейсхолдеры {имя:Тип} выделяются
/// в отдельные токены.
/// </summary>
public static class SqlTokenizer
{
    public static IReadOnlyList<SqlToken> Tokenize(string sql) => new Scanner(sql).Tokenize();

    private sealed class Scanner(string sql)
    {
        private readonly List<SqlToken> _tokens = [];
        private int _index;
        private int _line = 1;
        private int _column = 1;

        public IReadOnlyList<SqlToken> Tokenize()
        {
            while (_index < sql.Length)
            {
                var current = sql[_index];

                if (char.IsWhiteSpace(current))
                {
                    Advance();
                    continue;
                }

                if (current == '-' && Peek(1) == '-')
                {
                    SkipLineComment();
                    continue;
                }

                if (current == '#')
                {
                    SkipLineComment();
                    continue;
                }

                if (current == '/' && Peek(1) == '*')
                {
                    SkipBlockComment();
                    continue;
                }

                if (current == '\'')
                {
                    _tokens.Add(ReadQuoted('\''));
                    continue;
                }

                if (current is '"' or '`')
                {
                    _tokens.Add(ReadQuotedIdentifier(current));
                    continue;
                }

                if (current == '$' && TryReadHeredoc(out var heredoc))
                {
                    _tokens.Add(heredoc);
                    continue;
                }

                if (current == '{' && TryReadPlaceholder(out var placeholder))
                {
                    _tokens.Add(placeholder);
                    continue;
                }

                if (IsIdentifierStart(current))
                {
                    _tokens.Add(ReadWord());
                    continue;
                }

                if (char.IsDigit(current))
                {
                    _tokens.Add(ReadNumber());
                    continue;
                }

                _tokens.Add(ReadPunctuation());
            }

            return _tokens;
        }

        private char Peek(int offset) =>
            _index + offset < sql.Length ? sql[_index + offset] : '\0';

        private void Advance()
        {
            if (_index >= sql.Length) return;

            if (sql[_index] == '\r')
            {
                _index++;
                if (_index < sql.Length && sql[_index] == '\n') _index++;
                _line++;
                _column = 1;
                return;
            }

            if (sql[_index] == '\n')
            {
                _index++;
                _line++;
                _column = 1;
                return;
            }

            _index++;
            _column++;
        }

        private void SkipLineComment()
        {
            while (_index < sql.Length && sql[_index] is not ('\r' or '\n')) Advance();
        }

        private void SkipBlockComment()
        {
            var line = _line;
            var column = _column;
            Advance();
            Advance();
            while (_index < sql.Length)
            {
                if (sql[_index] == '*' && Peek(1) == '/')
                {
                    Advance();
                    Advance();
                    return;
                }
                Advance();
            }
            throw new SqlSyntaxException("Незавершённый блочный комментарий", line, column);
        }

        private SqlToken ReadQuoted(char quote)
        {
            var line = _line;
            var column = _column;
            var text = new StringBuilder();
            Advance();

            while (_index < sql.Length)
            {
                var current = sql[_index];
                if (current == quote)
                {
                    if (Peek(1) == quote)
                    {
                        text.Append(quote);
                        Advance();
                        Advance();
                        continue;
                    }

                    Advance();
                    return new SqlToken(SqlTokenKind.String, text.ToString(), line, column);
                }

                if (current == '\\' && _index + 1 < sql.Length)
                {
                    Advance();
                    text.Append(sql[_index]);
                    Advance();
                    continue;
                }

                text.Append(current);
                Advance();
            }

            throw new SqlSyntaxException("Незавершённый строковый литерал", line, column);
        }

        private SqlToken ReadQuotedIdentifier(char quote)
        {
            var line = _line;
            var column = _column;
            var text = new StringBuilder();
            Advance();

            while (_index < sql.Length)
            {
                var current = sql[_index];
                if (current == quote)
                {
                    if (Peek(1) == quote)
                    {
                        text.Append(quote);
                        Advance();
                        Advance();
                        continue;
                    }

                    Advance();
                    return new SqlToken(SqlTokenKind.QuotedIdentifier, text.ToString(), line, column);
                }

                if (current == '\\' && _index + 1 < sql.Length)
                {
                    Advance();
                    text.Append(sql[_index]);
                    Advance();
                    continue;
                }

                text.Append(current);
                Advance();
            }

            throw new SqlSyntaxException("Незавершённый идентификатор в кавычках", line, column);
        }

        private SqlToken ReadWord()
        {
            var line = _line;
            var column = _column;
            var start = _index;
            Advance();
            while (_index < sql.Length && IsIdentifierPart(sql[_index])) Advance();
            return new SqlToken(SqlTokenKind.Word, sql[start.._index], line, column);
        }

        private SqlToken ReadNumber()
        {
            var line = _line;
            var column = _column;
            var start = _index;
            while (_index < sql.Length)
            {
                var current = sql[_index];
                if (char.IsLetterOrDigit(current) || current is '.' or '_')
                {
                    Advance();
                    continue;
                }

                if ((current is '+' or '-') && _index > start && sql[_index - 1] is 'e' or 'E')
                {
                    Advance();
                    continue;
                }

                break;
            }
            return new SqlToken(SqlTokenKind.Number, sql[start.._index], line, column);
        }

        private SqlToken ReadPunctuation()
        {
            var line = _line;
            var column = _column;
            var text = sql[_index].ToString();
            Advance();
            return new SqlToken(SqlTokenKind.Punctuation, text, line, column);
        }

        private bool TryReadHeredoc(out SqlToken token)
        {
            token = null!;
            var line = _line;
            var column = _column;
            var start = _index;
            Advance(); // opening $

            var tagStart = _index;
            while (_index < sql.Length && IsIdentifierPart(sql[_index])) Advance();
            if (_index >= sql.Length || sql[_index] != '$')
            {
                Restart(start, line, column);
                return false;
            }

            var tag = sql[tagStart.._index];
            Advance(); // closing $ of the opening delimiter
            var delimiter = $"${tag}$";
            var contentStart = _index;
            var end = sql.IndexOf(delimiter, _index, StringComparison.Ordinal);
            if (end < 0)
                throw new SqlSyntaxException("Незавершённый heredoc-литерал", line, column);

            while (_index < end) Advance();
            var content = sql[contentStart..end];
            for (var i = 0; i < delimiter.Length; i++) Advance();

            token = new SqlToken(SqlTokenKind.String, content, line, column);
            return true;
        }

        private bool TryReadPlaceholder(out SqlToken token)
        {
            token = null!;
            var line = _line;
            var column = _column;
            var start = _index;
            Advance(); // {

            SkipWhitespace();
            if (_index >= sql.Length || !IsIdentifierStart(sql[_index]))
            {
                Restart(start, line, column);
                return false;
            }

            var nameStart = _index;
            Advance();
            while (_index < sql.Length && IsIdentifierPart(sql[_index])) Advance();
            var name = sql[nameStart.._index];

            SkipWhitespace();
            if (_index >= sql.Length || sql[_index] != ':')
            {
                Restart(start, line, column);
                return false;
            }

            Advance(); // :
            SkipWhitespace();
            var typeStart = _index;
            var parenthesisDepth = 0;
            while (_index < sql.Length)
            {
                var current = sql[_index];
                if (current is '\'' or '"' or '`')
                {
                    SkipQuotedInType(current);
                    continue;
                }

                if (current == '(')
                {
                    parenthesisDepth++;
                    Advance();
                    continue;
                }

                if (current == ')')
                {
                    if (parenthesisDepth > 0) parenthesisDepth--;
                    Advance();
                    continue;
                }

                if (current == '}' && parenthesisDepth == 0)
                {
                    var typeText = sql[typeStart.._index].Trim();
                    var raw = sql[start..(_index + 1)];
                    Advance(); // }
                    token = typeText.Length == 0
                        ? new SqlToken(SqlTokenKind.InvalidPlaceholder, raw, line, column, name, "")
                        : new SqlToken(SqlTokenKind.Placeholder, raw, line, column, name, typeText);
                    return true;
                }

                Advance();
            }

            token = new SqlToken(
                SqlTokenKind.InvalidPlaceholder,
                sql[start.._index],
                line,
                column,
                name,
                sql[typeStart.._index].Trim());
            return true;
        }

        private void SkipQuotedInType(char quote)
        {
            Advance();
            while (_index < sql.Length)
            {
                if (sql[_index] == quote)
                {
                    if (Peek(1) == quote)
                    {
                        Advance();
                        Advance();
                        continue;
                    }
                    Advance();
                    return;
                }

                if (sql[_index] == '\\' && _index + 1 < sql.Length)
                {
                    Advance();
                    Advance();
                    continue;
                }

                Advance();
            }
        }

        private void SkipWhitespace()
        {
            while (_index < sql.Length && char.IsWhiteSpace(sql[_index])) Advance();
        }

        private void Restart(int index, int line, int column)
        {
            _index = index;
            _line = line;
            _column = column;
        }

        private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

        private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
    }
}
