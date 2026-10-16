namespace ScreenTranslator.Core;

public sealed record TranslationCredentials(string AppId, string SecretKey)
{
    // Records otherwise print every property, including the secret, in logs.
    public override string ToString() => "TranslationCredentials { AppId = [redacted], SecretKey = [redacted] }";
}
