using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Databy.Reports.Catalog;

public sealed record CatalogFileError(string File, string Message);
public sealed record ReportCatalog(string Version, IReadOnlyList<ReportDefinition> Reports, IReadOnlyList<CatalogFileError> Errors, IReadOnlyList<string> Folders);

public sealed class ReportCatalogLoader
{
    private static readonly HashSet<string> ParameterTypes = new(StringComparer.OrdinalIgnoreCase)
        { "date", "daterange", "int", "decimal", "string", "bool", "lookup" };
    private static readonly HashSet<string> UnsupportedTypes = new(StringComparer.OrdinalIgnoreCase) { "daterange", "lookup" };
    private static readonly HashSet<string> RoutineKinds = new(StringComparer.OrdinalIgnoreCase) { "function" };
    private static readonly HashSet<string> Roles = new(StringComparer.OrdinalIgnoreCase) { "admin", "user" };
    private static readonly HashSet<string> InputColumnTypes = new(StringComparer.OrdinalIgnoreCase) { "string", "int", "decimal", "date", "bool" };
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    public ReportCatalog Load(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Каталог отчётов не найден: {path}");
        var reports = new List<ReportDefinition>();
        var errors = new List<CatalogFileError>();
        var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(path, "*.yml", SearchOption.AllDirectories)
                     .Where(f => !string.Equals(Path.GetFileName(f), "_folder.yml", StringComparison.OrdinalIgnoreCase)))
        {
            ReportDefinition? report;
            try { report = _deserializer.Deserialize<ReportDefinition>(File.ReadAllText(file)); }
            catch (Exception ex) { errors.Add(new(file, ex.Message)); continue; }
            var issue = Validate(report);
            if (issue is not null) { errors.Add(new(file, issue)); continue; }
            if (!codes.Add(report!.Code)) { errors.Add(new(file, $"Повторяется code '{report.Code}'")); continue; }
            reports.Add(report);
            var folder = Path.GetRelativePath(path, Path.GetDirectoryName(file)!);
            if (folder != ".")
            {
                var current = "";
                foreach (var segment in folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                { current = current.Length == 0 ? segment : Path.Combine(current, segment); folders.Add(current); }
            }
        }
        var versionFile = Path.Combine(path, "catalog.version");
        return new(File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "не указана", reports, errors, folders.ToArray());
    }

    private static string? Validate(ReportDefinition? report)
    {
        if (report is null) return "Файл YAML пуст";
        if (string.IsNullOrWhiteSpace(report.Code)) return "Не задан code";
        if (string.IsNullOrWhiteSpace(report.Title)) return "Не задан title";
        if (report.Routine is null || string.IsNullOrWhiteSpace(report.Routine.Name)) return "Не задан routine.name";
        if (!Regex.IsMatch(report.Routine.Name, @"^[a-zA-Z_][a-zA-Z0-9_]*\.[a-zA-Z_][a-zA-Z0-9_]*$")) return "routine.name должен иметь вид schema.function";
        if (!RoutineKinds.Contains(report.Routine.Kind)) return $"routine.kind '{report.Routine.Kind}' пока не поддерживается (допустимо: function)";
        if (report.Routine.TimeoutSec <= 0) return $"routine.timeout_sec должен быть больше нуля (сейчас {report.Routine.TimeoutSec})";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in report.Parameters)
        {
            if (!Regex.IsMatch(parameter.Name, @"^[a-zA-Z_][a-zA-Z0-9_]*$")) return $"Недопустимое имя параметра '{parameter.Name}'";
            if (!names.Add(parameter.Name)) return $"Повторяется имя параметра '{parameter.Name}'";
            if (UnsupportedTypes.Contains(parameter.Type)) return $"Тип параметра '{parameter.Type}' пока не поддерживается";
            if (!ParameterTypes.Contains(parameter.Type)) return $"Неизвестный тип параметра '{parameter.Type}'";
        }

        foreach (var parameter in report.Parameters)
        {
            try { ParameterDefaults.Resolve(parameter, DateOnly.FromDateTime(DateTime.Today)); }
            catch (FormatException ex) { return ex.Message; }
        }

