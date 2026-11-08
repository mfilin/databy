using System.Text;

namespace Databy.Reports.Input;

/// <summary>Определение кодировки текстовых файлов: BOM, строгий UTF-8, иначе Windows-1251.</summary>
internal static class InputEncoding
{
    static InputEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Detect(byte[] bytes, int length)
    {
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false, true);
        if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

        try
        {
            new UTF8Encoding(false, true).GetString(bytes, 0, length);
            return new UTF8Encoding(false, true);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251);
        }
    }

    public static bool HasBom(byte[] bytes, int length) =>
        (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ||
        (length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)));
}
