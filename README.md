# Multiple WebAssembly clients in one Blazor Web App

A sample of one .NET 10 **Blazor Web App** that serves static SSR pages (home, login) and hosts **several
Interactive WebAssembly clients**, each in its own area of the site. It is an alternative to the older hosted model,
where every area is a standalone WebAssembly app behind a Razor Pages host page and always shows a loading screen.

**Live demo:** <https://multiple-clients-blazor.azurewebsites.net> (log in with any user name and pick a role).

| Project | Role | URL | Static web asset base path |
| --- | --- | --- | --- |
| `MultipleClientsWeb` | Host, static SSR pages, cookie authentication | `/`, `/login` | – |
| `MultipleClientAdmin` | WebAssembly client, role `Admin` | `/admin/**` | `admin-client` |
| `MultipleClientGame` | WebAssembly client, any logged-in user | `/game/**` | `game-client` |
| `MultipleClientShared` | Razor class library used by the host and both clients (cache-busting examples) | – | `_content/MultipleClientShared` |
| `MultipleClientsWeb.Tests` | TUnit request-level tests of the host | – | – |

```
dotnet run --project MultipleClientsWeb --launch-profile http
dotnet test --solution MultipleClientApps.slnx
```

## How it works

There is no official guidance for this setup in .NET 8 to 10 (see dotnet/aspnetcore#52216, dotnet/aspnetcore#62249
and dotnet/aspnetcore#65910, all still open). This setup copies what the aspnetcore repository does in its own
`Components.TestServer` test assets (`GlobalInteractivityApp.razor`, `RemoteAuthenticationApp.razor`):

1. **A separate `StaticWebAssetBasePath` for each client.** Both clients publish a `_framework` folder. Without
   separate base paths the SDK fails with *Conflicting assets with the same target path*. With them, the runtimes are
   served from `/admin-client/_framework/` and `/game-client/_framework/`.
2. **One `MapRazorComponents<App>()`** with `AddAdditionalAssemblies` for both clients. Client pages are routed
   by their full URL (`@page "/admin/users"`), so there is one endpoint table and server-side `[Authorize]` works
   unchanged. An anonymous user is redirected to the static SSR login page before any WebAssembly is downloaded.
3. **`App.razor` picks the area by request path** (`ClientArea.FromPath`):
   - **Outside an area** it renders the host's `Routes` as static SSR. `blazor.web.js` only handles enhanced
     navigation there, and no runtime is loaded.
   - **Inside an area** it renders that client's own `Routes` component with `@rendermode="InteractiveWebAssembly"`.
     The page is prerendered and then becomes interactive.
4. **`Blazor.start({ webAssembly: { loadBootResource } })`** points the runtime loader at the area's base path.
   `blazor.web.js` itself always looks for `/_framework/dotnet.js`, which does not exist when a client has its own
   base path. The import map from `<ImportMap />` then resolves the fingerprinted file names.
5. **The authentication state** flows from the host to each client through
   `AddAuthenticationStateSerialization` and `AddAuthenticationStateDeserialization`. Each client's router is an
   `AuthorizeRouteView`.
6. **Each client's files are private to its area** (`ClientAssetIsolation`). By default every page's import map,
   and a public `_framework/resource-collection.js`, list the files of *all* clients, and anyone can download
   `/admin-client/_framework/MultipleClientAdmin.wasm`. Because the assembly names are predictable, hiding the lists
   alone would not be enough, so this sample does three things:
   - `MapStaticAssets().RequireClientAreaAuthorization()` puts an `[Authorize]` (with `ClientArea.RequiredRole`) on
     every file under a client's base path. Without access, the request is redirected to the login page.
   - `MapRazorComponents<App>()...WithClientAreaAssetsOnly()` replaces the app-wide asset collection of each page
     with the host's files, the shared libraries and that page's own area. `<ImportMap />` and `@Assets[...]` pick it up
     without changes.
   - It also drops the url of the app-wide resource collection, and `UseNoAppWideResourceCollection` answers it with 404.
     Client components therefore get an empty `Assets` collection, so they must build urls themselves (see
     `SharedAssets`).

## Caveats found

- **One WebAssembly runtime per page.** `Blazor.start` can only run once, so moving from one area to another, or
  from SSR into an area, must be a full page load:
  - on SSR pages, put `data-enhance-nav="false"` on links into an area;
  - inside a client, use `NavigateTo(..., forceLoad: true)`;
  - after the login post, redirect with `forceLoad: true`.
- **Don't use `<ResourcePreloader />`.** It preloads the runtime of *every* referenced client on every page.
  `App.razor` adds a `<link rel="preload">` for the current area's `dotnet.js` only.
- **The framework shares the import map and resource collection across clients** unless you scope them (step 6). The
  shared lists reveal which assemblies, and so which features, every client contains. `ClientAssetIsolation` uses
  public APIs, except that it removes two internal metadata types by name (`ResourceCollectionUrlMetadata`,
  `ResourcePreloadCollection`). Recheck this when you upgrade .NET.
- **JS initializers (`*.lib.module.js`) of all clients stay public.** `blazor.web.js` loads the initializers of every
  client on every page, including the static SSR pages. In this sample they only exist in Development (the hot reload
  module), and the `Blazor-Web-Initializers` comment lists them there.
- **`_content/*` from Razor class libraries stays at the site root** (dotnet/aspnetcore#62249), not under a client's
  base path. With `<base href="/">` this works without URL rewrites for `/<area>/_content/...`: the
  `MultipleClientShared` images, CSS and JS module load from `/_content/MultipleClientShared/` in both clients.
- **Client routes must not overlap**, because there is a single endpoint table. Prefixing every page with its
  area handles this.
- **Hot reload of the clients is not verified.** dotnet/aspnetcore#62249 reports that it needs a rewrite of
  `/{client}/_content/Microsoft.DotNet.HotReload.WebAssembly.Browser/*`.

## Compared with the hosted model

The hosted model has one standalone WebAssembly app per area (e.g. an admin app with base path `admin`), each behind
a Razor Pages host page:

| | Hosted model | This sample |
| --- | --- | --- |
| Host page | Razor Pages `MapFallbackToPage("admin/{*path}")` | `MapRazorComponents<App>`, one route per page |
| First paint | Loading spinner until WebAssembly runs | Prerendered HTML, then interactive |
| Login | Razor Pages | Static SSR Razor component |
| Boot script | `blazor.webassembly.js` + manual fingerprint map in `loadBootResource` | `blazor.web.js` + import map, `loadBootResource` only swaps the folder |
| Asset paths | URL rewrites for `/<area>/_content/...`, a hand-built `ResourceAssetCollection` | none needed so far |

## Cache busting

The home page shows the URLs each technique below produces, and the `VersionBadge` from `MultipleClientShared` uses
them in the SSR pages and in both clients.

| Technique | Where in this sample |
| --- | --- |
| **Fingerprinted URLs through `@Assets[...]`.** `MapStaticAssets` serves them with `max-age=31536000, immutable`. | `App.razor`: `app.css`, favicon, `SharedAssets.CssBundle` |
| **A version query on URLs that code builds itself** (library code, `import()` of JS modules). | `SharedAssets.AppendVersion`, `SharedAssets.ModuleUrl` |
| **A version query on image `url()`s in scoped CSS**, rewritten after `BundleScopedCssFiles` by the `VersionCssUrls` task in `Directory.Build.targets`. The SDK does not fingerprint `url()`s inside scoped CSS. | `MultipleClientShared.csproj`, `VersionBadge.razor.css` |
| **A fingerprinted `@import` of a library's scoped CSS bundle.** | Done by the .NET 10 SDK, no rewrite needed |
| **The client runtime resolved through the import map.** `dotnet.js` itself stays unfingerprinted and `no-cache`, so the browser always gets the current boot manifest. Each page's import map only lists its own client. | `<ImportMap />` + `loadBootResource` in `App.razor`, `WithClientAreaAssetsOnly` |
| **No shared caches for `_content` and `_framework`.** These responses get `private, max-age=86400, must-revalidate`, or `private, no-cache` where `MapStaticAssets` sent `no-cache`, so a CDN never serves an old library file or boot manifest behind an unchanged URL. | `StaticAssetCaching.UsePrivateLibraryAndFrameworkCaching` |

`$(Version)` is set in `Directory.Build.props`; CI sets it per deployment so every version query changes.

Response headers of a published build (`ASPNETCORE_ENVIRONMENT=Production`; in Development `MapStaticAssets`
sends `no-cache` for everything):

| URL | Cache-Control |
| --- | --- |
| `/app.{hash}.css`, `/MultipleClientsWeb.{hash}.styles.css` | `max-age=31536000, immutable` |
| `/_content/MultipleClientShared/shared.{hash}.css`, `/_framework/blazor.web.{hash}.js` | `private, max-age=86400, must-revalidate` |
| `/_content/MultipleClientShared/images/logo.svg?v=1.0.0` | `private, max-age=86400, must-revalidate` |
| `/_content/MultipleClientShared/js/asset-info.js?v=1.0.0`, `/admin-client/_framework/dotnet.js` | `private, no-cache` |
