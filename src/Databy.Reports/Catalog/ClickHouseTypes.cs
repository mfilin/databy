using System.Globalization;

namespace Databy.Reports.Catalog;

public sealed record ClickHouseTypeArgument(ClickHouseType? Type, string? Literal);

public sealed record ClickHouseType(string Name, IReadOnlyList<ClickHouseTypeArgument> Arguments)
{
    public string Normalized => Arguments.Count == 0
        ? Name.ToLowerInvariant()
        : $"{Name.ToLowerInvariant()}({string.Join(",", Arguments.Select(argument =>
            argument.Type?.Normalized ?? argument.Literal ?? ""))})";

    /// <summary>Снимает Nullable/LowCardinality для сравнения с типом параметра YAML.</summary>
    public ClickHouseType Unwrap()
    {
        var type = this;
        while (type.Arguments.Count == 1
               && type.Arguments[0].Type is { } inner
               && (type.Name.Equals("Nullable", StringComparison.OrdinalIgnoreCase)
                   || type.Name.Equals("LowCardinality", StringComparison.OrdinalIgnoreCase)))
        {
            type = inner;
        }
        return type;
    }
}

public static class ClickHouseTypeParser
{
    public static bool TryParse(string text, out ClickHouseType type, out string error)
    {
        var parser = new Parser(text);
        if (!parser.TryParseType(out type, out error)) return false;

        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            error = "лишние символы после типа";
            return false;
        }

        return true;
    }

    private sealed class Parser(string text)
    {
        private int _index;

        public bool AtEnd => _index >= text.Length;

        public void SkipWhitespace()
        {
            while (_index < text.Length && char.IsWhiteSpace(text[_index])) _index++;
        }

        public bool TryParseType(out ClickHouseType type, out string error)
        {
            type = null!;
            error = "";

            SkipWhitespace();
            if (!TryReadIdentifier(out var name))
            {
                error = "ожидается имя типа";
                return false;
            }

            var arguments = new List<ClickHouseTypeArgument>();
            SkipWhitespace();
            if (_index < text.Length && text[_index] == '(')
            {
                _index++;
                SkipWhitespace();
                if (_index < text.Length && text[_index] == ')')
                {
                    error = $"тип {name} не должен иметь пустые аргументы";
                    return false;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (_index >= text.Length)
                    {
                        error = "не закрыта скобка аргументов типа";
                        return false;
                    }

                    if (IsIdentifierStart(text[_index]))
                    {
                        if (!TryParseType(out var nested, out error)) return false;
                        arguments.Add(new ClickHouseTypeArgument(nested, null));
                    }
                    else if (!TryReadLiteral(out var literal))
                    {
                        error = $"некорректный аргумент типа {name}";
                        return false;
                    }
                    else
                    {
                        arguments.Add(new ClickHouseTypeArgument(null, literal));
                    }

                    SkipWhitespace();
                    if (_index >= text.Length)
                    {
                        error = "не закрыта скобка аргументов типа";
                        return false;
                    }

                    if (text[_index] == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (text[_index] == ')')
                    {
                        _index++;
                        break;
                    }

                    error = $"ожидается ',' или ')' в аргументах типа {name}";
                    return false;
                }
            }

            type = new ClickHouseType(name, arguments);
            return true;
        }

        private bool TryReadIdentifier(out string name)
        {
            name = "";
            if (_index >= text.Length || !IsIdentifierStart(text[_index])) return false;

            var start = _index;
            _index++;
            while (_index < text.Length && IsIdentifierPart(text[_index])) _index++;
            name = text[start.._index];
            return true;
        }

        private bool TryReadLiteral(out string literal)
        {
            literal = "";
            if (_index >= text.Length) return false;

            var current = text[_index];
            if (current is '\'' or '"')
            {
                var quote = current;
                _index++;
                var value = new System.Text.StringBuilder();
                while (_index < text.Length)
                {
                    current = text[_index];
                    if (current == quote)
                    {
                        if (_index + 1 < text.Length && text[_index + 1] == quote)
                        {
                            value.Append(quote);
                            _index += 2;
                            continue;
                        }

                        _index++;
                        literal = value.ToString();
                        return true;
                    }

                    if (current == '\\' && _index + 1 < text.Length)
                    {
                        _index++;
                        value.Append(text[_index]);
                        _index++;
                        continue;
                    }

                    value.Append(current);
                    _index++;
                }

                return false;
            }

            var start = _index;
            if (current is '+' or '-') _index++;
            while (_index < text.Length && char.IsDigit(text[_index])) _index++;
            if (_index < text.Length && text[_index] == '.')
            {
                _index++;
                while (_index < text.Length && char.IsDigit(text[_index])) _index++;
            }

            if (_index == start || (_index == start + 1 && current is '+' or '-')) return false;
            literal = text[start.._index];
            return true;
        }

        private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

        private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
    }
}

