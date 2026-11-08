using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Databy.Reports.Services;

/// <summary>Выбор файла списка через StorageProvider Avalonia.</summary>
public sealed class AvaloniaFilePickerService(TopLevel topLevel) : IFilePickerService
{
    public async Task<string?> PickInputFileAsync(CancellationToken cancellationToken = default)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Загрузить список значений",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Список значений") { Patterns = ["*.csv", "*.txt", "*.tsv", "*.xlsx"] },
                new FilePickerFileType("CSV") { Patterns = ["*.csv"] },
                new FilePickerFileType("Текст") { Patterns = ["*.txt", "*.tsv"] },
                new FilePickerFileType("Excel") { Patterns = ["*.xlsx"] }
            ]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }
}
