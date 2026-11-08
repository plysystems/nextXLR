using Microsoft.Windows.ApplicationModel.Resources;

namespace NextXLR.Services;

/// <summary>
/// Localized strings for code. XAML uses <c>x:Uid</c> against the same files:
/// <c>Strings/en-US/Resources.resw</c>, <c>Strings/de-DE/Resources.resw</c>.
/// MRT Core picks the language from the Windows display language automatically; anything
/// without a translation falls back to English (the default language in the .csproj).
/// To add a language, copy <c>Strings/en-US</c> to e.g. <c>Strings/fr-FR</c> and translate.
/// </summary>
public static class Loc
{
    private static readonly ResourceLoader? Loader = TryCreate();

    private static ResourceLoader? TryCreate()
    {
        try { return new ResourceLoader(); } catch { return null; }
    }

    /// <summary>Code keys use plain names (no dots — dots are reserved for x:Uid property keys).</summary>
    public static string Get(string key)
    {
        try
        {
            var s = Loader?.GetString(key);
            return string.IsNullOrEmpty(s) ? key : s;
        }
        catch { return key; }
    }

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}