/// <summary>Правила совместимости типов параметров YAML и плейсхолдеров ClickHouse.</summary>
public static class ClickHouseTypeRules
{
    private static readonly HashSet<string> IntegerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Int8", "Int16", "Int32", "Int64",
        "UInt8", "UInt16", "UInt32", "UInt64"
    };

    public static string DefaultFor(string yamlType) => yamlType.ToLowerInvariant() switch
    {
        "string" => "String",
        "int" => "Int64",
        "decimal" => "Decimal(18,2)",
        "date" => "Date",
        "bool" => "Bool",
        _ => ""
    };

    /// <summary>Проверяет db_type параметра и возвращает действующий тип.</summary>
    public static string? ValidateParameter(ParameterDefinition parameter, out ClickHouseType? effective)
    {
        effective = null;

        var defaultType = DefaultFor(parameter.Type);
        if (defaultType.Length == 0)
            return $"Тип параметра '{parameter.Type}' не поддерживается";

        var text = string.IsNullOrWhiteSpace(parameter.DbType) ? defaultType : parameter.DbType!.Trim();
        if (!ClickHouseTypeParser.TryParse(text, out var type, out var parseError))
            return $"Некорректный db_type для параметра '{parameter.Name}': {parseError}";

        var supportError = ValidateSupported(type);
        if (supportError is not null)
            return $"Некорректный db_type для параметра '{parameter.Name}': {supportError}";

        if (!BelongsToFamily(type, parameter.Type))
            return $"db_type '{text}' несовместим с типом параметра '{parameter.Type}'";

        effective = type;
        return null;
    }

    /// <summary>Проверяет тип конкретного плейсхолдера.</summary>
    public static string? ValidatePlaceholder(
        ClickHouseType placeholder, ParameterDefinition parameter, ClickHouseType effective)
    {
        var supportError = ValidateSupported(placeholder);
        if (supportError is not null)
            return $"Плейсхолдер '{{{parameter.Name}:{placeholder.Normalized}}}': {supportError}";

        if (!BelongsToFamily(placeholder, parameter.Type))
            return $"Тип плейсхолдера '{placeholder.Normalized}' несовместим с параметром " +
                   $"'{parameter.Name}' типа {parameter.Type}";

        if (!string.IsNullOrWhiteSpace(parameter.DbType)
            && !string.Equals(placeholder.Unwrap().Normalized, effective.Unwrap().Normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"Тип плейсхолдера '{placeholder.Normalized}' не совпадает с db_type " +
                   $"'{effective.Normalized}' параметра '{parameter.Name}'";
        }

        return null;
    }

    private static string? ValidateSupported(ClickHouseType type)
    {
        var name = type.Name.ToLowerInvariant();
        switch (name)
        {
            case "nullable":
                if (type.Arguments.Count != 1 || type.Arguments[0].Type is not { } nullableInner)
                    return "Nullable требует ровно один тип";
                if (nullableInner.Name.Equals("Nullable", StringComparison.OrdinalIgnoreCase))
                    return "Nullable не может быть вложенным";
                return ValidateSupported(nullableInner);

            case "lowcardinality":
                if (type.Arguments.Count != 1 || type.Arguments[0].Type is not { } inner)
                    return "LowCardinality требует ровно один тип";
                if (!inner.Name.Equals("String", StringComparison.OrdinalIgnoreCase) || inner.Arguments.Count != 0)
                    return "LowCardinality поддерживается только для String";
                return null;

            case "array":
                return "массивы не поддерживаются; для отчётов по списку используйте input_keys";

            case "string":
                return type.Arguments.Count == 0 ? null : "String не принимает аргументов";

            case "fixedstring":
                return "FixedString не поддерживается для параметров; используйте String";

            case "bool":
                return type.Arguments.Count == 0 ? null : "Bool не принимает аргументов";

            case "date":
            case "date32":
                return type.Arguments.Count == 0 ? null : $"{type.Name} не принимает аргументов";

            case "int8":
            case "int16":
            case "int32":
            case "int64":
            case "uint8":
            case "uint16":
            case "uint32":
            case "uint64":
                return type.Arguments.Count == 0 ? null : $"{type.Name} не принимает аргументов";

            case "decimal":
                return ValidateDecimal(type, 1, 76);

            case "decimal32":
                return ValidateDecimal(type, 1, 9);

            case "decimal64":
                return ValidateDecimal(type, 1, 18);

            case "decimal128":
                return ValidateDecimal(type, 1, 38);

            case "decimal256":
                return ValidateDecimal(type, 1, 76);

            default:
                return $"Тип ClickHouse '{type.Name}' не поддерживается";
        }
    }

    private static string? ValidateDecimal(ClickHouseType type, int minimumPrecision, int maximumScale)
    {
        var expected = type.Name.Equals("Decimal", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        if (type.Arguments.Count != expected)
            return expected == 2
                ? "Decimal требует точность и масштаб: Decimal(p,s)"
                : $"{type.Name} требует масштаб: {type.Name}(s)";

        if (!TryGetInteger(type.Arguments[^1], out var scale) || scale < 0 || scale > maximumScale)
            return $"Некорректный масштаб {type.Name}";

        if (expected == 1) return null;

        if (!TryGetInteger(type.Arguments[0], out var precision)
            || precision < minimumPrecision || precision > 76 || precision < scale)
        {
            return "Некорректная точность Decimal(p,s)";
        }

        return null;
    }

    private static bool TryGetInteger(ClickHouseTypeArgument argument, out int value)
    {
        value = 0;
        return argument.Type is null
               && int.TryParse(argument.Literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool BelongsToFamily(ClickHouseType type, string yamlType)
    {
        var unwrapped = type.Unwrap();
        var name = unwrapped.Name.ToLowerInvariant();
        return yamlType.ToLowerInvariant() switch
        {
            "string" => name == "string" && unwrapped.Arguments.Count == 0,
            "int" => IntegerTypes.Contains(unwrapped.Name) && unwrapped.Arguments.Count == 0,
            "decimal" => name is "decimal" or "decimal32" or "decimal64" or "decimal128" or "decimal256",
            "date" => (name is "date" or "date32") && unwrapped.Arguments.Count == 0,
            "bool" => name == "bool" && unwrapped.Arguments.Count == 0,
            _ => false
        };
    }
}