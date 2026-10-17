namespace MultipleClientsWeb.Tests;

public class ClientAreaTests
{
    [Test]
    [Arguments("/admin", "admin-client")]
    [Arguments("/admin/users", "admin-client")]
    [Arguments("/ADMIN", "admin-client")]
    [Arguments("/game", "game-client")]
    [Arguments("/game/level/1", "game-client")]
    [Arguments("/", null)]
    [Arguments("/login", null)]
    [Arguments("/administrator", null)]
    [Arguments("/admin-client/_framework/dotnet.js", null)]
    public async Task FromPath_Path_ReturnsAreaOfThePath(string path, string? expectedAssetBasePath) =>
        await Assert.That(ClientArea.FromPath(new PathString(path))?.StaticWebAssetBasePath).IsEqualTo(expectedAssetBasePath);

    [Test]
    [Arguments("/admin-client/_framework/dotnet.js", "admin-client")]
    [Arguments("/Game-Client/_framework/dotnet.js", "game-client")]
    [Arguments("/_content/AdminOnlyLib/admin-tools.js", "admin-client")]
    [Arguments("/_content/GameOnlyLib/game-tools.js", "game-client")]
    [Arguments("/_content/MultipleClientShared/shared.css", null)]
    [Arguments("/_content/AdminOnlyLibX/admin-tools.js", null)]
    [Arguments("/admin/users", null)]
    [Arguments("/admin-clientx/_framework/dotnet.js", null)]
    [Arguments("/_framework/blazor.web.js", null)]
    public async Task FromAssetPath_Path_ReturnsAreaThatOwnsTheFile(string path, string? expectedAssetBasePath) =>
        await Assert.That(ClientArea.FromAssetPath(new PathString(path))?.StaticWebAssetBasePath).IsEqualTo(expectedAssetBasePath);

    [Test]
    [Arguments("/_framework/resource-collection.js", true)]
    [Arguments("/_framework/resource-collection.TEuF6.js.gz", true)]
    [Arguments("/_framework/blazor.web.js", false)]
    [Arguments("/admin-client/_framework/resource-collection.js", false)]
    public async Task IsAppWideResourceCollection_Path_MatchesOnlyTheRootCollection(string path, bool expected) =>
        await Assert.That(ClientAssetIsolation.IsAppWideResourceCollection(new PathString(path))).IsEqualTo(expected);
}
