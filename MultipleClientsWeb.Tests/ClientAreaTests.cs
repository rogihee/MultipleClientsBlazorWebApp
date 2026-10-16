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
}
