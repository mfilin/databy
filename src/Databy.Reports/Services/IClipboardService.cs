namespace Databy.Reports.Services;

/// <summary>Чтение текста из системного буфера обмена.</summary>
public interface IClipboardService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}
