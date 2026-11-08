namespace Databy.Reports.Input;

/// <summary>
/// Результат разбора списка. <see cref="Stage"/> владеет временным файлом.
/// </summary>
public sealed class InputParseResult : IDisposable
{
    public InputParseResult(InputStage stage, IReadOnlyList<string> sourceColumns,
        IReadOnlyList<InputPreviewRow> preview, IReadOnlyList<InputRowError> errors,
        long totalRows, long validRows, long errorRows)
    {
        Stage = stage;
        SourceColumns = sourceColumns;
        Preview = preview;
        Errors = errors;
        TotalRows = totalRows;
        ValidRows = validRows;
        ErrorRows = errorRows;
    }

    public InputStage Stage { get; }
    public IReadOnlyList<string> SourceColumns { get; }
    public IReadOnlyList<InputPreviewRow> Preview { get; }
    public IReadOnlyList<InputRowError> Errors { get; }
    public long TotalRows { get; }
    public long ValidRows { get; }
    public long ErrorRows { get; }

    public void Dispose() => Stage.Dispose();
}
