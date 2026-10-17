using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// <c>@Assets[...]</c> no longer list the other clients. Replaces the url of the app-wide resource collection: an
    /// area page points its WebAssembly client at the area's own collection (see
    /// <see cref="MapClientAreaResourceCollections"/>), so <c>@Assets[...]</c> keeps working in the client; other pages
    /// get no url.
    /// </summary>
    public static RazorComponentsEndpointConventionBuilder WithClientAreaAssetsOnly(this RazorComponentsEndpointConventionBuilder builder)
    {
        var areaCollections = new ConcurrentDictionary<(ResourceAssetCollection All, ClientArea? Area), (ResourceAssetCollection Own, ClientAreaResourceCollection? File)>();

        // Finally runs after the framework has added the app-wide collection, import map and resource collection url.
        builder.Finally(endpoint => {
            if (endpoint is not RouteEndpointBuilder { RoutePattern.RawText: { } pattern }
                || endpoint.Metadata.OfType<ResourceAssetCollection>().LastOrDefault() is not { } all)
                return;

            var area = ClientArea.FromPath(ToPath(pattern));
            var (own, file) = areaCollections.GetOrAdd((all, area), key => {
                var own = new ResourceAssetCollection(
                    [.. ((IReadOnlyList<ResourceAsset>)key.All).Where(asset => ClientArea.FromAssetPath(ToPath(asset.Url)) is var owner && (owner is null || owner == key.Area))]);
                return (own, key.Area is null ? null : new ClientAreaResourceCollection(key.Area, own));
            });

            for (var i = endpoint.Metadata.Count - 1; i >= 0; i--)
                if (endpoint.Metadata[i] is ResourceAssetCollection or ImportMapDefinition
                    || endpoint.Metadata[i].GetType().Name is "ResourceCollectionUrlMetadata" or "ResourcePreloadCollection")
                    endpoint.Metadata.RemoveAt(i);

            endpoint.Metadata.Add(own);
            endpoint.Metadata.Add(ImportMapDefinition.FromResourceCollection(own));
            if (file is not null) {
                endpoint.Metadata.Add(file);
                endpoint.Metadata.Add(Activator.CreateInstance(ResourceCollectionUrlMetadataType, $"./{file.Url}")!);
            }
        });
        return builder;
    }

    /// <summary>
    /// Serves each area's resource collection at <c>/{area}/_framework/resource-collection[.{fingerprint}].js</c>, to
    /// users who may open the area only. The WebAssembly client imports it at startup to resolve <c>@Assets[...]</c>.
    /// </summary>
    public static void MapClientAreaResourceCollections(this IEndpointRouteBuilder endpoints)
    {
        foreach (var area in ClientArea.All) {
            var routes = endpoints.MapGroup($"/{area.StaticWebAssetBasePath}/_framework")
                .RequireAuthorization(new AuthorizeAttribute { Roles = area.RequiredRole });
            routes.MapGet("resource-collection.js", context => ServeResourceCollection(context, area, fingerprint: null));
            routes.MapGet("resource-collection.{fingerprint}.js", context =>
                ServeResourceCollection(context, area, (string?)context.Request.RouteValues["fingerprint"]));
        }
    }

    private static Task ServeResourceCollection(HttpContext context, ClientArea area, string? fingerprint)
    {
        var file = context.RequestServices.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<ClientAreaResourceCollection>())
            .FirstOrDefault(candidate => candidate?.Area == area);
        if (file is null || fingerprint is not null && fingerprint != file.Fingerprint) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        context.Response.ContentType = "text/javascript";
        context.Response.Headers.CacheControl = fingerprint is null ? "no-cache" : "max-age=31536000, immutable";
        return context.Response.Body.WriteAsync(file.Content).AsTask();
    }

    // Internal to Microsoft.AspNetCore.Components.Endpoints; the prerenderer hands its url to the WebAssembly client.
    private static readonly Type ResourceCollectionUrlMetadataType = typeof(RazorComponentsEndpointConventionBuilder).Assembly
        .GetType("Microsoft.AspNetCore.Components.Endpoints.ResourceCollectionUrlMetadata", throwOnError: true)!;

    /// <summary>
    /// Hides the app-wide <c>_framework/resource-collection.js</c> that <c>MapRazorComponents</c> maps for WebAssembly.
    /// It lists the files of every client; <see cref="MapClientAreaResourceCollections"/> serves one per area instead.
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

/// <summary>The <c>resource-collection.js</c> module of one area, in the format the WebAssembly client imports.</summary>
public sealed class ClientAreaResourceCollection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ClientAreaResourceCollection(ClientArea area, ResourceAssetCollection collection)
    {
        Area = area;
        Content = Encoding.UTF8.GetBytes(
            $"export function get() {{ return {JsonSerializer.Serialize((IReadOnlyList<ResourceAsset>)collection, JsonOptions)}; }}");
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Content))[..10];
    }

    public ClientArea Area { get; }

    public byte[] Content { get; }

    public string Fingerprint { get; }

    public string Url => $"{Area.StaticWebAssetBasePath}/_framework/resource-collection.{Fingerprint}.js";
}
