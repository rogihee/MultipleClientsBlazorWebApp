namespace MultipleClientsWeb.Tests;

/// <summary>
/// Request level checks of the host: static SSR outside the areas, and per area only that area's WebAssembly client.
/// </summary>
public partial class HostTests
{
    [ClassDataSource<WebApplicationFactory<Program>>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory<Program> Factory { get; init; }

    [Test]
    [Arguments("/")]
    [Arguments("/login")]
    public async Task Get_StaticPage_LoadsNoWebAssemblyClient(string path)
    {
        var html = await CreateClient().GetStringAsync(path);

        await Assert.That(html).Contains("<script>Blazor.start();</script>");
        await Assert.That(html).DoesNotContain("loadBootResource");
        await Assert.That(html).DoesNotContain("rel=\"preload\"");
    }

    [Test]
    [Arguments("/admin")]
    [Arguments("/game")]
    public async Task Get_AreaAnonymous_RedirectsToLogin(string path)
    {
        var response = await CreateClient().GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location?.PathAndQuery).IsEqualTo($"/login?ReturnUrl=%2F{path[1..]}");
    }

    [Test]
    public async Task Get_AdminAreaAsPlayer_RedirectsToLogin()
    {
        var client = await LogInAsync("Player");

        var response = await client.GetAsync("/admin");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location?.AbsolutePath).IsEqualTo("/login");
    }

    [Test]
    [Arguments("/admin", "Admin", "admin-client", "game-client", "Admin dashboard")]
    [Arguments("/admin/users", "Admin", "admin-client", "game-client", "Users")]
    [Arguments("/game", "Player", "game-client", "admin-client", "Guess the number")]
    public async Task Get_AreaAuthorized_BootsOnlyItsOwnClient(string path, string role, string ownClient, string otherClient, string heading)
    {
        var client = await LogInAsync(role);

        var html = await client.GetStringAsync(path);

        await Assert.That(html).Contains($"<h1>{heading}</h1>");
        await Assert.That(html).Contains($"`${{document.baseURI}}{ownClient}/_framework/${{name}}`");
        await Assert.That(html).Contains($"<link rel=\"preload\" href=\"{ownClient}/_framework/dotnet.");
        await Assert.That(html).DoesNotContain($"href=\"{otherClient}/_framework/dotnet.");
    }

    [Test]
    [Arguments("/", "Player", null, null)]
    [Arguments("/login", "Player", null, null)]
    [Arguments("/admin", "Admin", "admin-client", "_content/AdminOnlyLib/admin-tools.js")]
    [Arguments("/game", "Player", "game-client", "_content/GameOnlyLib/game-tools.js")]
    public async Task Get_Page_ImportMapListsOnlyItsOwnClient(string path, string role, string? ownClient, string? ownLibraryModule)
    {
        var client = await LogInAsync(role);

        var importMap = ImportMap().Match(await client.GetStringAsync(path)).Value;

        await Assert.That(importMap).Contains("_framework/blazor.web.js");
        await Assert.That(importMap).DoesNotContain("resource-collection");
        foreach (var basePath in ClientArea.All.Where(area => area.StaticWebAssetBasePath != ownClient).SelectMany(area => area.AssetBasePaths))
            await Assert.That(importMap).DoesNotContain($"{basePath}/");
        if (ownClient is not null)
            await Assert.That(importMap).Contains($"./{ownClient}/_framework/dotnet.js");
        if (ownLibraryModule is not null)
            await Assert.That(importMap).Contains($"\"./{ownLibraryModule}\"");
    }

    [Test]
    public async Task Get_HostCssBundle_LeavesOutAreaOnlyLibraries()
    {
        var bundle = await CreateClient().GetStringAsync("/MultipleClientsWeb.styles.css");

        await Assert.That(bundle).Contains("@import '_content/MultipleClientShared/");
        foreach (var library in ClientArea.All.SelectMany(area => area.Libraries))
            await Assert.That(bundle).DoesNotContain($"_content/{library}/");
    }

    [Test]
    [Arguments("/admin", "Admin", "AdminOnlyLib", "GameOnlyLib")]
    [Arguments("/game", "Player", "GameOnlyLib", "AdminOnlyLib")]
    public async Task Get_AreaPage_LinksOnlyItsOwnLibraryCss(string path, string role, string ownLibrary, string otherLibrary)
    {
        var client = await LogInAsync(role);

        var html = await client.GetStringAsync(path);

        await Assert.That(html).Contains($"<link rel=\"stylesheet\" href=\"_content/{ownLibrary}/{ownLibrary}.");
        await Assert.That(html).DoesNotContain(otherLibrary);
    }

    [Test]
    [Arguments("/admin", "Admin", "admin-client/images/admin-picture")]
    [Arguments("/game", "Player", "game-client/images/game-picture")]
    public async Task Get_AreaPage_ResolvesItsPicturesThroughAssets(string path, string role, string ownPicture)
    {
        var client = await LogInAsync(role);

        var html = await client.GetStringAsync(path);

        await Assert.That(html).Matches($"<img src=\"{ownPicture}\\.[a-z0-9]+\\.svg\"");
        await Assert.That(html).Matches("<img src=\"_content/MultipleClientShared/images/shared-picture\\.[a-z0-9]+\\.svg\"");
    }

    [Test]
    public async Task Get_Home_LeavesAnotherAreasAssetUnresolved()
    {
        var html = await CreateClient().GetStringAsync("/");

        await Assert.That(html).Contains("<code>admin-client/images/admin-picture.svg</code>");
        await Assert.That(html).DoesNotMatch("admin-picture\\.[a-z0-9]+\\.svg");
    }

    [Test]
    [Arguments("admin-client", "Admin", "AdminOnlyLib", "game-client")]
    [Arguments("game-client", "Player", "GameOnlyLib", "admin-client")]
    public async Task Get_AreaResourceCollection_ListsOnlyItsOwnArea(string basePath, string role, string ownLibrary, string otherBasePath)
    {
        var client = await LogInAsync(role);

        var response = await client.GetAsync($"/{basePath}/_framework/resource-collection.js");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(content).StartsWith("export function get() {");
        await Assert.That(content).Contains($"\"{basePath}/images/");
        await Assert.That(content).Contains($"\"_content/{ownLibrary}/");
        await Assert.That(content).Contains("\"_content/MultipleClientShared/images/shared-picture.");
        foreach (var other in ClientArea.All.Where(area => area.StaticWebAssetBasePath == otherBasePath).SelectMany(area => area.AssetBasePaths))
            await Assert.That(content).DoesNotContain($"\"{other}/");
    }

    [Test]
    [Arguments("admin-client/_framework/resource-collection.js", null)]
    [Arguments("admin-client/_framework/resource-collection.js", "Player")]
    [Arguments("game-client/_framework/resource-collection.js", null)]
    public async Task Get_AreaResourceCollectionWithoutAccess_RedirectsToLogin(string url, string? role)
    {
        var client = role is null ? CreateClient() : await LogInAsync(role);

        var response = await client.GetAsync(url);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location?.AbsolutePath).IsEqualTo("/login");
    }

    [Test]
    public async Task Get_AreaResourceCollectionWithWrongFingerprint_IsNotFound()
    {
        var client = await LogInAsync("Player");

        var response = await client.GetAsync("/game-client/_framework/resource-collection.0000000000.js");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("/_framework/resource-collection.js")]
    [Arguments("/_framework/resource-collection.js.gz")]
    public async Task Get_AppWideResourceCollection_IsNotFound(string path)
    {
        var response = await CreateClient().GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("admin-client/_framework/dotnet.js", "Admin")]
    [Arguments("game-client/_framework/dotnet.js", "Player")]
    [Arguments("game-client/_framework/dotnet.js", "Admin")]
    [Arguments("admin-client/_framework/AdminOnlyLib.wasm", "Admin")]
    [Arguments("_content/AdminOnlyLib/admin-tools.js", "Admin")]
    [Arguments("_content/GameOnlyLib/game-tools.js", "Player")]
    [Arguments("_content/GameOnlyLib/game-tools.js", "Admin")]
    public async Task Get_ClientFileWithAccessToItsArea_IsServedPrivately(string url, string role)
    {
        var client = await LogInAsync(role);

        var response = await client.GetAsync(url);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
    }

    [Test]
    [Arguments("admin-client/_framework/dotnet.js", null)]
    [Arguments("admin-client/_framework/MultipleClientAdmin.wasm", null)]
    [Arguments("game-client/_framework/dotnet.js", null)]
    [Arguments("admin-client/_framework/dotnet.js", "Player")]
    [Arguments("admin-client/_framework/MultipleClientAdmin.wasm", "Player")]
    [Arguments("admin-client/_framework/AdminOnlyLib.wasm", "Player")]
    [Arguments("_content/AdminOnlyLib/admin-tools.js", null)]
    [Arguments("_content/AdminOnlyLib/admin-tools.js", "Player")]
    [Arguments("_content/AdminOnlyLib/AdminOnlyLib.bundle.scp.css", "Player")]
    [Arguments("game-client/_framework/GameOnlyLib.wasm", null)]
    [Arguments("_content/GameOnlyLib/game-tools.js", null)]
    [Arguments("_content/GameOnlyLib/GameOnlyLib.bundle.scp.css", null)]
    public async Task Get_ClientFileWithoutAccessToItsArea_RedirectsToLogin(string url, string? role)
    {
        var client = role is null ? CreateClient() : await LogInAsync(role);

        var response = await client.GetAsync(url);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location?.AbsolutePath).IsEqualTo("/login");
    }

    private HttpClient CreateClient() => Factory.CreateClient(new() { AllowAutoRedirect = false });

    private async Task<HttpClient> LogInAsync(string role)
    {
        var client = CreateClient();
        var loginPage = await client.GetStringAsync("/login");
        var token = AntiforgeryToken().Match(loginPage).Groups[1].Value;

        var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Model.UserName"] = "alice",
            ["Model.Role"] = role
        }));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        return client;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("<script type=\"importmap\">.*?</script>", RegexOptions.Singleline)]
    private static partial Regex ImportMap();
}
