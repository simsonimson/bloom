using Microsoft.Windows.ApplicationModel.Resources;

namespace Bloom.App.Services;

/// <summary>Access to localized strings from Strings/&lt;lang&gt;/Resources.resw for code-behind.</summary>
public static class Strings
{
    private static readonly Lazy<ResourceLoader> Loader = new(() => new ResourceLoader());

    public static string Get(string key)
    {
        try
        {
            var value = Loader.Value.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}
