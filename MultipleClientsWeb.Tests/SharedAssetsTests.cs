using MultipleClientShared;

namespace MultipleClientsWeb.Tests;

public class SharedAssetsTests
{
    [Test]
    public async Task Version_Default_HasNoSourceRevision() =>
        await Assert.That(SharedAssets.Version).IsNotEmpty().And.DoesNotContain("+");

    [Test]
    [Arguments("_content/x/a.svg", "_content/x/a.svg?v=")]
    [Arguments("_content/x/a.svg?theme=dark", "_content/x/a.svg?theme=dark&v=")]
    public async Task AppendVersion_Url_AddsVersionQuery(string url, string expectedPrefix) =>
        await Assert.That(SharedAssets.AppendVersion(url)).IsEqualTo(expectedPrefix + SharedAssets.Version);

    [Test]
    public async Task ModuleUrl_Path_IsRelativeAndVersioned() =>
        await Assert.That(SharedAssets.ModuleUrl(SharedAssets.AssetInfoModule))
            .IsEqualTo($"./_content/MultipleClientShared/js/asset-info.js?v={SharedAssets.Version}");
}
