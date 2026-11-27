using System.Security.Cryptography;
using System.Text;

namespace Databy.Reports.Catalog;

/// <summary>Проверенный SQL-файл каталога. Текст читается один раз при загрузке каталога.</summary>
public sealed record SqlScript(string RelativePath, string FullPath, string Text, string Hash);

public sealed class SqlFileException(string message) : Exception(message);

/// <summary>
/// Загрузка SQL-файла по ADR-005: только внутри корня каталога, относительный путь,
/// расширение .sql, UTF-8, не более 256 КБ.
/// </summary>
public static class SqlFileLoader
{
    public const int MaximumBytes = 256 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static SqlScript Load(string catalogRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new SqlFileException("не задан routine.file");

        var normalized = relativePath.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized))
            throw new SqlFileException($"путь должен быть относительным: {relativePath}");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new SqlFileException($"путь не задан: {relativePath}");
        if (segments.Any(segment => segment == ".."))
            throw new SqlFileException($"путь не должен содержать '..': {relativePath}");
        if (segments.Any(segment => segment.Contains(':')))
            throw new SqlFileException($"путь не должен содержать ':': {relativePath}");

        if (!string.Equals(Path.GetExtension(normalized), ".sql", StringComparison.OrdinalIgnoreCase))
            throw new SqlFileException($"файл должен иметь расширение .sql: {relativePath}");

        var root = Path.GetFullPath(catalogRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInside(root, fullPath, relativePath);
        EnsureNoLinkEscape(root, segments, relativePath);

        if (!File.Exists(fullPath))
            throw new SqlFileException($"файл не найден: {relativePath}");

        var length = new FileInfo(fullPath).Length;
        if (length > MaximumBytes)
            throw new SqlFileException(
                $"размер {length} байт превышает предел {MaximumBytes} байт: {relativePath}");

        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length > MaximumBytes)
            throw new SqlFileException(
                $"размер {bytes.Length} байт превышает предел {MaximumBytes} байт: {relativePath}");

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new SqlFileException($"файл должен быть в кодировке UTF-8: {relativePath}");
        }

        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new SqlScript(string.Join('/', segments), fullPath, text, hash);
    }

    private static void EnsureInside(string root, string fullPath, string relativePath)
    {
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, PathComparison))
            throw new SqlFileException($"путь выходит за пределы каталога: {relativePath}");
    }

    /// <summary>
    /// Не даёт symlink/junction увести файл за корень каталога. Проверка не является
    /// границей безопасности, но закрывает очевидный обход относительного пути.
    /// </summary>
    private static void EnsureNoLinkEscape(string root, IReadOnlyList<string> segments, string relativePath)
    {
        var current = root;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment is "" or ".") continue;
            current = Path.Combine(current, segment.Replace('/', Path.DirectorySeparatorChar));

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (!info.Exists || info.LinkTarget is null) continue;

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
                throw new SqlFileException($"не удалось проверить ссылку: {relativePath}");

            var targetPath = Path.GetFullPath(target.FullName);
            if (!IsInside(root, targetPath))
                throw new SqlFileException($"ссылка выводит за пределы каталога: {relativePath}");

            current = i < segments.Count - 1 ? targetPath : current;
        }
    }

    private static bool IsInside(string root, string path)
    {
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}