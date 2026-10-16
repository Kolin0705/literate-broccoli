using Xunit;

namespace ScreenTranslator.Core.Tests;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(" \t\r\n  ", "")]
    [InlineData("  Hello\t  world!  ", "Hello world!")]
    [InlineData("  One\r\n  two\rthree  ", "One\ntwo\nthree")]
    [InlineData("\nFirst.\n\n \t\nSecond.\n\n", "First.\n\nSecond.")]
    [InlineData("Don't “fix” co-op / 42—中文 🐉.", "Don't “fix” co-op / 42—中文 🐉.")]
    [InlineData("a\u00a0\u00a0b", "a b")]
    public void CleansWhitespaceWithoutChangingTextOrParagraphs(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
        Assert.Equal(expected, TextNormalizer.Normalize(TextNormalizer.Normalize(input)));
    }
}
