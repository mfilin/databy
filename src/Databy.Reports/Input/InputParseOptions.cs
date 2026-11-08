using Databy.Reports.Catalog;

namespace Databy.Reports.Input;

/// <summary>Разделитель колонок в текстовом источнике.</summary>
public enum InputDelimiter
{
    Auto,
    Tab,
    Semicolon,
    Comma,
    None
}

/// <summary>
/// Параметры разбора пользовательского списка.
/// <see cref="ColumnMapping"/> сопоставляет «ключевую колонку контракта → индекс в источнике».
/// </summary>
public sealed class InputParseOptions
{
    public InputDelimiter Delimiter { get; init; } = InputDelimiter.Auto;
    public bool HasHeader { get; init; }
    public IReadOnlyDictionary<string, int> ColumnMapping { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<InputColumnDefinition> Columns { get; init; } = [];
    public int MaxRows { get; init; } = InputDefinition.MaximumRows;
    public int PreviewSize { get; init; } = 100;
    public int MaxErrors { get; init; } = 100;
    public int CancellationCheckInterval { get; init; } = 1000;
}
