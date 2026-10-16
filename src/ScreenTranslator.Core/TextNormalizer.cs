using System.Text;

namespace ScreenTranslator.Core;

public static class TextNormalizer
{
    /// <summary>Normalize OCR whitespace while preserving line breaks and paragraph boundaries.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var result = new StringBuilder();
        var paragraphBreak = false;

        foreach (var rawLine in lines)
        {
            var line = NormalizeLine(rawLine);
            if (line.Length == 0)
            {
                if (result.Length > 0)
                    paragraphBreak = true;
                continue;
            }

            if (result.Length > 0)
                result.Append(paragraphBreak ? "\n\n" : "\n");
            result.Append(line);
            paragraphBreak = false;
        }

        return result.ToString();
    }

    private static string NormalizeLine(string line)
    {
        var result = new StringBuilder();
        var pendingSpace = false;
        foreach (var character in line)
        {
            if (char.IsWhiteSpace(character))
            {
                if (result.Length > 0)
                    pendingSpace = true;
                continue;
            }

            if (pendingSpace)
                result.Append(' ');
            result.Append(character);
            pendingSpace = false;
        }
        return result.ToString();
    }
}