        var resultIssue = ValidateResult(report.Result);
        if (resultIssue is not null) return resultIssue;

        var inputIssue = ValidateInput(report);
        if (inputIssue is not null) return inputIssue;

        var invalidRole = report.Roles.FirstOrDefault(role => !Roles.Contains(role));
        return invalidRole is null ? null : $"Недопустимая роль '{invalidRole}'";
    }

    private static string? ValidateInput(ReportDefinition report)
    {
        var input = report.Input;
        if (input is null) return null;

        if (input.Columns.Count == 0) return "В input.columns должна быть хотя бы одна колонка";

        if (input.MaxRows < 1 || input.MaxRows > InputDefinition.MaximumRows)
            return $"input.max_rows должен быть в диапазоне 1..{InputDefinition.MaximumRows} (сейчас {input.MaxRows})";
        if (input.BatchSize < 1 || input.BatchSize > input.MaxRows)
            return $"input.batch_size должен быть в диапазоне 1..max_rows (сейчас {input.BatchSize}, max_rows {input.MaxRows})";
        if (string.IsNullOrWhiteSpace(input.ResultKey)) return "Не задан input.result_key";
        if (string.Equals(input.StatusColumn, input.ResultKey, StringComparison.Ordinal))
            return $"input.status_column не должен совпадать с input.result_key ('{input.ResultKey}')";

        if (!Regex.IsMatch(input.RowIdParameter, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            return $"Недопустимое имя input.row_id_parameter '{input.RowIdParameter}'";

        var parameterNames = new HashSet<string>(report.Parameters.Select(parameter => parameter.Name), StringComparer.OrdinalIgnoreCase);
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in input.Columns)
        {
            if (!Regex.IsMatch(column.Name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                return $"Недопустимое имя колонки input '{column.Name}'";
            if (columnNames.Contains(column.Name))
                return $"Повторяется имя колонки input '{column.Name}'";
            if (parameterNames.Contains(column.Name))
                return $"Колонка input '{column.Name}' совпадает с именем параметра отчёта";
            if (!InputColumnTypes.Contains(column.Type))
                return $"Неизвестный тип колонки input '{column.Type}' (допустимо: string, int, decimal, date, bool)";
            columnNames.Add(column.Name);
        }

        return null;
    }

    private static string? ValidateResult(ResultDefinition? result)
    {
        if (result is null || result.Hide.Count == 0) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in result.Hide)
        {
            if (string.IsNullOrWhiteSpace(name)) return "В result.hide указано пустое имя колонки";
            if (!seen.Add(name)) return $"В result.hide повторяется колонка '{name}'";
            if (result.Columns.TryGetValue(name, out var definition) && !definition.Hidden)
                return $"Колонка '{name}' одновременно указана в result.hide и имеет hidden: false";
        }
        return null;
    }

    public static DateOnly ResolveDateDefault(string value, DateOnly today) => value switch
    {
        "today" => today,
        "month_start" => new DateOnly(today.Year, today.Month, 1),
        _ when DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date,
        _ => throw new ArgumentException($"Неизвестное значение даты по умолчанию: {value}", nameof(value))
    };
}

public sealed class ReportDefinition
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public RoutineDefinition Routine { get; set; } = new();
    public List<ParameterDefinition> Parameters { get; set; } = [];
    public ResultDefinition? Result { get; set; }
    public InputDefinition? Input { get; set; }
    public List<string> Roles { get; set; } = [];
    public override string ToString() => Title;
}
public sealed class RoutineDefinition { public string Name { get; set; } = ""; public string Kind { get; set; } = "function"; public int TimeoutSec { get; set; } = 60; }
public sealed class ParameterDefinition { public string Name { get; set; } = ""; public string Type { get; set; } = ""; public string Label { get; set; } = ""; public bool Required { get; set; } public string? Default { get; set; } }
public sealed class ResultDefinition
{
    public string? Sheet { get; set; }
    public List<string> Hide { get; set; } = [];
    public Dictionary<string, ColumnDefinition> Columns { get; set; } = new(StringComparer.Ordinal);
}
public sealed class ColumnDefinition { public string? Title { get; set; } public string? Format { get; set; } public bool Hidden { get; set; } }
