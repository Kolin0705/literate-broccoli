namespace ScreenTranslator.Core;

/// <summary>A safe, actionable message suitable for displaying directly to the user.</summary>
public sealed class TranslationException(string message) : Exception(message);
