namespace MultipleClientsWeb.Tests;

public partial class StaticAssetCachingTests
{
    [ClassDataSource<WebApplicationFactory<Program>>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory<Program> Factory { get; init; }

    [Test]
    [Arguments("/_content/MultipleClientShared/shared.css", true)]
    [Arguments("/_framework/blazor.web.js", true)]
    [Arguments("/admin-client/_framework/dotnet.js", true)]
    [Arguments("/game-client/_framework/dotnet.js", true)]
    [Arguments("/app.css", false)]
    [Arguments("/admin", false)]
    [Arguments("/_contentx/file.css", false)]
    public async Task IsSharedCacheDenied_Path_DeniesLibraryAndFrameworkFiles(string path, bool expected) =>
        await Assert.That(StaticAssetCaching.IsSharedCacheDenied(new PathString(path))).IsEqualTo(expected);

    [Test]
    [Arguments("no-cache", "private, no-cache")]
    [Arguments("No-Cache", "private, no-cache")]
    [Arguments("max-age=31536000, immutable", StaticAssetCaching.PrivateCacheControl)]
    [Arguments("", StaticAssetCaching.PrivateCacheControl)]
    public async Task GetPrivateCacheControl_CurrentValue_KeepsRevalidationOfUnfingerprintedFiles(string current, string expected) =>
        await Assert.That(StaticAssetCaching.GetPrivateCacheControl(current)).IsEqualTo(expected);

    [Test]
    [Arguments("_content/MultipleClientShared/images/logo.svg?v=1.0.0")]
    [Arguments("_framework/blazor.web.js")]
    public async Task Get_LibraryOrFrameworkFile_IsPrivate(string url)
    {
        var response = await Factory.CreateClient().GetAsync(url);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
    }

    [Test]
    public async Task Get_HostFile_IsNotPrivate()
    {
        var response = await Factory.CreateClient().GetAsync("app.css");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.Private).IsNotEqualTo(true);
    }

    [Test]
    public async Task Get_LibraryScopedCssBundle_HasVersionedImageUrls()
    {
        var client = Factory.CreateClient();
        var hostStyles = await client.GetStringAsync("MultipleClientsWeb.styles.css");
        var import = LibraryBundleImport().Match(hostStyles);

        await Assert.That(import.Success).IsTrue();
        var bundle = await client.GetStringAsync(import.Groups[1].Value);
        await Assert.That(bundle).Contains($"images/badge-bg.svg?v={MultipleClientShared.SharedAssets.Version}");
    }

    [GeneratedRegex(@"@import '(_content/MultipleClientShared/MultipleClientShared\.\w+\.bundle\.scp\.css)'")]
    private static partial Regex LibraryBundleImport();
}
