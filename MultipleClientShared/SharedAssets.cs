using System.Reflection;

namespace MultipleClientShared;

/// <summary>
/// URLs of the files this library ships under <c>_content/MultipleClientShared</c>. The SDK fingerprints
/// them only when a page resolves them through <c>@Assets[...]</c>, so code that builds the url itself
/// (library code, the WebAssembly clients, a Razor Pages host) appends the package version instead.
/// </summary>
public static class SharedAssets
{
    public const string BaseUrl = "_content/MultipleClientShared/";
    public const string CssBundle = $"{BaseUrl}shared.css";
    public const string Logo = $"{BaseUrl}images/logo.svg";
    public const string AssetInfoModule = "js/asset-info.js";

    public static readonly string Version = PackageVersion(typeof(SharedAssets).Assembly);

    public static string AppendVersion(string url) => $"{url}{(url.Contains('?') ? '&' : '?')}v={Version}";

    /// <summary>URL for a JS module in this library, for <c>import()</c> through JS interop.</summary>
    public static string ModuleUrl(string path) => $"./{BaseUrl}{path}?v={Version}";

    private static string PackageVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString()
        ?? "";
}
