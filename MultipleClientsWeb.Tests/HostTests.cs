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
    [Arguments("admin-client")]
    [Arguments("game-client")]
    public async Task Get_ClientRuntime_IsServedFromItsBasePath(string assetBasePath)
    {
        var response = await CreateClient().GetAsync($"/{assetBasePath}/_framework/dotnet.js");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
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
}
