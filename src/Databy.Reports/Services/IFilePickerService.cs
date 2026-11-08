namespace Databy.Reports.Services;

/// <summary>Выбор файла со списком значений (csv, txt, xlsx).</summary>
public interface IFilePickerService
{
    Task<string?> PickInputFileAsync(CancellationToken cancellationToken = default);
}
