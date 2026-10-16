namespace MultipleClientsWeb;

/// <summary>
/// Keeps library and framework files out of shared caches (Cloudflare, proxies). Library code builds its
/// own <c>_content</c> urls with only a version query, so a CDN would keep serving the old file behind an unchanged url; and the WebAssembly boot
/// manifest has to stay in step with the deployment the browser talks to. Files resolved through
/// <c>@Assets[...]</c> are fingerprinted by <c>MapStaticAssets</c> and stay publicly cacheable.
/// </summary>
public static class StaticAssetCaching
{
    /// <summary>The browser may keep the file for a day and revalidates afterwards; shared caches may not store it.</summary>
    public const string PrivateCacheControl = "private, max-age=86400, must-revalidate";

    public static IApplicationBuilder UsePrivateLibraryAndFrameworkCaching(this IApplicationBuilder app) =>
        app.Use(async (context, next) => {
            if (IsSharedCacheDenied(context.Request.Path))
                context.Response.OnStarting(() => {
                    if (KeepsCacheControl(context.Response.StatusCode))
                        context.Response.Headers.CacheControl = GetPrivateCacheControl(context.Response.Headers.CacheControl.ToString());
                    return Task.CompletedTask;
                });
            await next();
        });

    /// <summary><c>/_content</c>, <c>/_framework</c> and each client's base path (<c>/admin-client/_framework</c>, ...).</summary>
    public static bool IsSharedCacheDenied(PathString path) =>
        path.StartsWithSegments("/_content")
        || path.StartsWithSegments("/_framework")
        || ClientArea.All.Any(area => path.StartsWithSegments($"/{area.StaticWebAssetBasePath}"));

    /// <summary>
    /// MapStaticAssets marks an unfingerprinted url (e.g. <c>_framework/dotnet.js</c>, which carries the
    /// boot manifest) <c>no-cache</c> so the browser revalidates it on every load. Replacing that with a
    /// max-age would keep the previous deployment's assemblies running until the cache expires.
    /// </summary>
    public static string GetPrivateCacheControl(string current) =>
        current.Contains("no-cache", StringComparison.OrdinalIgnoreCase) ? "private, no-cache" : PrivateCacheControl;

    /// <summary>A served asset; 304 included, since the browser applies its freshness to the copy it holds.</summary>
    private static bool KeepsCacheControl(int statusCode) =>
        statusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices
            or StatusCodes.Status304NotModified;
}
