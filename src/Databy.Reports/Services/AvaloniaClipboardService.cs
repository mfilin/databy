using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Databy.Reports.Services;

/// <summary>Буфер обмена Avalonia.</summary>
public sealed class AvaloniaClipboardService(TopLevel topLevel) : IClipboardService
{
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        var clipboard = topLevel.Clipboard;
        if (clipboard is null) return null;
        var text = await clipboard.TryGetTextAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }
}
