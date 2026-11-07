namespace Databy.Reports.Catalog;

/// <summary>Видимая колонка результата: имя из БД, заголовок и формат из YAML.</summary>
public sealed record ResultColumn(string Name, string Title, string? Format);

/// <summary>
/// Определяет набор и порядок колонок по ответу БД (порядок БД — основной),
/// применяя скрытие из <c>result.hide</c> и <c>result.columns.*.hidden</c>.
/// </summary>
public static class ResultColumns
{
    public static IReadOnlyList<ResultColumn> Resolve(IEnumerable<string> databaseColumns, ResultDefinition? result)
        => Resolve(databaseColumns, result, out _);

    public static IReadOnlyList<ResultColumn> Resolve(IEnumerable<string> databaseColumns, ResultDefinition? result,
        out IReadOnlyList<string> unknownHiddenNames)
    {
        var columns = databaseColumns.ToList();
        var hide = result?.Hide ?? [];
        var options = result?.Columns ?? new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal);

        var unknown = new List<string>();
        foreach (var name in hide)
        {
            if (!columns.Contains(name, StringComparer.Ordinal)) unknown.Add(name);
        }
        unknownHiddenNames = unknown;

        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in hide) hidden.Add(name);
        foreach (var pair in options)
        {
            if (pair.Value.Hidden) hidden.Add(pair.Key);
        }

        var visible = new List<ResultColumn>(columns.Count);
        foreach (var name in columns)
        {
            if (hidden.Contains(name)) continue;

            ColumnDefinition? definition = null;
            foreach (var pair in options)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal))
                {
                    definition = pair.Value;
                    break;
                }
            }

            var title = string.IsNullOrWhiteSpace(definition?.Title) ? name : definition!.Title!;
            visible.Add(new ResultColumn(name, title, definition?.Format));
        }
        return visible;
    }
}
