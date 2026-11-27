namespace Databy.Reports.Catalog;

public sealed record SqlValidationIssue(string Message, int Line = 0, int Column = 0);

/// <summary>
/// Статические проверки SQL-файла по ADR-005. Проверки не являются границей
/// безопасности: основная защита — права пользователя ClickHouse.
/// </summary>
public static class SqlValidator
{
    private static readonly HashSet<string> ForbiddenStatements = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "ALTER", "DROP", "CREATE", "ATTACH", "DETACH", "RENAME", "TRUNCATE",
        "OPTIMIZE", "SYSTEM", "KILL", "GRANT", "SET", "USE"
    };

    private static readonly HashSet<string> ExternalTableFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "file", "s3", "remote", "remoteSecure", "mysql", "postgresql", "jdbc",
        "odbc", "hdfs", "input", "executable"
    };

    private static readonly HashSet<string> ClauseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "FROM", "WHERE", "PREWHERE", "GROUP", "ORDER", "BY", "HAVING",
        "LIMIT", "OFFSET", "UNION", "ALL", "DISTINCT", "JOIN", "LEFT", "RIGHT", "INNER",
        "OUTER", "FULL", "CROSS", "ON", "USING", "AS", "INTO", "SETTINGS", "FORMAT",
        "ARRAY", "SAMPLE", "FINAL", "AND", "OR", "NOT", "IN", "IS", "CASE",
        "WHEN", "THEN", "ELSE", "END", "ASC", "DESC", "INTERVAL", "LIKE", "ILIKE",
        "BETWEEN", "EXISTS", "QUALIFY", "WINDOW", "TOP"
    };

    public static IReadOnlyList<SqlValidationIssue> Validate(
        string sql, IReadOnlyList<ParameterDefinition> parameters, bool hasInput)
    {
        var issues = new List<SqlValidationIssue>();

        IReadOnlyList<SqlToken> tokens;
        try
        {
            tokens = SqlTokenizer.Tokenize(sql);
        }
        catch (SqlSyntaxException exception)
        {
            issues.Add(new(exception.Message, exception.Line, exception.Column));
            return issues;
        }

        if (tokens.Count == 0)
        {
            issues.Add(new("SQL-файл пуст"));
            return issues;
        }

        var semicolons = tokens
            .Select((token, index) => (token, index))
            .Where(item => item.token.Kind == SqlTokenKind.Punctuation && item.token.Text == ";")
            .Select(item => item.index)
            .ToArray();

        if (semicolons.Length > 1)
            issues.Add(At(tokens[semicolons[1]], "Допускается только одна завершающая ';'"));
        else if (semicolons.Length == 1 && semicolons[0] != tokens.Count - 1)
            issues.Add(At(tokens[semicolons[0]], "После ';' не должно быть второго оператора"));

        var end = semicolons.Length > 0 ? semicolons[0] : tokens.Count;
        var body = tokens.Take(end).ToArray();
        if (body.Length == 0)
        {
            issues.Add(new("SQL-файл пуст"));
            return issues;
        }

        if (!IsWord(body[0], "SELECT") && !IsWord(body[0], "WITH"))
            issues.Add(At(body[0], "Первый оператор должен быть SELECT или WITH"));
        else if (IsWord(body[0], "WITH") && !HasTopLevelSelect(body))
            issues.Add(At(body[0], "После WITH ожидается SELECT"));

        var depth = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var token = body[i];
            if (token.Kind == SqlTokenKind.Punctuation)
            {
                if (token.Text == "(")
                {
                    depth++;
                }
                else if (token.Text == ")")
                {
                    depth--;
                    if (depth < 0)
                    {
                        issues.Add(At(token, "Лишняя закрывающая скобка"));
                        depth = 0;
                    }
                }
                continue;
            }

            if (token.Kind != SqlTokenKind.Word) continue;

            var word = token.Text.ToUpperInvariant();
            var previous = i > 0 ? body[i - 1] : null;
            var next = i + 1 < body.Length ? body[i + 1] : null;

            if (depth == 0 && ForbiddenStatements.Contains(word) && !IsIdentifierUse(word, previous, next))
                issues.Add(At(token, $"Запрещённое ключевое слово {word}"));

            if (word == "FORMAT" && IsClauseAtExpressionEnd(previous, next))
                issues.Add(At(token, "Предложение FORMAT запрещено"));
            else if (word == "SETTINGS" && IsClauseAtExpressionEnd(previous, next))
                issues.Add(At(token, "Предложение SETTINGS запрещено"));
            else if (word == "INTO" && next is { Kind: SqlTokenKind.Word } outfile
                     && outfile.Text.Equals("OUTFILE", StringComparison.OrdinalIgnoreCase))
                issues.Add(At(token, "INTO OUTFILE запрещён"));
        }

        if (depth != 0)
            issues.Add(new("Несбалансированные скобки: не хватает закрывающей ')'"));

        for (var i = 0; i + 1 < body.Length; i++)
        {
            var token = body[i];
            if (token.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)
                || !ExternalTableFunctions.Contains(token.Text))
            {
                continue;
            }

            if (body[i + 1].Kind == SqlTokenKind.Punctuation && body[i + 1].Text == "(")
                issues.Add(At(token, $"Табличная функция {token.Text} запрещена"));
        }

        if (hasInput && HasReservedInputKeysCte(body))
            issues.Add(new("Имя input_keys зарезервировано для временной таблицы списка"));

        ValidateSingleStatement(body, issues);
        ValidatePlaceholders(body, parameters, issues);
        return issues;
    }

    private static void ValidatePlaceholders(
        IReadOnlyList<SqlToken> tokens,
        IReadOnlyList<ParameterDefinition> parameters,
        List<SqlValidationIssue> issues)
    {
        foreach (var invalid in tokens.Where(token => token.Kind == SqlTokenKind.InvalidPlaceholder))
        {
            issues.Add(At(invalid,
                $"Некорректный плейсхолдер '{invalid.Text}': ожидается {{имя:Тип}}"));
        }

        // {имя} — не плейсхолдер и не map-литерал ClickHouse.
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i] is { Kind: SqlTokenKind.Punctuation, Text: "{" }
                && tokens[i + 1].Kind == SqlTokenKind.Word
                && tokens[i + 2] is { Kind: SqlTokenKind.Punctuation, Text: "}" })
            {
                issues.Add(At(tokens[i],
                    $"Некорректный плейсхолдер '{{{tokens[i + 1].Text}}}': ожидается {{имя:Тип}}"));
            }
        }

        var placeholders = tokens.Where(token => token.Kind == SqlTokenKind.Placeholder).ToArray();
        var placeholderNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placeholder in tokens.Where(token =>
                     token.Kind == SqlTokenKind.Placeholder || token.Kind == SqlTokenKind.InvalidPlaceholder))
        {
            if (!string.IsNullOrEmpty(placeholder.Name)) placeholderNames.Add(placeholder.Name);
        }

        var parameterMap = new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal);
        foreach (var parameter in parameters) parameterMap.TryAdd(parameter.Name, parameter);

        var effectiveTypes = new Dictionary<string, ClickHouseType>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            var issue = ClickHouseTypeRules.ValidateParameter(parameter, out var effective);
            if (issue is not null)
            {
                issues.Add(new(issue));
                continue;
            }

            effectiveTypes[parameter.Name] = effective!;
        }

        var placeholderTypes = new Dictionary<string, ClickHouseType>(StringComparer.Ordinal);
        var extraReported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placeholder in placeholders)
        {
            var name = placeholder.Name ?? "";
            if (!parameterMap.TryGetValue(name, out var parameter))
            {
                if (extraReported.Add(name))
                    issues.Add(At(placeholder, $"Плейсхолдер '{name}' не описан в parameters"));
                continue;
            }

            if (!ClickHouseTypeParser.TryParse(placeholder.TypeText ?? "", out var type, out var parseError))
            {
                issues.Add(At(placeholder, $"Некорректный тип плейсхолдера '{name}': {parseError}"));
                continue;
            }

            if (placeholderTypes.TryGetValue(name, out var existing))
            {
                if (!string.Equals(existing.Normalized, type.Normalized, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(At(placeholder,
                        $"Плейсхолдер '{name}' указан с разными типами: " +
                        $"{existing.Normalized} и {type.Normalized}"));
                }
            }
            else
            {
                placeholderTypes[name] = type;
            }

            if (!effectiveTypes.TryGetValue(name, out var effective)) continue;

            var issue = ClickHouseTypeRules.ValidatePlaceholder(type, parameter, effective);
            if (issue is not null) issues.Add(At(placeholder, issue));
        }

        foreach (var parameter in parameters)
        {
            if (!placeholderNames.Contains(parameter.Name))
                issues.Add(new($"Параметр '{parameter.Name}' не используется в SQL-файле"));
        }
    }

    private static bool HasTopLevelSelect(IReadOnlyList<SqlToken> tokens)
    {
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == SqlTokenKind.Punctuation)
            {
                if (token.Text == "(") depth++;
                else if (token.Text == ")") depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0 && IsWord(token, "SELECT")) return true;
        }

        return false;
    }

    /// <summary>
    /// Второй оператор без ';' (например, "SELECT 1 SELECT 2"). На верхнем уровне
    /// дополнительный SELECT допустим только как часть UNION/EXCEPT/INTERSECT.
    /// </summary>
    private static void ValidateSingleStatement(
        IReadOnlyList<SqlToken> tokens, List<SqlValidationIssue> issues)
    {
        var depth = 0;
        var allowStatementSelect = IsWord(tokens[0], "WITH");
        SqlToken? previous = null;

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == SqlTokenKind.Punctuation)
            {
                if (token.Text == "(") depth++;
                else if (token.Text == ")") depth = Math.Max(0, depth - 1);
                else if (depth == 0) previous = token;
                continue;
            }

            if (depth != 0) continue;

            if (IsWord(token, "SELECT"))
            {
                if (allowStatementSelect)
                {
                    allowStatementSelect = false;
                    previous = token;
                    continue;
                }

                if (previous is { Kind: SqlTokenKind.Word } previousWord
                    && previousWord.Text.ToUpperInvariant() is "UNION" or "EXCEPT" or "INTERSECT"
                        or "ALL" or "DISTINCT")
                {
                    previous = token;
                    continue;
                }

                issues.Add(At(token, "Похоже на второй оператор: допускается один запрос"));
                return;
            }

            if (IsWord(token, "WITH"))
            {
                issues.Add(At(token, "Похоже на второй оператор: допускается один запрос"));
                return;
            }

            previous = token;
        }
    }

    private static bool HasReservedInputKeysCte(IReadOnlyList<SqlToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)
                || !token.Text.Equals("input_keys", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Колонка вида t.input_keys не является объявлением CTE.
            if (i > 0 && tokens[i - 1].Kind == SqlTokenKind.Punctuation && tokens[i - 1].Text == ".")
                continue;

            if (i + 1 >= tokens.Count) continue;
            var next = tokens[i + 1];

            if (next.Kind == SqlTokenKind.Word && next.Text.Equals("AS", StringComparison.OrdinalIgnoreCase))
                return true;

            // CTE со списком колонок: input_keys(row_id) AS (...)
            if (next.Kind == SqlTokenKind.Punctuation && next.Text == "("
                && IsFollowedByAs(tokens, i + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFollowedByAs(IReadOnlyList<SqlToken> tokens, int openParenthesisIndex)
    {
        var depth = 0;
        for (var i = openParenthesisIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != SqlTokenKind.Punctuation) continue;
            if (token.Text == "(")
            {
                depth++;
            }
            else if (token.Text == ")")
            {
                depth--;
                if (depth == 0)
                {
                    return i + 1 < tokens.Count
                           && IsWord(tokens[i + 1], "AS");
                }
            }
        }

        return false;
    }

    private static bool IsIdentifierUse(string word, SqlToken? previous, SqlToken? next)
    {
        if (previous is { Kind: SqlTokenKind.Punctuation, Text: "." }) return true;
        if (previous is { Kind: SqlTokenKind.Word } previousWord
            && previousWord.Text.Equals("AS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // system.tables и колонки system/set/use не должны путаться с командами SYSTEM/SET/USE.
        if (word is "SYSTEM" or "SET" or "USE")
        {
            if (next is { Kind: SqlTokenKind.Punctuation, Text: "." }) return true;
            if (next is { Kind: SqlTokenKind.Word } nextWord && ClauseKeywords.Contains(nextWord.Text))
                return true;
        }

        return false;
    }

    private static bool IsClauseAtExpressionEnd(SqlToken? previous, SqlToken? next)
    {
        if (next is not { Kind: SqlTokenKind.Word } nextWord || previous is null) return false;
        if (ClauseKeywords.Contains(nextWord.Text)) return false;

        if (previous.Kind == SqlTokenKind.Punctuation)
            return previous.Text is ")" or "]" or "}" or "*";

        if (previous.Kind == SqlTokenKind.Word)
            return !ClauseKeywords.Contains(previous.Text);

        return previous.Kind is SqlTokenKind.String or SqlTokenKind.Number or SqlTokenKind.Placeholder;
    }

    private static bool IsWord(SqlToken token, string word) =>
        token.Kind == SqlTokenKind.Word && token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);

    private static SqlValidationIssue At(SqlToken token, string message) =>
        new(message, token.Line, token.Column);
}