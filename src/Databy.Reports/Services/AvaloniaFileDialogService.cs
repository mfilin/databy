using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Databy.Reports.Services;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private readonly TopLevel _topLevel;

    public AvaloniaFileDialogService(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<string?> PickExportPathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт отчёта",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices =
            [
                new FilePickerFileType("Excel") { Patterns = ["*.xlsx"] },
                new FilePickerFileType("CSV") { Patterns = ["*.csv"] }
            ]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path.LocalPath;
    }
}
