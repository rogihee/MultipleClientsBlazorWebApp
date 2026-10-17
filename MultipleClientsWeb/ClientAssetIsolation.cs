using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.StaticAssets;

namespace MultipleClientsWeb;

/// <summary>
/// Keeps each WebAssembly client to its own area. By default a Blazor Web App puts the assets of every client in the
/// import map of every page and in one public <c>_framework/resource-collection.js</c>, and serves every client's
/// <c>_framework</c> folder to anonymous users. That tells anyone which assemblies the admin client contains, and
/// lets them download and decompile it.
/// </summary>
public static class ClientAssetIsolation
{
    /// <summary>
    /// Only users who may open an area can download its client. JS initializers (<c>*.lib.module.js</c>) stay public:
    /// <c>blazor.web.js</c> loads the initializers of all clients on every page, the static SSR pages included.
    /// </summary>
    public static StaticAssetsEndpointConventionBuilder RequireClientAreaAuthorization(this StaticAssetsEndpointConventionBuilder builder)
    {
        builder.Add(endpoint => {
            if (endpoint is RouteEndpointBuilder { RoutePattern.RawText: { } pattern }
                && !pattern.EndsWith(".lib.module.js", StringComparison.OrdinalIgnoreCase)
                && ClientArea.FromAssetPath(ToPath(pattern)) is { } area)
                endpoint.Metadata.Add(new AuthorizeAttribute { Roles = area.RequiredRole });
        });
        return builder;
    }

    /// <summary>
    /// Gives each page the assets of the host, the shared libraries and its own area only, so the import map and
    /// <c>@Assets[...]</c> no longer list the other clients. Also drops the url of the app-wide resource collection,
    /// so the WebAssembly clients get an empty <c>Assets</c> collection; build client urls with
    /// <see cref="MultipleClientShared.SharedAssets"/> instead.
    /// </summary>
    public static RazorComponentsEndpointConventionBuilder WithClientAreaAssetsOnly(this RazorComponentsEndpointConventionBuilder builder)
    {
        var areaCollections = new ConcurrentDictionary<(ResourceAssetCollection All, ClientArea? Area), ResourceAssetCollection>();

        // Finally runs after the framework has added the app-wide collection, import map and resource collection url.
        builder.Finally(endpoint => {
            if (endpoint is not RouteEndpointBuilder { RoutePattern.RawText: { } pattern }
                || endpoint.Metadata.OfType<ResourceAssetCollection>().LastOrDefault() is not { } all)
                return;

            var area = ClientArea.FromPath(ToPath(pattern));
            var own = areaCollections.GetOrAdd((all, area), key => new ResourceAssetCollection(
                [.. ((IReadOnlyList<ResourceAsset>)key.All).Where(asset => ClientArea.FromAssetPath(ToPath(asset.Url)) is var owner && (owner is null || owner == key.Area))]));

            for (var i = endpoint.Metadata.Count - 1; i >= 0; i--)
                if (endpoint.Metadata[i] is ResourceAssetCollection or ImportMapDefinition
                    || endpoint.Metadata[i].GetType().Name is "ResourceCollectionUrlMetadata" or "ResourcePreloadCollection")
                    endpoint.Metadata.RemoveAt(i);

            endpoint.Metadata.Add(own);
            endpoint.Metadata.Add(ImportMapDefinition.FromResourceCollection(own));
        });
        return builder;
    }

    /// <summary>
    /// Hides the app-wide <c>_framework/resource-collection.js</c> that <c>MapRazorComponents</c> maps for WebAssembly.
    /// It lists the files of every client and is not used after <see cref="WithClientAreaAssetsOnly"/>.
    /// </summary>
    public static IApplicationBuilder UseNoAppWideResourceCollection(this IApplicationBuilder app) =>
        app.Use((context, next) => {
            if (IsAppWideResourceCollection(context.Request.Path)) {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            return next(context);
        });

    public static bool IsAppWideResourceCollection(PathString path) =>
        path.StartsWithSegments("/_framework", out var rest)
        && rest.Value?.StartsWith("/resource-collection.", StringComparison.OrdinalIgnoreCase) == true;

    private static PathString ToPath(string routeOrUrl) => new($"/{routeOrUrl.TrimStart('/')}");
}
